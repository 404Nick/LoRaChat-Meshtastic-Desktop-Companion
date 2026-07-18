using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using LoRaChat.Core.Abstractions;

namespace LoRaChat.Host;

/// <summary>Desktop serial enumeration/opening via <see cref="System.IO.Ports"/> (Windows/macOS/Linux).</summary>
public sealed class DesktopSerialProvider : ISerialProvider
{
    public IReadOnlyList<string> GetPortNames()
    {
        var ports = new SortedSet<string>(StringComparer.Ordinal);
        try { foreach (var p in SerialPort.GetPortNames()) ports.Add(p); } catch { }

        // SerialPort.GetPortNames() is unreliable for USB radios on Unix: on Linux it can miss
        // /dev/ttyUSB*/ttyACM*, and on macOS it returns the tty.* devices while a radio should be
        // opened via the cu.* callout device. Scan /dev directly to fill the gaps.
        try
        {
            if (OperatingSystem.IsLinux())
            {
                foreach (var pat in new[] { "ttyUSB*", "ttyACM*" })
                    foreach (var f in Directory.EnumerateFiles("/dev", pat)) ports.Add(f);
            }
            else if (OperatingSystem.IsMacOS())
            {
                foreach (var f in Directory.EnumerateFiles("/dev", "cu.*")) ports.Add(f);
            }
        }
        catch { /* /dev not enumerable — fall back to whatever SerialPort found */ }

        return ports.Count > 0 ? new List<string>(ports) : Array.Empty<string>();
    }

    public Task<IRawByteStream> OpenAsync(string portName, int baudRate, CancellationToken ct = default)
    {
        var port = new SerialPort(portName, baudRate)
        {
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = 2000,
            DtrEnable = true,
            RtsEnable = true,
        };
        port.Open();
        return Task.FromResult<IRawByteStream>(new SerialByteStream(port));
    }

    /// <summary>Adapts a <see cref="SerialPort"/> to the platform-agnostic <see cref="IRawByteStream"/>.
    /// Meshtastic framing over this stream lives in LoRaChat.Core (Phase 2).</summary>
    private sealed class SerialByteStream : IRawByteStream
    {
        private readonly SerialPort _port;

        public event EventHandler<ReadOnlyMemory<byte>>? BytesReceived;
        public event EventHandler<Exception>? Faulted;

        public SerialByteStream(SerialPort port)
        {
            _port = port;
            _port.DataReceived += OnData;
            _port.ErrorReceived += (_, _) => Faulted?.Invoke(this, new Exception("Serial error event"));
        }

        private void OnData(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int n = _port.BytesToRead;
                if (n <= 0) return;
                byte[] buf = new byte[n];
                int read = _port.Read(buf, 0, n);
                if (read > 0) BytesReceived?.Invoke(this, new ReadOnlyMemory<byte>(buf, 0, read));
            }
            catch (Exception ex) { Faulted?.Invoke(this, ex); }
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            _port.Write(data.ToArray(), 0, data.Length);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            try { _port.DataReceived -= OnData; if (_port.IsOpen) _port.Close(); _port.Dispose(); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
