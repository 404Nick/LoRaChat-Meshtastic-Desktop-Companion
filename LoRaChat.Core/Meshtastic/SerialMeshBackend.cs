using LoRaChat.Core.Abstractions;

namespace LoRaChat.Core.Meshtastic;

/// <summary>
/// Serial transport for the Meshtastic protocol. All protocol logic lives in
/// <see cref="MeshBackendBase"/>; this class only opens the port, applies the 0x94 0xC3 framing on the
/// way out, and de-frames the way in. The same code path serves the desktop <c>System.IO.Ports</c>
/// provider and the Android USB-serial provider (Phase 4) — both satisfy <see cref="ISerialProvider"/>.
/// </summary>
public sealed class SerialMeshBackend : MeshBackendBase
{
    private const int DefaultBaud = 115200;

    private readonly ISerialProvider _serial;
    private readonly SerialDeframer _deframer = new();
    private IRawByteStream? _stream;

    public SerialMeshBackend(ISerialProvider serial)
    {
        _serial = serial;
        _deframer.DebugText += (_, t) => LogLine($"[radio] {t}");
    }

    protected override async Task OpenTransportAsync(ConnectTarget target, CancellationToken ct)
    {
        if (target.Kind != "serial") throw new NotSupportedException($"SerialMeshBackend can't handle transport '{target.Kind}'.");

        _stream = await _serial.OpenAsync(target.Id, DefaultBaud, ct);
        _stream.BytesReceived += OnBytes;
        _stream.Faulted += (_, ex) => OnTransportFault(ex.Message);
        LogLine($"Serial port {target.Id} opened at {DefaultBaud} baud.");

        // Wake the serial link / flush any partial frame in the radio's parser before requesting config.
        await _stream.WriteAsync(new byte[32], ct);
    }

    protected override async Task CloseTransportAsync()
    {
        if (_stream != null)
        {
            _stream.BytesReceived -= OnBytes;
            try { await _stream.DisposeAsync(); } catch { }
            _stream = null;
            LogLine("Serial port closed.");
        }
    }

    protected override Task SendToRadioAsync(byte[] toRadioPayload, CancellationToken ct = default)
    {
        if (_stream == null) return Task.CompletedTask;
        return _stream.WriteAsync(SerialDeframer.Frame(toRadioPayload), ct).AsTask();
    }

    private void OnBytes(object? sender, ReadOnlyMemory<byte> data)
    {
        foreach (byte[] frame in _deframer.Push(data.Span)) FeedFromRadio(frame);
    }
}
