using LoRaChat.Core.Models;

namespace LoRaChat.Core.Meshtastic;

/// <summary>
/// A hardware-free <see cref="IMeshBackend"/> used to verify the UI bridge end-to-end without a radio
/// (Phase 1) and as a demo/offline mode. On connect it replays a canned "ready" + NodeDB snapshot and,
/// while connected, periodically emits a received text packet so the chat and node list populate. The
/// native serial/BLE backend (Phase 2/3) is a drop-in replacement raising the same events.
/// </summary>
public sealed class FakeMeshBackend : IMeshBackend
{
    private const string MyId = "!4b7a1c9e";
    private CancellationTokenSource? _cts;
    private int _tick;

    public event EventHandler<MeshReadyEvent>? Ready;
    public event EventHandler<IReadOnlyList<NodeSnapshot>>? NodeDb;
    public event EventHandler<MeshPacketEvent>? Packet;
    public event EventHandler<MeshAck>? Ack;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<string>? Log;

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(ConnectTarget target, CancellationToken ct = default)
    {
        if (IsConnected) return Task.CompletedTask;
        IsConnected = true;
        ConnectionChanged?.Invoke(this, true);
        Log?.Invoke(this, $"Demo backend connected ({target.Kind}:{target.Id}).");
        Ready?.Invoke(this, new MeshReadyEvent(MyId, 0x4b7a1c9e));
        NodeDb?.Invoke(this, BuildNodes());

        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (IsConnected)
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(this, false);
            Log?.Invoke(this, "Demo backend disconnected.");
        }
        return Task.CompletedTask;
    }

    public Task SendTextAsync(string text, string? destNodeId, int channel)
    {
        Log?.Invoke(this, $"TX [{(destNodeId ?? "^all")}/ch{channel}]: {text}");
        return Task.CompletedTask;
    }

    public Task SendCommandAsync(NodeCommand command)
    {
        // Echo an ack and, for NodeDB-mutating commands, re-emit the snapshot, mirroring the bridge.
        Ack?.Invoke(this, new MeshAck(command.Cmd, command.NodeId, true, null));
        if (command.Cmd is "favorite" or "unfavorite" or "ignore" or "unignore" or "remove" or "resetNodeDb")
            NodeDb?.Invoke(this, BuildNodes());
        return Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        string[] samples =
        {
            "Recon-2 online, holding position.",
            "Weather clear, visibility good.",
            "Relay-North signal nominal.",
        };
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(8), ct);
                string from = _tick % 2 == 0 ? "!f7e8d9c0" : "!3b2a19f8";
                Packet?.Invoke(this, new MeshPacketEvent(
                    from, "^all", 0, RxSnr: 6.5 - _tick % 4, RxRssi: -92, "TEXT_MESSAGE_APP",
                    samples[_tick % samples.Length]));
                _tick++;
            }
        }
        catch (OperationCanceledException) { }
    }

    private static IReadOnlyList<NodeSnapshot> BuildNodes()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new List<NodeSnapshot>
        {
            new() { NodeId = MyId, Num = 0x4b7a1c9e, LongName = "Vy-01", ShortName = "Vy", HwModel = "TBEAM",
                    Role = "CLIENT", Lat = 55.751, Lon = 37.618, Alt = 145, Snr = 0, HopsAway = 0,
                    LastHeard = now, Battery = 92, Voltage = 4.05, IsFavorite = true },
            new() { NodeId = "!f7e8d9c0", Num = 0xf7e8d9c0, LongName = "Recon-2", ShortName = "R2", HwModel = "HELTEC_V3",
                    Role = "CLIENT", Lat = 55.760, Lon = 37.640, Alt = 160, Snr = 6.25f, HopsAway = 1,
                    LastHeard = now, Battery = 78, Voltage = 3.92 },
            new() { NodeId = "!a1b2c3d4", Num = 0xa1b2c3d4, LongName = "Base-Alpha", ShortName = "BA", HwModel = "RAK4631",
                    Role = "ROUTER", Lat = 55.742, Lon = 37.600, Alt = 130, Snr = 4.0f, HopsAway = 2,
                    LastHeard = now - 300, Battery = 100, Voltage = 4.15 },
            new() { NodeId = "!3b2a19f8", Num = 0x3b2a19f8, LongName = "Relay-North", ShortName = "RN", HwModel = "TBEAM",
                    Role = "ROUTER_CLIENT", Lat = 55.780, Lon = 37.610, Alt = 175, Snr = 2.5f, HopsAway = 2,
                    LastHeard = now - 60, Battery = 64, Voltage = 3.80 },
        };
    }
}
