namespace LoRaChat.Core.Models;

/// <summary>A node in the local device's NodeDB (ported from Form1.NodeInfo).</summary>
public sealed class NodeInfo
{
    public uint Id { get; set; }
    public string? NodeIdStr { get; set; }
    public string? UserLongName { get; set; }
    public string? UserShortName { get; set; }
    public string? Role { get; set; }
    public string? HwModel { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? Altitude { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime LastHeard { get; set; }
    public bool IsStale { get; set; }
    public bool HasPosition { get; set; }
    public int? HopsAway { get; set; }
    public float? Snr { get; set; }
    public int? Battery { get; set; }
    public double? Voltage { get; set; }
    public bool IsFavorite { get; set; }
    /// <summary>Tracked app-side (the device NodeDB doesn't surface it), persisted via
    /// <see cref="AppSettings.IgnoredNodeIds"/> and re-applied on load.</summary>
    public bool IsIgnored { get; set; }
}

/// <summary>A node learned over MQTT (global map), ported from Form1.GlobalNodeInfo.</summary>
public sealed class GlobalNodeInfo
{
    public string? NodeId { get; set; }
    public string? UserLongName { get; set; }
    public string? UserShortName { get; set; }
    public string? Role { get; set; }
    public string? HwModel { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? Altitude { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>A user-drawn map rectangle (ported from Form1.ZoneData).</summary>
public sealed class ZoneData
{
    public string Name { get; set; } = "";
    public double Lat1 { get; set; }
    public double Lng1 { get; set; }
    public double Lat2 { get; set; }
    public double Lng2 { get; set; }
}

/// <summary>
/// One node entry as delivered by the device event source, mirroring the JSON produced by the old
/// <c>meshtastic_bridge.py</c> <c>node_to_json</c>. The native protocol layer (Phase 2) fills these
/// from decoded protobufs; <c>FakeMeshBackend</c> fills them from canned data.
/// </summary>
public sealed class NodeSnapshot
{
    public string? NodeId { get; set; }
    public uint? Num { get; set; }
    public string? LongName { get; set; }
    public string? ShortName { get; set; }
    public string? HwModel { get; set; }
    public string? Role { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public int? Alt { get; set; }
    public float? Snr { get; set; }
    public int? HopsAway { get; set; }
    public long? LastHeard { get; set; }
    public int? Battery { get; set; }
    public double? Voltage { get; set; }
    public bool IsFavorite { get; set; }
}
