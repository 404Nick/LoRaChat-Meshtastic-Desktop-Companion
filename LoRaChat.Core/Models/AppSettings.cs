using System.Collections.Generic;

namespace LoRaChat.Core.Models;

/// <summary>
/// Persisted application + device settings. Ported verbatim from Form1.AppSettings so the settings
/// screen and config-apply logic keep the exact same fields. Serialized to <c>settings.json</c> under
/// the app data directory; secret fields (MQTT passwords, admin key) are protected at rest via
/// <see cref="Abstractions.ISecretStore"/> instead of the original Windows-only DPAPI.
/// </summary>
public sealed class AppSettings
{
    // Minutes since last heard before a node is treated as offline (~2h, matching Meshtastic).
    public bool ShowNodes { get; set; } = true;
    public int NodeTimeout { get; set; } = 120;
    public List<string> IgnoredNodeIds { get; set; } = new();
    public bool SimIntervalBumped { get; set; }
    public string PrivateKey { get; set; } = "AQ==";
    public string ChannelName { get; set; } = "LongFast";
    public bool Stealth { get; set; }
    public bool RandomHop { get; set; }
    public bool RotateNodeId { get; set; }
    public bool Jitter { get; set; }
    public int Region { get; set; } = 9;
    public int Profile { get; set; }
    public int HopLimit { get; set; } = 3;
    public int TxPower { get; set; } = 22;
    public int ChannelNum { get; set; }
    public bool GpsEnabled { get; set; } = true;
    public int GpsUpdateSecs { get; set; } = 30;
    public int PositionBroadcastSecs { get; set; } = 300;
    public bool FixedPosition { get; set; }
    public bool SmartBroadcast { get; set; } = true;
    public int SmartMinDistance { get; set; } = 100;
    public int SmartMinInterval { get; set; } = 30;
    public bool PowerSaving { get; set; }
    public bool WifiEnabled { get; set; }
    public string WifiSsid { get; set; } = "";
    public string WifiPsk { get; set; } = "";
    public int Role { get; set; }
    public int RebroadcastMode { get; set; }
    public int NodeInfoBroadcastSecs { get; set; } = 900;
    public int ScreenOnSecs { get; set; } = 60;
    public int CarouselInterval { get; set; }
    public bool FlipScreen { get; set; }
    public bool BluetoothEnabled { get; set; } = true;
    public int BluetoothMode { get; set; }
    public int BluetoothPin { get; set; }
    public string PrimaryChannelName { get; set; } = "LongFast";
    public string PrimaryChannelPsk { get; set; } = "AQ==";
    public bool PrimaryUplinkEnabled { get; set; } = true;
    public bool PrimaryDownlinkEnabled { get; set; }
    public int SecondaryChannelCount { get; set; }
    public string PrivateChannelName { get; set; } = "PrivateChat";
    public string PrivateChannelPsk { get; set; } = "AQ==";
    public bool PrivateChannelEnabled { get; set; }
    public int GpsMode { get; set; } = 1;
    public string PrivateGpsDestNode { get; set; } = "";
    public int PrivateGpsIntervalSecs { get; set; } = 60;
    public string MqttBroker { get; set; } = "mqtt.meshtastic.org";
    public string? MqttUsername { get; set; }
    public string? MqttPassword { get; set; }
    public string MyNodeId { get; set; } = "";
    public string OwnerLongName { get; set; } = "";
    public string OwnerShortName { get; set; } = "";
    public bool OverrideFrequencyEnabled { get; set; }
    public double OverrideFrequencyValue { get; set; } = 869.075;
    public string Language { get; set; } = "en";
    public string Theme { get; set; } = "tactical";

    // --- Telemetry module ---
    public int DeviceMetricsInterval { get; set; }
    public bool EnvMetricsEnabled { get; set; }
    public int EnvMetricsInterval { get; set; }
    public bool AirQualityEnabled { get; set; }
    public int AirQualityInterval { get; set; }
    public bool PowerMetricsEnabled { get; set; }
    public int PowerMetricsInterval { get; set; }
    // --- Position extras ---
    public int GpsAttemptTime { get; set; }
    public int PositionFlags { get; set; }
    public double FixedLat { get; set; }
    public double FixedLon { get; set; }
    public int FixedAlt { get; set; }
    // --- LoRa extras ---
    public double FreqOffset { get; set; }
    public bool TxEnabled { get; set; } = true;
    public bool OverrideDutyCycle { get; set; }
    public bool UseModemPreset { get; set; } = true;
    public int Bandwidth { get; set; }
    public int SpreadFactor { get; set; }
    public int CodingRate { get; set; }
    // --- Display extras ---
    public int DisplayMode { get; set; }
    public int Units { get; set; }
    public bool WakeOnMotion { get; set; }
    public bool Use12hClock { get; set; }
    public bool CompassNorthTop { get; set; }
    // --- Device extras ---
    public bool SerialConsoleEnabled { get; set; } = true;
    public bool LedHeartbeatDisabled { get; set; }
    // --- Device MQTT module ---
    public bool MqttModuleEnabled { get; set; }
    public string MqttModuleAddress { get; set; } = "";
    public string MqttModuleUsername { get; set; } = "";
    public string? MqttModulePassword { get; set; }
    public bool MqttEncryption { get; set; }
    public bool MqttJson { get; set; }
    public bool MqttTls { get; set; }
    public bool MqttProxyToClient { get; set; }
    // --- Security / admin ---
    public bool RemoteAdminEnabled { get; set; }
    public bool DebugLogEnabled { get; set; }
    public string? AdminKey { get; set; }
    // --- Modules ---
    public bool SerialModuleEnabled { get; set; }
    public bool ExtNotifyEnabled { get; set; }
    public bool ExtNotifyBuzzer { get; set; }
    public bool StoreForwardEnabled { get; set; }
    public bool RangeTestEnabled { get; set; }
    public bool CannedMessagesEnabled { get; set; }
    public bool NeighborInfoEnabled { get; set; }
    public int NeighborInfoInterval { get; set; }
    public bool PaxcounterEnabled { get; set; }
    public bool AmbientLightingEnabled { get; set; }
    public bool AudioEnabled { get; set; }
}
