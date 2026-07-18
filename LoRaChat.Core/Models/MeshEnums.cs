namespace LoRaChat.Core.Models;

/// <summary>Meshtastic device role, ported from Form1's Role enum. Used to turn a numeric role in an
/// MQTT message into a readable name.</summary>
public enum Role
{
    CLIENT = 0, CLIENT_MUTE = 1, ROUTER = 2, ROUTER_CLIENT = 3,
    REPEATER = 4, TRACKER = 5, SENSOR = 6, TAK = 7
}
