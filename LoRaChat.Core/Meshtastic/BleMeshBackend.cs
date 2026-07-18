using LoRaChat.Core.Abstractions;

namespace LoRaChat.Core.Meshtastic;

/// <summary>
/// BLE transport for the Meshtastic protocol. Protocol logic lives in <see cref="MeshBackendBase"/>;
/// this class only manages the GATT connection. Unlike serial there is no <c>0x94 0xC3</c> framing —
/// each read of the FROMRADIO characteristic yields exactly one <c>FromRadio</c>, and a FROMNUM
/// notification signals that packets are waiting to be drained. Works with any
/// <see cref="IBleProvider"/> (desktop or Android).
/// </summary>
public sealed class BleMeshBackend : MeshBackendBase
{
    private readonly IBleProvider _ble;
    private IBleMeshConnection? _conn;
    private int _draining;

    public BleMeshBackend(IBleProvider ble) => _ble = ble;

    protected override async Task OpenTransportAsync(ConnectTarget target, CancellationToken ct)
    {
        if (target.Kind != "ble") throw new NotSupportedException($"BleMeshBackend can't handle transport '{target.Kind}'.");

        _conn = await _ble.ConnectAsync(target.Id, ct);
        _conn.PacketsAvailable += (_, _) => _ = DrainAsync();
        LogLine($"BLE device {target.Id} connected.");

        // The initial config dump may already be queued before we subscribe — drain proactively.
        _ = DrainAsync();
    }

    protected override async Task CloseTransportAsync()
    {
        if (_conn != null)
        {
            try { await _conn.DisposeAsync(); } catch { }
            _conn = null;
            LogLine("BLE device disconnected.");
        }
    }

    protected override Task SendToRadioAsync(byte[] toRadioPayload, CancellationToken ct = default)
        => _conn == null ? Task.CompletedTask : _conn.SendToRadioAsync(toRadioPayload, ct).AsTask();

    private async Task DrainAsync()
    {
        // Serialize drains so overlapping FROMNUM notifications don't interleave reads.
        if (Interlocked.Exchange(ref _draining, 1) == 1) return;
        try
        {
            var conn = _conn;
            while (conn != null)
            {
                byte[]? payload;
                try { payload = await conn.ReadFromRadioAsync(); }
                catch (Exception ex) { OnTransportFault(ex.Message); return; }
                if (payload == null || payload.Length == 0) break;
                FeedFromRadio(payload);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _draining, 0);
        }
    }
}
