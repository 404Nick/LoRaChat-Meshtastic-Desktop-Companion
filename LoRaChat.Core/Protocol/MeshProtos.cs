using ProtoBuf;

namespace LoRaChat.Core.Protocol;

// Hand-written protobuf-net contracts for the subset of the Meshtastic schema the app needs. Field
// numbers/types are taken verbatim from the official meshtastic/protobufs (mesh.proto, portnums.proto,
// telemetry.proto, admin.proto, channel.proto). protobuf-net silently skips fields we don't declare,
// so we only model what we read/write. Kept in LoRaChat.Core so every platform head shares it.

public enum PortNum
{
    UNKNOWN_APP = 0,
    TEXT_MESSAGE_APP = 1,
    POSITION_APP = 3,
    NODEINFO_APP = 4,
    ROUTING_APP = 5,
    ADMIN_APP = 6,
    TELEMETRY_APP = 67,
}

[ProtoContract]
public sealed class ToRadio
{
    [ProtoMember(1)] public MeshPacket? Packet { get; set; }
    [ProtoMember(3)] public uint? WantConfigId { get; set; }
    [ProtoMember(4)] public bool? Disconnect { get; set; }
    [ProtoMember(7)] public Heartbeat? Heartbeat { get; set; }
}

[ProtoContract]
public sealed class Heartbeat { }

[ProtoContract]
public sealed class FromRadio
{
    [ProtoMember(1)] public uint Id { get; set; }
    [ProtoMember(2)] public MeshPacket? Packet { get; set; }
    [ProtoMember(3)] public MyNodeInfo? MyInfo { get; set; }
    [ProtoMember(4)] public NodeInfo? NodeInfo { get; set; }
    // config(5), moduleConfig(9), metadata(13) etc. are intentionally not modeled — protobuf-net skips them.
    [ProtoMember(7)] public uint? ConfigCompleteId { get; set; }
    [ProtoMember(10)] public Channel? Channel { get; set; }
}

[ProtoContract]
public sealed class MyNodeInfo
{
    [ProtoMember(1)] public uint MyNodeNum { get; set; }
}

[ProtoContract]
public sealed class MeshPacket
{
    [ProtoMember(1, DataFormat = DataFormat.FixedSize)] public uint From { get; set; }
    [ProtoMember(2, DataFormat = DataFormat.FixedSize)] public uint To { get; set; }
    [ProtoMember(3)] public uint Channel { get; set; }
    [ProtoMember(4)] public Data? Decoded { get; set; }
    [ProtoMember(5)] public byte[]? Encrypted { get; set; }
    [ProtoMember(6, DataFormat = DataFormat.FixedSize)] public uint Id { get; set; }
    [ProtoMember(7, DataFormat = DataFormat.FixedSize)] public uint RxTime { get; set; }
    [ProtoMember(8)] public float RxSnr { get; set; }
    [ProtoMember(9)] public uint HopLimit { get; set; }
    [ProtoMember(10)] public bool WantAck { get; set; }
    [ProtoMember(11)] public int Priority { get; set; }
    [ProtoMember(12)] public int RxRssi { get; set; }
    [ProtoMember(15)] public uint HopStart { get; set; }
}

[ProtoContract]
public sealed class Data
{
    [ProtoMember(1)] public PortNum Portnum { get; set; }
    [ProtoMember(2)] public byte[]? Payload { get; set; }
    [ProtoMember(3)] public bool WantResponse { get; set; }
    [ProtoMember(4, DataFormat = DataFormat.FixedSize)] public uint Dest { get; set; }
    [ProtoMember(5, DataFormat = DataFormat.FixedSize)] public uint Source { get; set; }
    [ProtoMember(6, DataFormat = DataFormat.FixedSize)] public uint RequestId { get; set; }
    [ProtoMember(7, DataFormat = DataFormat.FixedSize)] public uint ReplyId { get; set; }
}

