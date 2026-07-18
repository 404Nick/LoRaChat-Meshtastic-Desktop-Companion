using System.Globalization;
using System.Text.Json;
using LoRaChat.Core.Models;

namespace LoRaChat.Core.Services;

/// <summary>
/// Owns the local NodeDB (ported from the node-handling parts of Form1: ApplyNodeSnapshot,
/// TouchNodeLastHeard, CheckStaleNodes, WebViewSetNodes, node persistence). Produces the anonymous-object
/// shapes the HTML UI expects for <c>setNodes</c>/<c>setMapNodes</c>, keeping the JSON contract identical.
/// </summary>
public sealed class NodeDbService
{
    private readonly Dictionary<uint, NodeInfo> _nodes = new();
    private readonly SettingsService _settings;
    private readonly string _nodesFilePath;

    public bool ShowIgnored { get; set; }
    public string MyNodeId { get; set; } = "";

    public NodeDbService(SettingsService settings, string appDataDir)
    {
        _settings = settings;
        _nodesFilePath = Path.Combine(appDataDir, "nodes.json");
        Load();
    }

    public int ActiveNodeCount => _nodes.Values.Count(n => !n.IsStale);
    public IReadOnlyCollection<NodeInfo> Nodes => _nodes.Values;

    private static uint KeyFor(string? nodeId, uint? num)
    {
        if (num.HasValue) return num.Value;
        if (!string.IsNullOrEmpty(nodeId) && nodeId.StartsWith('!') &&
            uint.TryParse(nodeId.AsSpan(1), NumberStyles.HexNumber, null, out uint parsed))
            return parsed;
        return (uint)(nodeId?.GetHashCode() ?? 0);
    }

    /// <summary>Applies one node from a device snapshot, merging into any existing entry (ported from
    /// Form1.ApplyNodeSnapshot). Only overwrites fields that are actually present.</summary>
    public void ApplySnapshot(NodeSnapshot s)
    {
        if (string.IsNullOrEmpty(s.NodeId) && !s.Num.HasValue) return;
        uint key = KeyFor(s.NodeId, s.Num);

        if (!_nodes.TryGetValue(key, out var node))
        {
            node = new NodeInfo { Id = key, NodeIdStr = s.NodeId };
            _nodes[key] = node;
        }
        if (!string.IsNullOrEmpty(s.NodeId)) node.NodeIdStr = s.NodeId;

        if (s.LongName != null) node.UserLongName = s.LongName;
        if (s.ShortName != null) node.UserShortName = s.ShortName;
        if (s.HwModel != null) node.HwModel = s.HwModel;
        if (s.Role != null) node.Role = s.Role;

        if (s.Lat.HasValue && s.Lon.HasValue)
        {
            node.Latitude = s.Lat;
            node.Longitude = s.Lon;
            node.HasPosition = true;
            if (s.Alt.HasValue) node.Altitude = s.Alt;
        }

        if (s.Snr.HasValue) node.Snr = s.Snr;
        if (s.HopsAway.HasValue) node.HopsAway = s.HopsAway;
        if (s.Battery.HasValue) node.Battery = s.Battery;
        if (s.Voltage.HasValue) node.Voltage = s.Voltage;
        node.IsFavorite = s.IsFavorite;

        if (!string.IsNullOrEmpty(node.NodeIdStr))
            node.IsIgnored = _settings.Current.IgnoredNodeIds.Contains(node.NodeIdStr);

        if (s.LastHeard is > 0)
        {
            DateTime dt = DateTimeOffset.FromUnixTimeSeconds(s.LastHeard.Value).LocalDateTime;
            node.LastHeard = dt;
            node.LastSeen = dt;
            node.IsStale = (DateTime.Now - dt).TotalMinutes > _settings.Current.NodeTimeout;
        }
    }

    public void ApplyAll(IReadOnlyList<NodeSnapshot> snapshots)
    {
        foreach (var s in snapshots) ApplySnapshot(s);
        Save();
    }

    /// <summary>Marks a node heard-from right now (ported from Form1.TouchNodeLastHeard).</summary>
    public void TouchLastHeard(string nodeId)
    {
        uint key = KeyFor(nodeId, null);
        if (!_nodes.TryGetValue(key, out var node))
        {
            node = new NodeInfo { Id = key, NodeIdStr = nodeId };
            _nodes[key] = node;
        }
        node.LastHeard = DateTime.Now;
        node.LastSeen = DateTime.Now;
        node.IsStale = false;
    }

