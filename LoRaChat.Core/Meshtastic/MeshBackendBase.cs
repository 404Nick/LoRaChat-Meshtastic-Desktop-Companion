using System.Text;
using LoRaChat.Core.Models;
using LoRaChat.Core.Protocol;
using ProtoBuf;

namespace LoRaChat.Core.Meshtastic;

/// <summary>
/// Transport-independent Meshtastic protocol engine shared by the serial and BLE backends. It owns the
/// connect handshake (send <c>want_config_id</c>, read the streamed NodeDB until <c>config_complete_id</c>),
/// decodes <c>FromRadio</c> messages into node/packet events, and builds <c>ToRadio</c> messages for text
/// and admin commands. Subclasses supply only the transport: how a <c>ToRadio</c> payload is written and how
/// incoming <c>FromRadio</c> payloads are obtained (serial does 0x94 0xC3 framing; BLE reads whole packets).
/// </summary>
public abstract class MeshBackendBase : IMeshBackend
{
    protected const uint Broadcast = 0xFFFFFFFF;

    private readonly Dictionary<uint, NodeSnapshot> _nodes = new();
    private readonly Random _rnd = new();
    private uint _myNum;
    private string? _myNodeId;
    private uint _wantConfigId;
    private bool _configComplete;
    private System.Threading.Timer? _heartbeat;

    public event EventHandler<MeshReadyEvent>? Ready;
    public event EventHandler<IReadOnlyList<NodeSnapshot>>? NodeDb;
    public event EventHandler<MeshPacketEvent>? Packet;
    public event EventHandler<MeshAck>? Ack;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<string>? Log;

    public bool IsConnected { get; private set; }

    // ---- Transport hooks implemented by subclasses ----

    /// <summary>Opens the underlying transport (serial port / BLE connection).</summary>
    protected abstract Task OpenTransportAsync(ConnectTarget target, CancellationToken ct);

    /// <summary>Closes the underlying transport.</summary>
    protected abstract Task CloseTransportAsync();

    /// <summary>Writes one serialized <c>ToRadio</c> payload to the radio (serial frames it; BLE writes raw).</summary>
    protected abstract Task SendToRadioAsync(byte[] toRadioPayload, CancellationToken ct = default);

    protected void LogLine(string message) => Log?.Invoke(this, message);

    // ---- Connection lifecycle ----

