using System.Text;
using System.Text.Json;
using LoRaChat.Core.Models;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace LoRaChat.Core.Services;

/// <summary>
/// The global-map MQTT feed, ported from Form1's ConnectMqttAsync / OnMqttMessageReceived /
/// DisconnectMqtt / CleanupGlobalNodes. Subscribes to the public Meshtastic JSON topic
/// (<c>msh/+/2/json/+/+</c>), decodes position/nodeinfo messages into a global-node dictionary, and
/// raises events the bridge turns into <c>setGlobalMapNodes</c> / <c>setMqttStatus</c> pushes.
/// MQTTnet is cross-platform, so this works unchanged on all heads.
/// </summary>
public sealed class MqttService : IDisposable
{
    private readonly SettingsService _settings;
    private IMqttClient? _client;
    private bool _connected;
    private bool _intentionalDisconnect;
    private bool _firstMessageSeen;
    private bool _firstParseErrorLogged;
    private readonly System.Threading.Timer _reconnectTimer;
    private readonly System.Threading.Timer _cleanupTimer;

    private readonly Dictionary<string, GlobalNodeInfo> _globalNodes = new();

    public event EventHandler? StatusChanged;
    public event EventHandler? GlobalNodesChanged;
    public event EventHandler<string>? Log;

    public bool IsConnected => _connected;
    public int GlobalNodeCount => _globalNodes.Count;

    public MqttService(SettingsService settings)
    {
        _settings = settings;
        _reconnectTimer = new System.Threading.Timer(_ => _ = TryReconnectAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _cleanupTimer = new System.Threading.Timer(_ => CleanupGlobalNodes(), null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>Global-map node payload (positioned nodes), matching Form1.WebViewSetGlobalMapNodes.</summary>
    public object BuildGlobalMapNodesPayload(string myNodeId) =>
        _globalNodes.Values.Where(n => n.Latitude.HasValue && n.Longitude.HasValue).Select(n => new
        {
            nodeId = n.NodeId,
            longName = n.UserLongName,
            role = n.Role,
            hwModel = n.HwModel,
            lat = n.Latitude,
            lon = n.Longitude,
            alt = n.Altitude,
            isMine = !string.IsNullOrEmpty(myNodeId) && string.Equals(n.NodeId, myNodeId, StringComparison.OrdinalIgnoreCase),
            isStale = false,
        }).ToList();

    public async Task ConnectAsync()
    {
        if (_connected) return;
        _intentionalDisconnect = false;

        var s = _settings.Current;
        if (string.IsNullOrEmpty(s.MqttUsername) || string.IsNullOrEmpty(s.MqttPassword))
        {
            Log?.Invoke(this, "MQTT: set username/password in Settings first. For the public broker use meshdev / large4cats.");
            return;
        }

        try
        {
            try { _client?.Dispose(); } catch { } // avoid orphaning a prior client on reconnect
            var factory = new MqttFactory();
            _client = factory.CreateMqttClient();

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(s.MqttBroker, 1883)
                .WithClientId($"lorachat_{Guid.NewGuid():N}")
                .WithCleanSession()
                .WithCredentials(s.MqttUsername, s.MqttPassword)
                .Build();

            _client.ConnectedAsync += async _ =>
            {
                _connected = true;
                _firstMessageSeen = false;
                _firstParseErrorLogged = false;
                StatusChanged?.Invoke(this, EventArgs.Empty);
                await _client.SubscribeAsync(new MqttTopicFilterBuilder()
                    .WithTopic("msh/+/2/json/+/+")
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                    .Build());
            };

            _client.DisconnectedAsync += _ =>
            {
                _connected = false;
                StatusChanged?.Invoke(this, EventArgs.Empty);
                if (!_intentionalDisconnect) _reconnectTimer.Change(5000, Timeout.Infinite);
                return Task.CompletedTask;
            };

            _client.ApplicationMessageReceivedAsync += e =>
            {
                try
                {
                    var seg = e.ApplicationMessage.PayloadSegment;
                    byte[] payload = seg.Count > 0 ? seg.ToArray() : Array.Empty<byte>();
                    OnMessage(e.ApplicationMessage.Topic, payload);
                }
                catch (Exception ex) { Log?.Invoke(this, $"MQTT receive handler error: {ex.Message}"); }
                return Task.CompletedTask;
            };

            await _client.ConnectAsync(options);
            _reconnectTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"MQTT connection error: {ex.Message}");
            if (ex.Message.Contains("BadUserNameOrPassword", StringComparison.OrdinalIgnoreCase))
                Log?.Invoke(this, "SYSTEM Invalid MQTT credentials. For public mqtt.meshtastic.org use meshdev / large4cats.");
        }
    }

    public void Disconnect()
    {
        _intentionalDisconnect = true;
        _reconnectTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _ = _client?.DisconnectAsync();
    }

    private async Task TryReconnectAsync()
    {
        if (_connected || _intentionalDisconnect) return;
        try { await ConnectAsync(); } catch { /* the timer will retry */ }
    }

    private void OnMessage(string topic, byte[] payload)
    {
        if (payload.Length == 0) return;
        if (!_firstMessageSeen) { _firstMessageSeen = true; Log?.Invoke(this, $"MQTT: first data received ({topic})"); }

        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(payload));
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeElem)) return;
            string? type = typeElem.GetString();
            if (type is not ("position" or "nodeinfo")) return;