[ProtoContract]
public sealed class User
{
    [ProtoMember(1)] public string? Id { get; set; }
    [ProtoMember(2)] public string? LongName { get; set; }
    [ProtoMember(3)] public string? ShortName { get; set; }
    [ProtoMember(5)] public int HwModel { get; set; }
    [ProtoMember(7)] public int Role { get; set; }
}

[ProtoContract]
public sealed class Position
{
    [ProtoMember(1, DataFormat = DataFormat.FixedSize)] public int LatitudeI { get; set; }
    [ProtoMember(2, DataFormat = DataFormat.FixedSize)] public int LongitudeI { get; set; }
    [ProtoMember(3)] public int Altitude { get; set; }
    [ProtoMember(4, DataFormat = DataFormat.FixedSize)] public uint Time { get; set; }
}

[ProtoContract]
public sealed class NodeInfo
{
    [ProtoMember(1)] public uint Num { get; set; }
    [ProtoMember(2)] public User? User { get; set; }
    [ProtoMember(3)] public Position? Position { get; set; }
    [ProtoMember(4)] public float Snr { get; set; }
    [ProtoMember(5, DataFormat = DataFormat.FixedSize)] public uint LastHeard { get; set; }
    [ProtoMember(6)] public DeviceMetrics? DeviceMetrics { get; set; }
    [ProtoMember(7)] public uint Channel { get; set; }
    [ProtoMember(9)] public uint HopsAway { get; set; }
    [ProtoMember(10)] public bool IsFavorite { get; set; }
    [ProtoMember(11)] public bool IsIgnored { get; set; }
}

[ProtoContract]
public sealed class DeviceMetrics
{
    [ProtoMember(1)] public uint? BatteryLevel { get; set; }
    [ProtoMember(2)] public float? Voltage { get; set; }
    [ProtoMember(3)] public float? ChannelUtilization { get; set; }
    [ProtoMember(4)] public float? AirUtilTx { get; set; }
    [ProtoMember(5)] public uint? UptimeSeconds { get; set; }
}

[ProtoContract]
public sealed class Telemetry
{
    [ProtoMember(1, DataFormat = DataFormat.FixedSize)] public uint Time { get; set; }
    [ProtoMember(2)] public DeviceMetrics? DeviceMetrics { get; set; }
}

[ProtoContract]
public sealed class ChannelSettings
{
    [ProtoMember(2)] public byte[]? Psk { get; set; }
    [ProtoMember(3)] public string? Name { get; set; }
    [ProtoMember(5)] public bool UplinkEnabled { get; set; }
    [ProtoMember(6)] public bool DownlinkEnabled { get; set; }
}

[ProtoContract]
public sealed class Channel
{
    [ProtoMember(1)] public int Index { get; set; }
    [ProtoMember(2)] public ChannelSettings? Settings { get; set; }
    // Role: 0 = DISABLED, 1 = PRIMARY, 2 = SECONDARY.
    [ProtoMember(3)] public int Role { get; set; }
}

/// <summary>Local-node admin command (admin.proto). Only the fields the app issues are modeled.</summary>
[ProtoContract]
public sealed class AdminMessage
{
    [ProtoMember(32)] public User? SetOwner { get; set; }
    [ProtoMember(38)] public uint? RemoveByNodenum { get; set; }
    [ProtoMember(39)] public uint? SetFavoriteNode { get; set; }
    [ProtoMember(40)] public uint? RemoveFavoriteNode { get; set; }
    [ProtoMember(41)] public Position? SetFixedPosition { get; set; }
    [ProtoMember(42)] public bool? RemoveFixedPosition { get; set; }
    [ProtoMember(47)] public uint? SetIgnoredNode { get; set; }
    [ProtoMember(48)] public uint? RemoveIgnoredNode { get; set; }
    [ProtoMember(97)] public int? RebootSeconds { get; set; }
}
