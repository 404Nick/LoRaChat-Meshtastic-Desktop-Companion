using System.Globalization;
using System.Text.Json;
using LoRaChat.Core.Abstractions;
using LoRaChat.Core.Meshtastic;
using LoRaChat.Core.Models;
using LoRaChat.Core.Services;

namespace LoRaChat.Core.Bridge;

/// <summary>
/// The heart of the app: reproduces the JS↔host contract the original Form1 implemented. It receives
/// the ~55 <c>window.chrome.webview.postMessage</c> message types from the HTML UI and dispatches them
/// to the Core services / mesh backend, and pushes state back with <c>window.LoRaChatUI.*</c> calls.
/// It is deliberately transport-agnostic — it talks to <see cref="IMeshBackend"/>, so the same bridge
/// drives the fake backend (Phase 1) and the native serial/BLE backend (Phase 2/3).
/// </summary>
public sealed class UiBridge : IDisposable
{
    private readonly IWebViewHost _host;
    private readonly IMeshBackend _backend;
    private readonly SettingsService _settings;
    private readonly NodeDbService _nodes;
    private readonly IDialogService _dialogs;
    private readonly ISerialProvider _serial;
    private readonly IBleProvider _ble;
    private readonly IUiDispatcher _ui;
    private readonly TelemetrySubstitutionModule _telemetrySim;
    private readonly MqttService _mqtt;
    private readonly TileService _tiles;
    private readonly WebViewPush _push;
    private bool _simBroadcast;
    private readonly string _telemetryConfigPath;

    private readonly List<(string Time, string Text)> _eventLog = new();
    private const int MaxEventLogEntries = 200;
    private readonly List<double?> _snrHistory = new(new double?[16]);
    private readonly List<ZoneData> _zones = new();
    private readonly string _zonesPath;

    private readonly Dictionary<string, List<(string Text, bool IsMine)>> _privateChats = new();
    private readonly List<string> _privateContactIds = new();
    private string? _selectedPrivateContactId;
    private const int PrivateChannelIndex = 1;

    private List<string> _availablePorts = new();
    private string? _selectedPort;
    private string? _selectedBleAddress;
    private string _connMode = "serial";
    private bool _showIgnored;
    private int _rxCount, _txCount, _unreadChat;
    private readonly DateTime _startTime = DateTime.Now;
    private System.Threading.Timer? _tickTimer;

    public UiBridge(
        IWebViewHost host, IMeshBackend backend, SettingsService settings, NodeDbService nodes,
        IDialogService dialogs, ISerialProvider serial, IBleProvider ble, IUiDispatcher ui,
        MqttService mqtt, TileService tiles, string appDataDir)
    {
        _host = host;
        _backend = backend;
        _settings = settings;
        _nodes = nodes;
        _dialogs = dialogs;
        _serial = serial;
        _ble = ble;
        _ui = ui;
        _mqtt = mqtt;
        _tiles = tiles;
        _push = new WebViewPush(host);
        _zonesPath = Path.Combine(appDataDir, "zones.json");
        _telemetryConfigPath = Path.Combine(appDataDir, "telemetry_mock.json");

        _nodes.MyNodeId = _settings.Current.MyNodeId;
        LoadZones();

        // Telemetry simulation module: hot-reloads telemetry_mock.json, surfaces its log in the
        // airtime log, and refreshes the sim status indicator on change.
        _telemetrySim = new TelemetrySubstitutionModule(_telemetryConfigPath)
        {
            Log = m => _ui.Post(() => AppendLog(m)),
            OnConfigChanged = () => _ui.Post(SetTelemetrySim),
        };
        _telemetrySim.Start();

        _host.MessageReceived += OnMessage;
        _backend.Ready += (_, e) => _ui.Post(() => OnReady(e));
        _backend.NodeDb += (_, e) => _ui.Post(() => OnNodeDb(e));
        _backend.Packet += (_, e) => _ui.Post(() => OnPacket(e));
        _backend.Ack += (_, e) => _ui.Post(() => OnAck(e));
        _backend.ConnectionChanged += (_, c) => _ui.Post(() => SetConnectionStatus(c));
        _backend.Log += (_, m) => _ui.Post(() => AppendLog(m));

        _mqtt.StatusChanged += (_, _) => _ui.Post(SetMqttStatus);
        _mqtt.GlobalNodesChanged += (_, _) => _ui.Post(SetGlobalMapNodes);
        _mqtt.Log += (_, m) => _ui.Post(() => AppendLog(m));
    }