            string? nodeId = null;
            if (root.TryGetProperty("sender", out var senderElem) && senderElem.ValueKind == JsonValueKind.String)
                nodeId = senderElem.GetString();
            else if (root.TryGetProperty("from", out var fromElem))
            {
                if (fromElem.ValueKind == JsonValueKind.Number && fromElem.TryGetUInt32(out uint fromNum)) nodeId = "!" + fromNum.ToString("x8");
                else if (fromElem.ValueKind == JsonValueKind.String) nodeId = fromElem.GetString();
            }
            if (string.IsNullOrEmpty(nodeId)) return;

            double? lat = null, lon = null;
            int? alt = null;
            if (type == "position" && root.TryGetProperty("payload", out var posP) && posP.ValueKind == JsonValueKind.Object)
            {
                if (posP.TryGetProperty("latitude_i", out var latI) && posP.TryGetProperty("longitude_i", out var lonI) &&
                    latI.ValueKind == JsonValueKind.Number && lonI.ValueKind == JsonValueKind.Number)
                {
                    lat = latI.GetInt64() * 1e-7;
                    lon = lonI.GetInt64() * 1e-7;
                }
                else if (posP.TryGetProperty("latitude", out var latE) && posP.TryGetProperty("longitude", out var lonE))
                {
                    lat = latE.GetDouble();
                    lon = lonE.GetDouble();
                }
                if (posP.TryGetProperty("altitude", out var altE) && altE.ValueKind == JsonValueKind.Number && altE.TryGetInt32(out int altVal))
                    alt = altVal;
            }

            string longName = "?", shortName = "?", roleStr = "?", hwModel = "?";
            bool hasPayload = root.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object;
            if (hasPayload && p.TryGetProperty("user", out var user))
            {
                if (user.TryGetProperty("longName", out var ln)) longName = ln.ValueKind == JsonValueKind.String ? ln.GetString()! : ln.ToString();
                if (user.TryGetProperty("shortName", out var sn)) shortName = sn.ValueKind == JsonValueKind.String ? sn.GetString()! : sn.ToString();
            }
            if (hasPayload && p.TryGetProperty("role", out var roleElem))
            {
                if (roleElem.ValueKind == JsonValueKind.String) roleStr = roleElem.GetString()!;
                else if (roleElem.ValueKind == JsonValueKind.Number && roleElem.TryGetInt32(out int roleNum)) roleStr = ((Role)roleNum).ToString();
            }
            if (hasPayload && p.TryGetProperty("hwModel", out var hwElem))
                hwModel = hwElem.ValueKind == JsonValueKind.String ? hwElem.GetString()! : hwElem.ToString();

            if (!_globalNodes.TryGetValue(nodeId, out var existing))
            {
                _globalNodes[nodeId] = new GlobalNodeInfo
                {
                    NodeId = nodeId, UserLongName = longName, UserShortName = shortName, Role = roleStr,
                    HwModel = hwModel, Latitude = lat, Longitude = lon, Altitude = alt, LastUpdated = DateTime.Now
                };
            }
            else
            {
                existing.UserLongName = longName;
                existing.UserShortName = shortName;
                existing.Role = roleStr;
                existing.HwModel = hwModel;
                if (lat.HasValue && lon.HasValue) { existing.Latitude = lat; existing.Longitude = lon; existing.Altitude = alt; }
                existing.LastUpdated = DateTime.Now;
            }
            GlobalNodesChanged?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            if (!_firstParseErrorLogged)
            {
                _firstParseErrorLogged = true;
                Log?.Invoke(this, $"MQTT: could not parse message ({ex.GetType().Name}: {ex.Message})");
            }
        }
    }

    private void CleanupGlobalNodes()
    {
        var now = DateTime.Now;
        var toRemove = _globalNodes.Where(kv => (now - kv.Value.LastUpdated).TotalMinutes > 30).Select(kv => kv.Key).ToList();
        foreach (var key in toRemove) _globalNodes.Remove(key);
        if (toRemove.Count > 0) { GlobalNodesChanged?.Invoke(this, EventArgs.Empty); StatusChanged?.Invoke(this, EventArgs.Empty); }
    }

    public void Dispose()
    {
        _intentionalDisconnect = true;
        try { _reconnectTimer.Dispose(); } catch { }
        try { _cleanupTimer.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
    }
}
