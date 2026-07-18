namespace LoRaChat.Core.Abstractions;

/// <summary>A Bluetooth LE device found while scanning for Meshtastic radios.</summary>
public readonly record struct BleDeviceInfo(string Address, string Name);

/// <summary>
/// A dumb, duplex byte pipe to a serial device. The Meshtastic serial framing
/// (<c>0x94 0xC3</c> + length + protobuf) lives in LoRaChat.Core, so this interface stays a plain
/// stream that each platform can satisfy: desktop via <c>System.IO.Ports.SerialPort</c>, Android via
/// usb-serial-for-android.
/// </summary>
public interface IRawByteStream : IAsyncDisposable
{
    /// <summary>Raised whenever bytes arrive from the device.</summary>
    event EventHandler<ReadOnlyMemory<byte>>? BytesReceived;

    /// <summary>Raised if the underlying connection drops or errors.</summary>
    event EventHandler<Exception>? Faulted;

    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
}

/// <summary>Enumerates and opens serial ports. Implemented per platform.</summary>
public interface ISerialProvider
{
    /// <summary>Names of currently attached serial ports (e.g. "COM5", "/dev/ttyUSB0").
    /// On Android this reflects attached USB-OTG devices.</summary>
    IReadOnlyList<string> GetPortNames();

    Task<IRawByteStream> OpenAsync(string portName, int baudRate, CancellationToken ct = default);
}

/// <summary>
/// Packet-oriented BLE link to a Meshtastic radio. Unlike serial, BLE has no <c>0x94 0xC3</c>
/// framing — each read of the FROMRADIO characteristic yields exactly one <c>FromRadio</c> protobuf,
/// and each write to TORADIO carries one <c>ToRadio</c>. A FROMNUM notification tells us data is
/// waiting to be drained.
/// </summary>
public interface IBleMeshConnection : IAsyncDisposable
{
    /// <summary>Raised when the radio notifies (FROMNUM) that one or more packets are ready to read.</summary>
    event EventHandler? PacketsAvailable;

    /// <summary>Reads the next queued <c>FromRadio</c> protobuf, or null when the queue is drained.</summary>
    Task<byte[]?> ReadFromRadioAsync(CancellationToken ct = default);

    ValueTask SendToRadioAsync(byte[] toRadioPayload, CancellationToken ct = default);
}

/// <summary>Scans for and connects to Meshtastic radios over BLE. Implemented per platform.</summary>
public interface IBleProvider
{
    Task<IReadOnlyList<BleDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct = default);

    Task<IBleMeshConnection> ConnectAsync(string address, CancellationToken ct = default);
}
