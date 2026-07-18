using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using AApplication = Android.App.Application;
using Java.Util;
using LoRaChat.Core.Abstractions;

namespace LoRaChat.Host;

/// <summary>
/// Native Android BLE provider using <see cref="BluetoothGatt"/>. Scans for and connects to Meshtastic
/// radios over the documented GATT service (FROMRADIO/TORADIO/FROMNUM). This is the primary Android
/// transport. Requires the BLUETOOTH_SCAN/CONNECT (API 31+) or location (older) runtime permissions,
/// declared in AndroidManifest.xml and granted by the user before first use.
/// </summary>
internal sealed class AndroidBleProvider : IBleProvider
{
    // Meshtastic BLE GATT UUIDs.
    internal static readonly UUID ServiceUuid = UUID.FromString("6ba1b218-15a8-461f-9fa8-5dcae273eafd")!;
    internal static readonly UUID ToRadioUuid = UUID.FromString("f75c76d2-129e-4dad-a1dd-7866124401e7")!;
    internal static readonly UUID FromRadioUuid = UUID.FromString("2c55e69e-4993-11ed-b878-0242ac120002")!;
    internal static readonly UUID FromNumUuid = UUID.FromString("ed9da18c-a800-4f66-a670-aa7547e34453")!;
    internal static readonly UUID Cccd = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

    private static BluetoothManager? Manager =>
        AApplication.Context.GetSystemService(Context.BluetoothService) as BluetoothManager;

    public async Task<IReadOnlyList<BleDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var scanner = Manager?.Adapter?.BluetoothLeScanner;
        if (scanner == null) return Array.Empty<BleDeviceInfo>();

        var found = new Dictionary<string, BleDeviceInfo>();
        var callback = new ScanCollector(found);
        var filters = new List<ScanFilter>
        {
            new ScanFilter.Builder()!.SetServiceUuid(new ParcelUuid(ServiceUuid))!.Build()!,
        };
        var settings = new ScanSettings.Builder()!.SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency)!.Build();

        scanner.StartScan(filters, settings, callback);
        try { await Task.Delay(timeout, ct); }
        catch (System.OperationCanceledException) { }
        finally { scanner.StopScan(callback); }

        return new List<BleDeviceInfo>(found.Values);
    }

    public async Task<IBleMeshConnection> ConnectAsync(string address, CancellationToken ct = default)
    {
        var device = Manager?.Adapter?.GetRemoteDevice(address)
            ?? throw new InvalidOperationException($"BLE device {address} not found.");
        var conn = new AndroidBleConnection();
        await conn.OpenAsync(device, ct);
        return conn;
    }

    private sealed class ScanCollector : ScanCallback
    {
        private readonly Dictionary<string, BleDeviceInfo> _found;
        public ScanCollector(Dictionary<string, BleDeviceInfo> found) => _found = found;

        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            var dev = result?.Device;
            if (dev?.Address == null) return;
            _found[dev.Address] = new BleDeviceInfo(dev.Address, dev.Name ?? dev.Address);
        }
    }
}

/// <summary>
/// One Meshtastic BLE GATT connection. Android GATT is callback-based and allows only one operation in
/// flight, so reads/writes are serialized through a semaphore and completed via TaskCompletionSources.
/// FROMNUM notifications raise <see cref="PacketsAvailable"/>; the backend then drains FROMRADIO.
/// </summary>
internal sealed class AndroidBleConnection : BluetoothGattCallback, IBleMeshConnection
{
    private readonly SemaphoreSlim _op = new(1, 1);
    private BluetoothGatt? _gatt;
    private BluetoothGattCharacteristic? _toRadio;
    private BluetoothGattCharacteristic? _fromRadio;
    private BluetoothGattCharacteristic? _fromNum;

    private TaskCompletionSource<bool>? _connected;
    private TaskCompletionSource<bool>? _servicesDiscovered;
    private TaskCompletionSource<byte[]?>? _readResult;
    private TaskCompletionSource<bool>? _writeResult;

