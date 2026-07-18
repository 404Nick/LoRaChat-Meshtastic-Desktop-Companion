using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Hardware.Usb;
using AApplication = Android.App.Application;
using LoRaChat.Core.Abstractions;

namespace LoRaChat.Host;

/// <summary>
/// Android USB-OTG serial provider implementing the USB CDC-ACM class (native-USB radios such as
/// nRF52 boards). Chip-specific UARTs (CP210x, CH340, FTDI) need the usb-serial-for-android binding —
/// on those, prefer the BLE transport. Enumerates attached USB devices for the port dropdown.
/// </summary>
internal sealed class AndroidSerialProvider : ISerialProvider
{
    private const int CdcSetLineCoding = 0x20;
    private const int CdcSetControlLineState = 0x22;

    private static UsbManager? Manager =>
        AApplication.Context.GetSystemService(Context.UsbService) as UsbManager;

    public IReadOnlyList<string> GetPortNames()
    {
        var list = new List<string>();
        var mgr = Manager;
        if (mgr?.DeviceList == null) return list;
        foreach (var dev in mgr.DeviceList.Values)
            if (dev != null) list.Add(dev.DeviceName ?? $"usb:{dev.DeviceId}");
        return list;
    }

    public Task<IRawByteStream> OpenAsync(string portName, int baudRate, CancellationToken ct = default)
    {
        var mgr = Manager ?? throw new InvalidOperationException("USB service unavailable.");
        UsbDevice? device = null;
        foreach (var d in mgr.DeviceList!.Values)
            if (d != null && (d.DeviceName == portName || $"usb:{d.DeviceId}" == portName)) { device = d; break; }
        if (device == null) throw new InvalidOperationException($"USB device {portName} not attached.");
        if (!mgr.HasPermission(device))
            throw new InvalidOperationException("USB permission not granted. Reconnect the device and accept the permission dialog, or use BLE.");

        // Find the CDC data interface (class 0x0A) with two bulk endpoints.
        UsbInterface? data = null;
        for (int i = 0; i < device.InterfaceCount; i++)
        {
            var iface = device.GetInterface(i);
            if (iface.InterfaceClass == UsbClass.CdcData) { data = iface; break; }
        }
        data ??= device.GetInterface(device.InterfaceCount - 1);

        UsbEndpoint? bulkIn = null, bulkOut = null;
        for (int e = 0; e < data.EndpointCount; e++)
        {
            var ep = data.GetEndpoint(e);
            if (ep.Type != UsbAddressing.XferBulk) continue;
            if (ep.Direction == UsbAddressing.In) bulkIn = ep; else bulkOut = ep;
        }
        if (bulkIn == null || bulkOut == null) throw new InvalidOperationException("No CDC bulk endpoints (non-CDC chip — use BLE).");

        var conn = mgr.OpenDevice(device) ?? throw new InvalidOperationException("Failed to open USB device.");
        conn.ClaimInterface(data, true);

        // CDC SET_LINE_CODING (baud, 1 stop bit, no parity, 8 data bits) + SET_CONTROL_LINE_STATE (DTR|RTS).
        byte[] lineCoding =
        {
            (byte)(baudRate & 0xFF), (byte)((baudRate >> 8) & 0xFF), (byte)((baudRate >> 16) & 0xFF), (byte)((baudRate >> 24) & 0xFF),
            0x00, 0x00, 0x08,
        };
        conn.ControlTransfer((UsbAddressing)0x21, CdcSetLineCoding, 0, 0, lineCoding, lineCoding.Length, 2000);
        conn.ControlTransfer((UsbAddressing)0x21, CdcSetControlLineState, 0x03, 0, null, 0, 2000);

        return Task.FromResult<IRawByteStream>(new UsbCdcStream(conn, data, bulkIn, bulkOut));
    }

    private sealed class UsbCdcStream : IRawByteStream
    {
        private readonly UsbDeviceConnection _conn;
        private readonly UsbInterface _iface;
        private readonly UsbEndpoint _in;
        private readonly UsbEndpoint _out;
        private readonly CancellationTokenSource _cts = new();
        private readonly Thread _reader;

        public event EventHandler<ReadOnlyMemory<byte>>? BytesReceived;
        public event EventHandler<Exception>? Faulted;

        public UsbCdcStream(UsbDeviceConnection conn, UsbInterface iface, UsbEndpoint bulkIn, UsbEndpoint bulkOut)
        {
            _conn = conn;
            _iface = iface;
            _in = bulkIn;
            _out = bulkOut;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "usb-cdc-read" };
            _reader.Start();
        }

        private void ReadLoop()
        {
            byte[] buf = new byte[_in.MaxPacketSize > 0 ? _in.MaxPacketSize : 64];
            while (!_cts.IsCancellationRequested)
            {
                int n;
                try { n = _conn.BulkTransfer(_in, buf, buf.Length, 200); }
                catch (Exception ex) { Faulted?.Invoke(this, ex); return; }
                if (n > 0)
                {
                    var copy = new byte[n];
                    Buffer.BlockCopy(buf, 0, copy, 0, n);
                    BytesReceived?.Invoke(this, copy);
                }
            }
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            byte[] arr = data.ToArray();
            int sent = _conn.BulkTransfer(_out, arr, arr.Length, 2000);
            if (sent < 0) Faulted?.Invoke(this, new Exception("USB bulk write failed."));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            try { _cts.Cancel(); } catch { }
            try { _conn.ReleaseInterface(_iface); _conn.Close(); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