    public void CheckStale()
    {
        foreach (var n in _nodes.Values)
            n.IsStale = (DateTime.Now - n.LastHeard).TotalMinutes > _settings.Current.NodeTimeout;
    }

    public bool SetFavorite(string nodeId, bool favorite)
    {
        var n = _nodes.Values.FirstOrDefault(x => x.NodeIdStr == nodeId);
        if (n == null) return false;
        n.IsFavorite = favorite;
        return true;
    }

    public void SetIgnored(string nodeId, bool ignored)
    {
        var list = _settings.Current.IgnoredNodeIds;
        if (ignored && !list.Contains(nodeId)) list.Add(nodeId);
        else if (!ignored) list.Remove(nodeId);
        var n = _nodes.Values.FirstOrDefault(x => x.NodeIdStr == nodeId);
        if (n != null) n.IsIgnored = ignored;
        _settings.Save();
    }

    public void Remove(string nodeId)
    {
        var key = _nodes.FirstOrDefault(kv => kv.Value.NodeIdStr == nodeId).Key;
        _nodes.Remove(key);
        Save();
    }

    public void Reset()
    {
        _nodes.Clear();
        Save();
    }

    public string? LookupDisplayName(string nodeId)
        => _nodes.Values.FirstOrDefault(n => n.NodeIdStr == nodeId && !string.IsNullOrEmpty(n.UserLongName))?.UserLongName;

    /// <summary>The ordered node list in the exact shape the UI's <c>setNodes</c> expects
    /// (ported from Form1.WebViewSetNodes).</summary>
    public object BuildNodesPayload() =>
        _nodes.Values
            .Where(n => ShowIgnored || !n.IsIgnored)
            .OrderByDescending(n => n.IsFavorite)
            .ThenByDescending(n => n.LastHeard)
            .Select(n => new
            {
                nodeId = n.NodeIdStr,
                longName = n.UserLongName,
                shortName = n.UserShortName,
                role = n.Role,
                hwModel = n.HwModel,
                lat = n.HasPosition ? n.Latitude : null,
                lon = n.HasPosition ? n.Longitude : null,
                alt = n.Altitude,
                hops = n.HopsAway,
                snr = n.Snr,
                battery = n.Battery,
                voltage = n.Voltage,
                isFavorite = n.IsFavorite,
                isIgnored = n.IsIgnored,
                lastHeard = n.LastHeard,
                isStale = n.IsStale,
                isMine = IsMine(n.NodeIdStr),
            })
            .ToList();

    /// <summary>Map-node payload (positioned nodes only), matching Form1.WebViewSetMapNodes.</summary>
    public object BuildMapNodesPayload() =>
        _nodes.Values.Where(n => n.HasPosition).Select(n => new
        {
            nodeId = n.NodeIdStr,
            longName = n.UserLongName,
            role = n.Role,
            hwModel = n.HwModel,
            lat = n.Latitude,
            lon = n.Longitude,
            alt = n.Altitude,
            isMine = IsMine(n.NodeIdStr),
            isStale = n.IsStale,
        }).ToList();

    public string? MyIdentityLongName =>
        _nodes.Values.FirstOrDefault(n => IsMine(n.NodeIdStr))?.UserLongName;
    public string? MyIdentityShortName =>
        _nodes.Values.FirstOrDefault(n => IsMine(n.NodeIdStr))?.UserShortName;

    private bool IsMine(string? nodeId) =>
        !string.IsNullOrEmpty(MyNodeId) && string.Equals(nodeId, MyNodeId, StringComparison.OrdinalIgnoreCase);

    private void Save()
    {
        try { File.WriteAllText(_nodesFilePath, JsonSerializer.Serialize(_nodes.Values.ToList())); }
        catch { /* best-effort persistence */ }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_nodesFilePath)) return;
            var list = JsonSerializer.Deserialize<List<NodeInfo>>(File.ReadAllText(_nodesFilePath));
            if (list == null) return;
            foreach (var n in list)
            {
                if (!string.IsNullOrEmpty(n.NodeIdStr))
                    n.IsIgnored = _settings.Current.IgnoredNodeIds.Contains(n.NodeIdStr);
                _nodes[n.Id] = n;
            }
        }
        catch { /* corrupt cache is non-fatal */ }
    }
}