    public event EventHandler? PacketsAvailable;

    public async Task OpenAsync(BluetoothDevice device, CancellationToken ct)
    {
        _connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _servicesDiscovered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _gatt = device.ConnectGatt(Android.App.Application.Context, false, this, BluetoothTransports.Le);
        if (_gatt == null) throw new InvalidOperationException("ConnectGatt returned null.");

        using (ct.Register(() => _connected.TrySetCanceled()))
            await _connected.Task;

        _gatt.DiscoverServices();
        await _servicesDiscovered.Task;

        var svc = _gatt.GetService(AndroidBleProvider.ServiceUuid)
            ?? throw new InvalidOperationException("Meshtastic BLE service not found on device.");
        _toRadio = svc.GetCharacteristic(AndroidBleProvider.ToRadioUuid);
        _fromRadio = svc.GetCharacteristic(AndroidBleProvider.FromRadioUuid);
        _fromNum = svc.GetCharacteristic(AndroidBleProvider.FromNumUuid);
        if (_toRadio == null || _fromRadio == null || _fromNum == null)
            throw new InvalidOperationException("Meshtastic BLE characteristics missing.");

        EnableNotifications(_fromNum);
    }

    private void EnableNotifications(BluetoothGattCharacteristic ch)
    {
        _gatt!.SetCharacteristicNotification(ch, true);
        var cccd = ch.GetDescriptor(AndroidBleProvider.Cccd);
        if (cccd != null)
        {
#pragma warning disable CA1422 // legacy descriptor write path for broad API-level support
            cccd.SetValue(BluetoothGattDescriptor.EnableNotificationValue!.ToArray());
            _gatt.WriteDescriptor(cccd);
#pragma warning restore CA1422
        }
    }

    public async Task<byte[]?> ReadFromRadioAsync(CancellationToken ct = default)
    {
        await _op.WaitAsync(ct);
        try
        {
            _readResult = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_gatt!.ReadCharacteristic(_fromRadio!)) return null;
            using (ct.Register(() => _readResult.TrySetResult(null)))
                return await _readResult.Task;
        }
        finally { _op.Release(); }
    }

    public async ValueTask SendToRadioAsync(byte[] payload, CancellationToken ct = default)
    {
        await _op.WaitAsync(ct);
        try
        {
            _writeResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CA1422 // legacy write path for broad API-level support
            _toRadio!.WriteType = GattWriteType.Default;
            _toRadio.SetValue(payload);
            _gatt!.WriteCharacteristic(_toRadio);
#pragma warning restore CA1422
            using (ct.Register(() => _writeResult.TrySetResult(false)))
                await _writeResult.Task;
        }
        finally { _op.Release(); }
    }

    // ---- GATT callbacks ----

    public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
    {
        if (newState == ProfileState.Connected) _connected?.TrySetResult(true);
        else if (newState == ProfileState.Disconnected) _connected?.TrySetException(new Exception("BLE disconnected during connect."));
    }

    public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
        => _servicesDiscovered?.TrySetResult(status == GattStatus.Success);

#pragma warning disable CA1422 // legacy value-getter callbacks for broad API-level support
    public override void OnCharacteristicRead(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
        => _readResult?.TrySetResult(status == GattStatus.Success ? characteristic?.GetValue() : null);

    public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
    {
        if (characteristic?.Uuid?.Equals(AndroidBleProvider.FromNumUuid) == true)
            PacketsAvailable?.Invoke(this, EventArgs.Empty);
    }
#pragma warning restore CA1422

    public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
        => _writeResult?.TrySetResult(status == GattStatus.Success);

    public ValueTask DisposeAsync()
    {
        try { _gatt?.Disconnect(); _gatt?.Close(); } catch { }
        _gatt = null;
        return ValueTask.CompletedTask;
    }
}
