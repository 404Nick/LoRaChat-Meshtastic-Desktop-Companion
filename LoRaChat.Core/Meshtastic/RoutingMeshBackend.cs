using LoRaChat.Core.Models;

namespace LoRaChat.Core.Meshtastic;

/// <summary>
/// Presents serial and BLE backends as a single <see cref="IMeshBackend"/> to the UI bridge, routing
/// <see cref="ConnectAsync"/> by the target's transport kind and forwarding whichever backend's events.
/// This keeps the bridge transport-agnostic: it just calls Connect with a serial or BLE target and the
/// right backend handles it.
/// </summary>
public sealed class RoutingMeshBackend : IMeshBackend
{
    private readonly IMeshBackend _serial;
    private readonly IMeshBackend _ble;
    private IMeshBackend? _active;

    public event EventHandler<MeshReadyEvent>? Ready;
    public event EventHandler<IReadOnlyList<NodeSnapshot>>? NodeDb;
    public event EventHandler<MeshPacketEvent>? Packet;
    public event EventHandler<MeshAck>? Ack;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<string>? Log;

    public RoutingMeshBackend(IMeshBackend serial, IMeshBackend ble)
    {
        _serial = serial;
        _ble = ble;
        foreach (var b in new[] { _serial, _ble }) Forward(b);
    }

    private void Forward(IMeshBackend b)
    {
        // Only surface events from the currently active backend so a stale one can't leak state.
        b.Ready += (s, e) => { if (ReferenceEquals(s, _active)) Ready?.Invoke(this, e); };
        b.NodeDb += (s, e) => { if (ReferenceEquals(s, _active)) NodeDb?.Invoke(this, e); };
        b.Packet += (s, e) => { if (ReferenceEquals(s, _active)) Packet?.Invoke(this, e); };
        b.Ack += (s, e) => { if (ReferenceEquals(s, _active)) Ack?.Invoke(this, e); };
        b.ConnectionChanged += (s, e) => { if (ReferenceEquals(s, _active)) ConnectionChanged?.Invoke(this, e); };
        b.Log += (s, e) => { if (ReferenceEquals(s, _active)) Log?.Invoke(this, e); };
    }

    public bool IsConnected => _active?.IsConnected ?? false;

    public async Task ConnectAsync(ConnectTarget target, CancellationToken ct = default)
    {
        var chosen = target.Kind == "ble" ? _ble : _serial;
        // Tear down the other transport if it was connected.
        if (_active != null && !ReferenceEquals(_active, chosen) && _active.IsConnected)
            await _active.DisconnectAsync();
        _active = chosen;
        await chosen.ConnectAsync(target, ct);
    }

    public Task DisconnectAsync() => _active?.DisconnectAsync() ?? Task.CompletedTask;
    public Task SendTextAsync(string text, string? destNodeId, int channel) => (_active ?? _serial).SendTextAsync(text, destNodeId, channel);
    public Task SendCommandAsync(NodeCommand command) => (_active ?? _serial).SendCommandAsync(command);
}
