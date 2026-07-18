using LoRaChat.Core.Models;

namespace LoRaChat.Core.Meshtastic;

/// <summary>How to reach a radio: a serial port name or a BLE address.</summary>
public sealed record ConnectTarget(string Kind, string Id)
{
    public static ConnectTarget Serial(string port) => new("serial", port);
    public static ConnectTarget Ble(string address) => new("ble", address);
}

/// <summary>Emitted on connect with the radio's own node id (bridge "ready" event).</summary>
public sealed record MeshReadyEvent(string? MyNodeId, uint? MyNodeNum);

/// <summary>A received packet (bridge "packet" event).</summary>
public sealed record MeshPacketEvent(
    string? FromId,
    string? ToId,
    int Channel,
    double? RxSnr,
    double? RxRssi,
    string? PortNum,
    string? Text);

/// <summary>Result of a node-management command (bridge "ack" event).</summary>
public sealed record MeshAck(string Cmd, string? NodeId, bool Ok, string? Error);

/// <summary>A node-management command issued to the radio (bridge stdin commands).</summary>
public sealed record NodeCommand(string Cmd, string? NodeId = null)
{
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public int? Alt { get; init; }
    public int? Battery { get; init; }
    public double? Voltage { get; init; }
    public int? Seconds { get; init; }
}

/// <summary>
/// The device link, abstracted as the exact event contract the original <c>meshtastic_bridge.py</c>
/// exposed (ready / nodedb / packet / config / ack). This is the seam that makes the UI bridge
/// transport-independent: <c>FakeMeshBackend</c> replays canned events for hardware-free verification
/// (Phase 1), and the native serial/BLE implementation (Phase 2/3) raises the same events from decoded
/// protobufs. Nothing downstream of this interface knows how bytes reach the radio.
/// </summary>
public interface IMeshBackend
{
    event EventHandler<MeshReadyEvent>? Ready;
    event EventHandler<IReadOnlyList<NodeSnapshot>>? NodeDb;
    event EventHandler<MeshPacketEvent>? Packet;
    event EventHandler<MeshAck>? Ack;
    event EventHandler<bool>? ConnectionChanged;
    /// <summary>Diagnostic/log line for the airtime log.</summary>
    event EventHandler<string>? Log;

    bool IsConnected { get; }

    Task ConnectAsync(ConnectTarget target, CancellationToken ct = default);
    Task DisconnectAsync();

    /// <summary>Sends a text message. <paramref name="destNodeId"/> null = broadcast.</summary>
    Task SendTextAsync(string text, string? destNodeId, int channel);

    /// <summary>Issues a node-management command (favorite/ignore/remove/requestPosition/…).</summary>
    Task SendCommandAsync(NodeCommand command);
}