    public async Task ConnectAsync(ConnectTarget target, CancellationToken ct = default)
    {
        if (IsConnected) return;
        await OpenTransportAsync(target, ct);

        IsConnected = true;
        _configComplete = false;
        _nodes.Clear();
        ConnectionChanged?.Invoke(this, true);

        _wantConfigId = RandomId();
        await SendAsync(new ToRadio { WantConfigId = _wantConfigId }, ct);

        _heartbeat = new System.Threading.Timer(_ => _ = SendHeartbeatAsync(), null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task DisconnectAsync()
    {
        _heartbeat?.Dispose();
        _heartbeat = null;
        try { if (IsConnected) await SendAsync(new ToRadio { Disconnect = true }); } catch { }
        try { await CloseTransportAsync(); } catch { }
        if (IsConnected)
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(this, false);
        }
    }

    /// <summary>Subclasses call this when the transport delivers a complete <c>FromRadio</c> protobuf.</summary>
    protected void FeedFromRadio(byte[] payload)
    {
        try { HandleFromRadio(Decode<FromRadio>(payload)); }
        catch (Exception ex) { LogLine($"FromRadio decode error: {ex.Message}"); }
    }

    /// <summary>Subclasses call this when the transport faults; it tears the connection down.</summary>
    protected void OnTransportFault(string reason)
    {
        LogLine($"Transport fault: {reason}");
        _ = DisconnectAsync();
    }

    // ---- FromRadio handling ----

    private void HandleFromRadio(FromRadio fr)
    {
        if (fr.MyInfo != null)
        {
            _myNum = fr.MyInfo.MyNodeNum;
            _myNodeId = NumToId(_myNum);
        }
        else if (fr.NodeInfo != null)
        {
            ApplyNodeInfo(fr.NodeInfo);
            if (_configComplete) EmitNodeDb();
        }
        else if (fr.ConfigCompleteId is { } id && id == _wantConfigId)
        {
            _configComplete = true;
            Ready?.Invoke(this, new MeshReadyEvent(_myNodeId, _myNum));
            EmitNodeDb();
            LogLine("Config sync complete.");
        }
        else if (fr.Packet != null)
        {
            HandlePacket(fr.Packet);
        }
    }

    private void HandlePacket(Protocol.MeshPacket pkt)
    {
        string fromId = NumToId(pkt.From);
        double? snr = pkt.RxSnr != 0 ? pkt.RxSnr : null;
        double? rssi = pkt.RxRssi != 0 ? pkt.RxRssi : null;
        string? text = null;
        string? portName = null;

        if (pkt.Decoded is { } d)
        {
            portName = d.Portnum.ToString();
            switch (d.Portnum)
            {
                case PortNum.TEXT_MESSAGE_APP:
                    if (d.Payload != null) text = Encoding.UTF8.GetString(d.Payload);
                    break;
                case PortNum.POSITION_APP:
                    if (d.Payload != null) SetPos(NodeFor(pkt.From), Decode<Position>(d.Payload));
                    break;
                case PortNum.NODEINFO_APP:
                    if (d.Payload != null) ApplyUser(pkt.From, Decode<User>(d.Payload));
                    break;
                case PortNum.TELEMETRY_APP:
                    if (d.Payload != null) { var t = Decode<Telemetry>(d.Payload); if (t.DeviceMetrics != null) SetMetrics(NodeFor(pkt.From), t.DeviceMetrics); }
                    break;
            }
        }
        else if (pkt.Encrypted != null)
        {
            portName = "ENCRYPTED";
        }

        TouchLastHeard(pkt.From);
        Packet?.Invoke(this, new MeshPacketEvent(fromId, NumToId(pkt.To), (int)pkt.Channel, snr, rssi, portName, text));
        if (portName is "POSITION_APP" or "NODEINFO_APP" or "TELEMETRY_APP") EmitNodeDb();
    }

    // ---- Node bookkeeping ----

    private NodeSnapshot NodeFor(uint num)
    {
        if (!_nodes.TryGetValue(num, out var n))
        {
            n = new NodeSnapshot { Num = num, NodeId = NumToId(num) };
            _nodes[num] = n;
        }
        return n;
    }

    private void ApplyNodeInfo(Protocol.NodeInfo ni)
    {
        var n = NodeFor(ni.Num);
        if (ni.User != null)
        {
            n.LongName = ni.User.LongName;
            n.ShortName = ni.User.ShortName;
            n.HwModel = HardwareModelName(ni.User.HwModel);
            n.Role = ((Role)ni.User.Role).ToString();
        }
        if (ni.Position != null) SetPos(n, ni.Position);
        if (ni.Snr != 0) n.Snr = ni.Snr;
        if (ni.LastHeard != 0) n.LastHeard = ni.LastHeard;
        if (ni.HopsAway != 0 || n.HopsAway == null) n.HopsAway = (int)ni.HopsAway;
        if (ni.DeviceMetrics != null) SetMetrics(n, ni.DeviceMetrics);
        n.IsFavorite = ni.IsFavorite;
    }

    private void ApplyUser(uint num, User u)
    {
        var n = NodeFor(num);
        n.LongName = u.LongName;
        n.ShortName = u.ShortName;
        n.HwModel = HardwareModelName(u.HwModel);
        n.Role = ((Role)u.Role).ToString();
    }

    private static void SetPos(NodeSnapshot n, Position p)
    {
        if (p.LatitudeI == 0 && p.LongitudeI == 0) return;
        n.Lat = p.LatitudeI * 1e-7;
        n.Lon = p.LongitudeI * 1e-7;
        if (p.Altitude != 0) n.Alt = p.Altitude;
    }

    private static void SetMetrics(NodeSnapshot n, DeviceMetrics m)
    {
        if (m.BatteryLevel.HasValue) n.Battery = (int)m.BatteryLevel.Value;
        if (m.Voltage.HasValue) n.Voltage = m.Voltage.Value;
    }

    private void TouchLastHeard(uint num)
    {
        if (num == 0 || num == Broadcast) return;
        NodeFor(num).LastHeard = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private void EmitNodeDb() => NodeDb?.Invoke(this, _nodes.Values.ToList());

    // ---- Sending ----

    public Task SendTextAsync(string text, string? destNodeId, int channel)
    {
        var pkt = new Protocol.MeshPacket
        {
            From = _myNum,
            To = destNodeId != null ? IdToNum(destNodeId) : Broadcast,
            Channel = (uint)channel,
            Id = RandomId(),
            WantAck = true,
            HopLimit = 3,
            Decoded = new Data { Portnum = PortNum.TEXT_MESSAGE_APP, Payload = Encoding.UTF8.GetBytes(text) },
        };
        return SendAsync(new ToRadio { Packet = pkt });
    }

    public async Task SendCommandAsync(NodeCommand command)
    {
        if (!IsConnected)
        {
            Ack?.Invoke(this, new MeshAck(command.Cmd, command.NodeId, false, "not connected"));
            return;
        }
        try
        {
            switch (command.Cmd)
            {
                case "favorite": await SendAdmin(new AdminMessage { SetFavoriteNode = IdToNum(command.NodeId!) }); break;
                case "unfavorite": await SendAdmin(new AdminMessage { RemoveFavoriteNode = IdToNum(command.NodeId!) }); break;
                case "ignore": await SendAdmin(new AdminMessage { SetIgnoredNode = IdToNum(command.NodeId!) }); break;
                case "unignore": await SendAdmin(new AdminMessage { RemoveIgnoredNode = IdToNum(command.NodeId!) }); break;
                case "remove": await SendAdmin(new AdminMessage { RemoveByNodenum = IdToNum(command.NodeId!) }); break;
                case "requestPosition": await RequestPositionAsync(command.NodeId!); break;
                case "reboot": await SendAdmin(new AdminMessage { RebootSeconds = 5 }); break;
                case "setFixedPosition":
                    await SendAdmin(new AdminMessage
                    {
                        SetFixedPosition = new Position
                        {
                            LatitudeI = (int)((command.Lat ?? 0) * 1e7),
                            LongitudeI = (int)((command.Lon ?? 0) * 1e7),
                            Altitude = command.Alt ?? 0,
                        }
                    });
                    break;
                case "clearFixedPosition": await SendAdmin(new AdminMessage { RemoveFixedPosition = true }); break;
                case "resetNodeDb": break; // handled app-side
            }
            Ack?.Invoke(this, new MeshAck(command.Cmd, command.NodeId, true, null));
        }
        catch (Exception ex)
        {
            Ack?.Invoke(this, new MeshAck(command.Cmd, command.NodeId, false, ex.Message));
        }
    }

    private Task RequestPositionAsync(string nodeId)
    {
        var pkt = new Protocol.MeshPacket
        {
            From = _myNum,
            To = IdToNum(nodeId),
            Id = RandomId(),
            WantAck = true,
            Decoded = new Data { Portnum = PortNum.POSITION_APP, WantResponse = true, Payload = Encode(new Position()) },
        };
        return SendAsync(new ToRadio { Packet = pkt });
    }

    private Task SendAdmin(AdminMessage admin)
    {
        var pkt = new Protocol.MeshPacket
        {
            From = _myNum,
            To = _myNum,
            Id = RandomId(),
            WantAck = true,
            Decoded = new Data { Portnum = PortNum.ADMIN_APP, Payload = Encode(admin) },
        };
        return SendAsync(new ToRadio { Packet = pkt });
    }

    private async Task SendHeartbeatAsync()
    {
        try { await SendAsync(new ToRadio { Heartbeat = new Heartbeat() }); }
        catch { /* the transport will fault separately if truly dead */ }
    }

    private Task SendAsync(ToRadio msg, CancellationToken ct = default) => SendToRadioAsync(Encode(msg), ct);

    // ---- Helpers ----

    protected uint RandomId() => (uint)_rnd.Next(1, int.MaxValue);
    protected static string NumToId(uint num) => "!" + num.ToString("x8");

    protected static uint IdToNum(string nodeId)
    {
        string hex = nodeId.StartsWith('!') ? nodeId[1..] : nodeId;
        return uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint n) ? n : 0;
    }

    protected static byte[] Encode<T>(T msg)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, msg);
        return ms.ToArray();
    }

    protected static T Decode<T>(byte[] data)
    {
        using var ms = new MemoryStream(data);
        return Serializer.Deserialize<T>(ms);
    }

    private static string HardwareModelName(int hw) => hw switch
    {
        0 => "UNSET", 1 => "TLORA_V2", 4 => "T_BEAM", 9 => "TBEAM", 31 => "RAK4631",
        39 => "HELTEC_WSL_V3", 43 => "HELTEC_V3", _ => $"HW_{hw}",
    };
}