    // ---------------- Inbound: JS -> host ----------------

    private void OnMessage(object? sender, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? type = Str(root, "type");
            switch (type)
            {
                case "ready": PushInitialState(); break;
                case "sendMessage": HandleSendMessage(Str(root, "text")); break;
                case "toggleListening": _ = ToggleListeningAsync(); break;
                case "refreshPorts": RefreshPorts(); break;
                case "portSelected": SelectPort(Str(root, "port")); break;
                case "scanBle": _ = ScanBleAsync(); break;
                case "bleSelected": SelectBle(Str(root, "address")); break;
                case "switchTab": break; // handled entirely UI-side
                case "refreshNodes": SetNodes(); break;
                case "toggleShowIgnored":
                    _showIgnored = Bool(root, "value") ?? !_showIgnored;
                    _nodes.ShowIgnored = _showIgnored;
                    SetNodes(); SetMapNodes();
                    break;
                case "favoriteNode": HandleNodeCmd(Str(root, "nodeId"), (Bool(root, "value") ?? true) ? "favorite" : "unfavorite"); break;
                case "ignoreNode": HandleNodeCmd(Str(root, "nodeId"), (Bool(root, "value") ?? true) ? "ignore" : "unignore"); break;
                case "removeNode": HandleNodeCmd(Str(root, "nodeId"), "remove"); break;
                case "requestPosition": HandleNodeCmd(Str(root, "nodeId"), "requestPosition"); break;
                case "resetNodeDb": HandleNodeCmd(null, "resetNodeDb"); break;
                case "rebootNode":
                    _ = _backend.SendCommandAsync(new NodeCommand("reboot", Str(root, "nodeId")));
                    AppendSystem("Reboot requested.");
                    break;
                case "consoleCommand": RunConsoleCommand(Str(root, "command")); break;
                case "selectPrivateContact": SelectPrivateContact(Str(root, "nodeId")); break;
                case "openPrivateChat": OpenPrivateChat(Str(root, "nodeId")); break;
                case "sendPrivateMessage": HandleSendPrivate(Str(root, "nodeId"), Str(root, "text")); break;
                case "deletePrivateChat": DeletePrivateChat(Str(root, "nodeId")); break;
                case "setOwner": HandleSetOwner(Str(root, "longName"), Str(root, "shortName")); break;
                case "applySettings": if (root.TryGetProperty("settings", out var se)) ApplySettings(se); break;
                case "syncConfig": AppendSystem("Load-from-device not available on demo backend."); break;
                case "resetSettings": ResetSettings(); break;
                case "generatePrivatePsk": GeneratePrivatePsk(); break;
                case "generateMyNodeId": SetMyNodeId(GenerateNodeId(), announce: true); break;
                case "setMyNodeIdManual": SetMyNodeId(Str(root, "nodeId"), announce: true); break;
                case "setLanguage": _settings.Current.Language = Str(root, "language") ?? "en"; _settings.Save(); break;
                case "setTheme": _settings.Current.Theme = Str(root, "theme") ?? "tactical"; _settings.Save(); break;
                case "mapReady": SetMapNodes(); SetGlobalMapNodes(); SetZones(); SetMqttStatus(); SetMyNodeLabel(); break;
                case "deleteAllZones": _zones.Clear(); SaveZones(); SetZones(); break;
                case "addZone": AddZone(root); break;
                case "deleteZone": DeleteZone(Str(root, "name")); break;
                case "downloadWorldTiles": _ = DownloadTilesAsync(); break;
                case "mqttConnect": _ = _mqtt.ConnectAsync(); SetMqttStatus(); break;
                case "mqttDisconnect": _mqtt.Disconnect(); SetMqttStatus(); break;
                case "detectMyNode": AppendSystem("Detect-my-node requires a connected radio."); break;
                case "telemetrySimSetEnabled": HandleTelemetrySimEnabled(Bool(root, "value")); break;
                case "telemetrySimBroadcast": HandleTelemetrySimBroadcast(Bool(root, "value")); break;
                case "openTelemetryConfig":
                    AppendSystem($"Telemetry rules file: {_telemetryConfigPath} — edit it and it hot-reloads.");
                    break;
                default: /* unknown message: ignore, matching original */ break;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"WebView message error: {ex.Message}");
        }
    }

    private async Task ToggleListeningAsync()
    {
        if (_backend.IsConnected) { await _backend.DisconnectAsync(); return; }
        string? target = _connMode == "ble" ? _selectedBleAddress : _selectedPort;
        if (string.IsNullOrEmpty(target))
        {
            AppendSystem(_connMode == "ble" ? "Select a Bluetooth device first." : "Select a COM port first.");
            return;
        }
        var ct = _connMode == "ble" ? ConnectTarget.Ble(target) : ConnectTarget.Serial(target);
        try { await _backend.ConnectAsync(ct); }
        catch (Exception ex) { AppendSystem($"Connect failed: {ex.Message}"); }
    }

    private void HandleSendMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AppendChat(_nodes.MyIdentityShortName ?? "Me", text, isMine: true);
        _txCount++;
        SetPacketCounters();
        _ = _backend.SendTextAsync(text, null, 0);
    }

    private void HandleNodeCmd(string? nodeId, string action)
    {
        switch (action)
        {
            case "favorite": case "unfavorite":
                if (nodeId != null) _nodes.SetFavorite(nodeId, action == "favorite");
                _ = _backend.SendCommandAsync(new NodeCommand(action, nodeId));
                SetNodes();
                break;
            case "ignore": case "unignore":
                if (nodeId != null)
                {
                    _nodes.SetIgnored(nodeId, action == "ignore");
                    // Propagate to the radio too (matches the original), not just app-side hiding.
                    _ = _backend.SendCommandAsync(new NodeCommand(action, nodeId));
                }
                SetNodes(); SetMapNodes();
                break;
            case "remove":
                if (nodeId != null) _nodes.Remove(nodeId);
                _ = _backend.SendCommandAsync(new NodeCommand(action, nodeId));
                SetNodes(); SetMapNodes();
                break;
            case "requestPosition":
                _ = _backend.SendCommandAsync(new NodeCommand(action, nodeId));
                if (nodeId != null) AppendSystem($"Requested position from {nodeId}.");
                break;
            case "resetNodeDb":
                _nodes.Reset();
                _ = _backend.SendCommandAsync(new NodeCommand(action));
                SetNodes(); SetMapNodes();
                break;
        }
    }

    // ---------------- Inbound: backend events ----------------

    private void OnReady(MeshReadyEvent e)
    {
        if (!string.IsNullOrEmpty(e.MyNodeId) && !string.Equals(e.MyNodeId, _nodes.MyNodeId, StringComparison.OrdinalIgnoreCase))
            SetMyNodeId(e.MyNodeId, announce: false);
        AppendLog("Connected.");
    }

    private void OnNodeDb(IReadOnlyList<NodeSnapshot> snapshot)
    {
        _nodes.ApplyAll(snapshot);
        SetNodes(); SetMapNodes(); PushHeaderStatus();
    }

    private void OnPacket(MeshPacketEvent p)
    {
        _rxCount++;
        SetPacketCounters();
        if (!string.IsNullOrEmpty(p.FromId) && p.FromId != "^all") _nodes.TouchLastHeard(p.FromId);
        if (p.RxSnr.HasValue) { SetSignal(p.RxSnr, p.RxRssi); RecordSignal(p.RxSnr.Value); }

        if (p.PortNum == "TEXT_MESSAGE_APP" && !string.IsNullOrEmpty(p.Text))
        {
            string sender = _nodes.LookupDisplayName(p.FromId ?? "") ?? p.FromId ?? "?";
            if (p.Channel == PrivateChannelIndex && p.FromId != null)
            {
                AppendPrivateMessage(p.FromId, p.Text, isMine: false);
            }
            else
            {
                AppendChat(sender, p.Text, isMine: false);
                _unreadChat++;
                SetChatBadge();
            }
        }
        SetNodes();
    }

    private void OnAck(MeshAck ack)
    {
        if (ack.Ok) AppendLog($"OK {ack.Cmd} {ack.NodeId}".TrimEnd());
        else AppendSystem($"Node action '{ack.Cmd}' failed: {ack.Error ?? "unknown error"}");
    }

    // ---------------- Outbound: host -> JS ----------------

    private void PushInitialState()
    {
        RefreshPorts();
        SetConnectionStatus(_backend.IsConnected);
        PushHeaderStatus();
        SetPacketCounters();
        SetUptime();
        foreach (var (time, text) in _eventLog)
            _push.Call("appendLogLine", time, text);
        SetPrivateContacts();
        SetNodes();
        PushMyIdentity();
        SetSettings();
        SetChatBadge();
        SetSignalHistory();
        SetTelemetrySim();
        _push.Call("setMyNodeIdDisplay", _nodes.MyNodeId);
        SetMyNodeLabel();

        _tickTimer ??= new System.Threading.Timer(_ => _ui.Post(() => { SetUptime(); }), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void SetNodes()
    {
        _push.Call("setNodes", _nodes.BuildNodesPayload());
        _push.Call("setNodesBadge", _nodes.ActiveNodeCount);
        SetBroadcastInfo();
        PushMyIdentity();
    }

    private void SetMapNodes() => _push.Call("setMapNodes", _nodes.BuildMapNodesPayload());
    private void SetGlobalMapNodes() => _push.Call("setGlobalMapNodes", _mqtt.BuildGlobalMapNodesPayload(_nodes.MyNodeId));
    private void SetZones() => _push.Call("setZones", _zones.Select(z => new { name = z.Name, lat1 = z.Lat1, lng1 = z.Lng1, lat2 = z.Lat2, lng2 = z.Lng2 }).ToList());
    private void SetMqttStatus() =>
        _push.Raw($"window.LoRaChatUI && window.LoRaChatUI.setMqttStatus && window.LoRaChatUI.setMqttStatus({(_mqtt.IsConnected ? "true" : "false")}, {_mqtt.GlobalNodeCount});");
    private void SetMyNodeLabel()
    {
        string text = string.IsNullOrEmpty(_nodes.MyNodeId) ? "Мой узел: не задан" : $"⭐ Мой узел: {_nodes.MyNodeId}";
        _push.Call("setMyNodeLabel", text);
    }

    private void PushMyIdentity()
    {
        string ln = _nodes.MyIdentityLongName ?? _settings.Current.OwnerLongName ?? "";
        string sn = _nodes.MyIdentityShortName ?? _settings.Current.OwnerShortName ?? "";
        _push.Call("setMyIdentity", ln, sn);
    }

    private void PushHeaderStatus()
    {
        string region = _settings.Current.Region >= 0 && _settings.Current.Region < MeshFrequency.RegionNames.Length
            ? MeshFrequency.RegionNames[_settings.Current.Region] : "-";
        string freq = CalculatedFreqText();
        string channel = string.IsNullOrWhiteSpace(_settings.Current.PrimaryChannelName) ? "-" : _settings.Current.PrimaryChannelName;
        string nodeId = string.IsNullOrEmpty(_nodes.MyNodeId) ? "-" : _nodes.MyNodeId;
        _push.Raw($"window.LoRaChatUI && window.LoRaChatUI.setHeaderStatus({{region:{WebViewPush.Serialize(region)},freq:{WebViewPush.Serialize(freq)},channel:{WebViewPush.Serialize(channel)},nodeCount:{_nodes.ActiveNodeCount},nodeId:{WebViewPush.Serialize(nodeId)}}});");
    }

    private void SetBroadcastInfo()
    {
        int count = _nodes.ActiveNodeCount;
        string text = $"Широковещательный · {CalculatedFreqText()} · {count} {MeshFrequency.RussianNodeWord(count)} в эфире";
        _push.Call("setBroadcastInfo", text);
    }

    private void SetConnectionStatus(bool connected) => _push.Call("setConnectionStatus", connected, _selectedPort ?? "");
    private void SetPacketCounters() => _push.Call("setPacketCounters", _rxCount, _txCount);
    private void SetChatBadge() => _push.Call("setChatBadge", _unreadChat);
    private void SetSignal(double? snr, double? rssi) => _push.Call("setSignal", snr, rssi);
    private void SetSignalHistory() => _push.Call("setSignalBars", _snrHistory);
    private void SetTelemetrySim() => _push.Call("setTelemetrySim", new
    {
        enabled = _telemetrySim.Enabled,
        activeRules = _telemetrySim.ActiveRuleCount,
        summary = _telemetrySim.Enabled ? _telemetrySim.Describe() : "Off",
        broadcast = _simBroadcast,
    });

    private void SetUptime()
    {
        TimeSpan e = DateTime.Now - _startTime;
        _push.Call("setUptime", $"{(int)e.TotalHours:00}:{e.Minutes:00}:{e.Seconds:00}");
    }

    private void RecordSignal(double snr)
    {
        _snrHistory.Add(snr);
        while (_snrHistory.Count > 16) _snrHistory.RemoveAt(0);
        SetSignalHistory();
    }

    private void SetSettings() => _push.Call("setSettings", SettingsPayload.Build(_settings.Current, CalculatedFreqText()));

    // ---------------- Chat ----------------

    private void AppendChat(string sender, string text, bool isMine) => _push.Call("appendChatMessage", sender, text, isMine);

    private void AppendLog(string text)
    {
        string time = DateTime.Now.ToString("HH:mm:ss");
        _eventLog.Add((time, text));
        if (_eventLog.Count > MaxEventLogEntries) _eventLog.RemoveAt(0);
        _push.Call("appendLogLine", time, text);
    }

    private void AppendSystem(string msg) => AppendLog($"SYSTEM {msg}");

    // ---------------- Private chat ----------------

    private void SelectPrivateContact(string? nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || !_privateContactIds.Contains(nodeId)) return;
        _selectedPrivateContactId = nodeId;
        if (_privateChats.TryGetValue(nodeId, out var hist))
            _push.Call("setPrivateMessages", nodeId, hist.Select(h => new { text = h.Text, isMine = h.IsMine }).ToList());
    }

    private void OpenPrivateChat(string? nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        if (!_privateContactIds.Contains(nodeId)) _privateContactIds.Add(nodeId);
        _selectedPrivateContactId = nodeId;
        SetPrivateContacts();
        SelectPrivateContact(nodeId);
    }

    private void HandleSendPrivate(string? nodeId, string? text)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(text)) return;
        AppendPrivateMessage(nodeId, text, isMine: true);
        _ = _backend.SendTextAsync(text, nodeId, PrivateChannelIndex);
    }

    private void AppendPrivateMessage(string nodeId, string text, bool isMine)
    {
        if (!_privateChats.TryGetValue(nodeId, out var hist)) { hist = new(); _privateChats[nodeId] = hist; }
        hist.Add((text, isMine));
        if (!_privateContactIds.Contains(nodeId)) { _privateContactIds.Add(nodeId); SetPrivateContacts(); }
        _push.Call("appendPrivateMessage", nodeId, text, isMine);
    }

    private void DeletePrivateChat(string? nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        _privateChats.Remove(nodeId);
        _privateContactIds.Remove(nodeId);
        SetPrivateContacts();
    }

    private void SetPrivateContacts()
    {
        var contacts = _privateContactIds.Select(id => new { id, name = _nodes.LookupDisplayName(id) }).ToList();
        _push.Call("setPrivateContacts", contacts, _selectedPrivateContactId ?? "");
    }

    // ---------------- Ports / BLE ----------------

    private void RefreshPorts()
    {
        _availablePorts = _serial.GetPortNames().ToList();
        _push.Call("setPorts", _availablePorts, _selectedPort ?? "");
    }

    private void SelectPort(string? port)
    {
        if (string.IsNullOrEmpty(port) || !_availablePorts.Contains(port)) return;
        _selectedPort = port;
        _connMode = "serial";
    }

    private void SelectBle(string? address)
    {
        if (string.IsNullOrEmpty(address)) return;
        _selectedBleAddress = address;
        _connMode = "ble";
    }

    private async Task ScanBleAsync()
    {
        _push.Call("setBleScanStatus", "Scanning for Bluetooth devices…", true);
        try
        {
            var devices = await _ble.ScanAsync(TimeSpan.FromSeconds(10));
            _push.Call("setBleDevices", devices.Select(d => new { address = d.Address, name = d.Name }).ToList(), _selectedBleAddress ?? "");
            _push.Call("setBleScanStatus", devices.Count == 0 ? "No devices found." : $"Found {devices.Count} device(s).", false);
        }
        catch (Exception ex)
        {
            _push.Call("setBleScanStatus", $"Scan failed: {ex.Message}", false);
        }
    }

    // ---------------- Settings / identity ----------------

    private void HandleSetOwner(string? longName, string? shortName)
    {
        if (longName != null) _settings.Current.OwnerLongName = longName;
        if (shortName != null) _settings.Current.OwnerShortName = shortName;
        _settings.Save();
        PushMyIdentity();
    }

    private void ApplySettings(JsonElement s)
    {
        SettingsPayload.Apply(_settings.Current, s);
        _settings.Save();
        _nodes.MyNodeId = _settings.Current.MyNodeId;
        SetSettings();
        PushHeaderStatus();
        _push.Call("setApplyStatus", "Settings saved.", "#4ade80");
    }

    private void ResetSettings()
    {
        _settings.ResetToDefaults();
        _nodes.MyNodeId = _settings.Current.MyNodeId;
        SetSettings();
        PushHeaderStatus();
    }

    private void GeneratePrivatePsk()
    {
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        _settings.Current.PrivateChannelPsk = Convert.ToBase64String(key);
        _settings.Save();
        _push.Call("setPrivatePsk", _settings.Current.PrivateChannelPsk);
    }

    private void SetMyNodeId(string? id, bool announce)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        _settings.Current.MyNodeId = id;
        _nodes.MyNodeId = id;
        _settings.Save();
        _push.Call("setMyNodeIdDisplay", id);
        SetMyNodeLabel();
        PushHeaderStatus();
        SetNodes();
        if (announce) AppendSystem($"Your node id is now: {id}");
    }

    private static string GenerateNodeId()
    {
        var b = new byte[4];
        System.Security.Cryptography.RandomNumberGenerator.Fill(b);
        return "!" + Convert.ToHexString(b).ToLowerInvariant();
    }

    private string CalculatedFreqText() => MeshFrequency.Calculate(_settings.Current);

    // ---------------- Telemetry simulation ----------------

    private void HandleTelemetrySimEnabled(bool? value)
    {
        bool en = value ?? !_telemetrySim.Enabled;
        _telemetrySim.SetEnabled(en);
        // Disabling the simulation must also stop spoofing the mesh, or the device would be left
        // pinned to a fixed position with the sim off (matches Form1 behavior).
        if (!en && _simBroadcast) SetSimBroadcast(false);
        SetTelemetrySim();
    }

    private void HandleTelemetrySimBroadcast(bool? value) => SetSimBroadcast(value ?? !_simBroadcast);

    private void SetSimBroadcast(bool on)
    {
        _simBroadcast = on;
        // The actual mesh injection (fixed position + telemetry packets) requires a connected radio
        // and admin messages, landing with the native backend in Phase 2. For now, drive the flag,
        // ask the backend (the demo backend acks), and reflect state in the UI.
        if (on)
        {
            _ = _backend.SendCommandAsync(new NodeCommand("setFixedPosition")
            {
                Lat = _settings.Current.FixedLat, Lon = _settings.Current.FixedLon, Alt = _settings.Current.FixedAlt,
            });
            AppendSystem("Simulated telemetry broadcast armed.");
        }
        else
        {
            _ = _backend.SendCommandAsync(new NodeCommand("clearFixedPosition"));
            AppendSystem("Simulated telemetry broadcast disarmed.");
        }
        SetTelemetrySim();
    }

    // ---------------- Console ----------------

    private void RunConsoleCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        // Console output goes to the console pane (appendConsoleOutput), not the airtime log.
        // Kinds: "cmd" (echoed input), "err" (error), "info" (dim), default (normal).
        ConsoleOut($"> {command}", "cmd");
        switch (command.Trim().ToLowerInvariant())
        {
            case "help":
                ConsoleOut("Available: help, nodes, status. Full device CLI passthrough arrives with over-the-air config sync.", "info");
                break;
            case "nodes":
                ConsoleOut($"{_nodes.ActiveNodeCount} active node(s), {_nodes.Nodes.Count} known.");
                break;
            case "status":
                ConsoleOut($"Connected: {_backend.IsConnected}. MQTT: {_mqtt.IsConnected}. My node: {(_nodes.MyNodeId is { Length: > 0 } id ? id : "unset")}.");
                break;
            default:
                ConsoleOut("Unknown command. Type 'help'.", "err");
                break;
        }
    }

    private void ConsoleOut(string text, string? kind = null) => _push.Call("appendConsoleOutput", text, kind);

    // ---------------- Tiles ----------------

    private async Task DownloadTilesAsync()
    {
        if (_tiles.IsDownloading) { AppendSystem("Tile download already in progress."); return; }
        AppendSystem("Downloading world tiles (zoom 0–7)…");
        await _tiles.DownloadWorldAsync(
            onProgress: text => _ui.Post(() => SetDownloadProgress(text)),
            onComplete: count => _ui.Post(async () =>
            {
                SetDownloadProgress("");
                if (count is int n) await _dialogs.ShowMessageAsync($"Download complete! {n} tiles.", "Done");
                else AppendSystem("Tile download cancelled or failed.");
            }));
    }

    private void SetDownloadProgress(string text) => _push.Call("setDownloadProgress", text);

    // ---------------- Zones ----------------

    private void AddZone(JsonElement root)
    {
        if (!root.TryGetProperty("zone", out var z)) return;
        string? name = Str(z, "name");
        if (string.IsNullOrWhiteSpace(name)) return;
        _zones.Add(new ZoneData
        {
            Name = name,
            Lat1 = z.GetProperty("lat1").GetDouble(),
            Lng1 = z.GetProperty("lng1").GetDouble(),
            Lat2 = z.GetProperty("lat2").GetDouble(),
            Lng2 = z.GetProperty("lng2").GetDouble(),
        });
        SaveZones();
        SetZones();
    }

    private void DeleteZone(string? name)
    {
        if (name == null) return;
        _zones.RemoveAll(z => z.Name == name);
        SaveZones();
        SetZones();
    }

    private void LoadZones()
    {
        try
        {
            if (!File.Exists(_zonesPath)) return;
            var list = JsonSerializer.Deserialize<List<ZoneData>>(File.ReadAllText(_zonesPath));
            if (list != null) _zones.AddRange(list);
        }
        catch { }
    }

    private void SaveZones()
    {
        try { File.WriteAllText(_zonesPath, JsonSerializer.Serialize(_zones)); } catch { }
    }

    // ---------------- JSON helpers (ported from Form1) ----------------

    private static string? Str(JsonElement o, string k) =>
        o.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    private static bool? Bool(JsonElement o, string k) =>
        o.TryGetProperty(k, out var e) && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False) ? e.GetBoolean() : null;

    public void Dispose()
    {
        _tickTimer?.Dispose();
        _telemetrySim.Dispose();
        _mqtt.Dispose();
        _host.MessageReceived -= OnMessage;
    }
}
