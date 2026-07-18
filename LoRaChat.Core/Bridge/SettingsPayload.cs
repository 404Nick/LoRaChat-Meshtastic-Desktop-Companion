using System.Text.Json;
using LoRaChat.Core.Models;

namespace LoRaChat.Core.Bridge;

/// <summary>
/// Maps <see cref="AppSettings"/> to/from the camelCase JSON the settings screen uses. Ported from
/// Form1.WebViewSetSettings (Build) and Form1.ApplySettingsFromWebView (Apply), preserving the exact
/// key names and the per-field range validation.
/// </summary>
public static class SettingsPayload
{
    // Dropdown option counts used to clamp index-valued settings (matches the UI's selects).
    private const int RegionCount = 19, ProfileCount = 9, RoleCount = 8, RebroadcastModeCount = 5,
        BluetoothModeCount = 3, DisplayModeCount = 4, UnitsCount = 2, GpsModeCount = 3;

    public static object Build(AppSettings s, string calculatedFreq) => new
    {
        region = s.Region, profile = s.Profile, hopLimit = s.HopLimit, txPower = s.TxPower, channelNum = s.ChannelNum,
        overrideFreqEnabled = s.OverrideFrequencyEnabled, overrideFreq = s.OverrideFrequencyValue,
        calculatedFreq,
        gpsMode = s.GpsMode, gpsEnabled = s.GpsEnabled, gpsUpdateSecs = s.GpsUpdateSecs,
        positionBroadcastSecs = s.PositionBroadcastSecs, fixedPosition = s.FixedPosition,
        smartBroadcast = s.SmartBroadcast, smartMinDistance = s.SmartMinDistance, smartMinInterval = s.SmartMinInterval,
        powerSaving = s.PowerSaving, wifiEnabled = s.WifiEnabled, wifiSsid = s.WifiSsid, wifiPsk = s.WifiPsk,
        role = s.Role, rebroadcastMode = s.RebroadcastMode, nodeInfoBroadcastSecs = s.NodeInfoBroadcastSecs,
        screenOnSecs = s.ScreenOnSecs, carouselInterval = s.CarouselInterval, flipScreen = s.FlipScreen,
        bluetoothEnabled = s.BluetoothEnabled, bluetoothMode = s.BluetoothMode, bluetoothPin = s.BluetoothPin,
        primaryChannelName = s.PrimaryChannelName, primaryChannelPsk = s.PrimaryChannelPsk,
        primaryUplinkEnabled = s.PrimaryUplinkEnabled, primaryDownlinkEnabled = s.PrimaryDownlinkEnabled,
        secondaryChannelCount = s.SecondaryChannelCount, privateChannelEnabled = s.PrivateChannelEnabled,
        privateChannelName = s.PrivateChannelName, privateChannelPsk = s.PrivateChannelPsk,
        stealth = s.Stealth, randomHop = s.RandomHop, rotateNodeId = s.RotateNodeId, jitter = s.Jitter,
        showNodes = s.ShowNodes, nodeTimeout = s.NodeTimeout,
        privateGpsDestNode = s.PrivateGpsDestNode, privateGpsIntervalSecs = s.PrivateGpsIntervalSecs,
        language = s.Language, theme = s.Theme,
        deviceMetricsInterval = s.DeviceMetricsInterval, envMetricsEnabled = s.EnvMetricsEnabled,
        envMetricsInterval = s.EnvMetricsInterval, airQualityEnabled = s.AirQualityEnabled,
        airQualityInterval = s.AirQualityInterval, powerMetricsEnabled = s.PowerMetricsEnabled,
        powerMetricsInterval = s.PowerMetricsInterval,
        gpsAttemptTime = s.GpsAttemptTime, positionFlags = s.PositionFlags,
        fixedLat = s.FixedLat, fixedLon = s.FixedLon, fixedAlt = s.FixedAlt,
        freqOffset = s.FreqOffset, txEnabled = s.TxEnabled, overrideDutyCycle = s.OverrideDutyCycle,
        useModemPreset = s.UseModemPreset, bandwidth = s.Bandwidth, spreadFactor = s.SpreadFactor, codingRate = s.CodingRate,
        displayMode = s.DisplayMode, units = s.Units, wakeOnMotion = s.WakeOnMotion,
        use12hClock = s.Use12hClock, compassNorthTop = s.CompassNorthTop,
        serialConsoleEnabled = s.SerialConsoleEnabled, ledHeartbeatDisabled = s.LedHeartbeatDisabled,
        mqttModuleEnabled = s.MqttModuleEnabled, mqttModuleAddress = s.MqttModuleAddress,
        mqttModuleUsername = s.MqttModuleUsername, mqttModulePassword = s.MqttModulePassword,
        mqttEncryption = s.MqttEncryption, mqttJson = s.MqttJson, mqttTls = s.MqttTls, mqttProxyToClient = s.MqttProxyToClient,
        remoteAdminEnabled = s.RemoteAdminEnabled, debugLogEnabled = s.DebugLogEnabled, adminKey = s.AdminKey,
        serialModuleEnabled = s.SerialModuleEnabled, extNotifyEnabled = s.ExtNotifyEnabled, extNotifyBuzzer = s.ExtNotifyBuzzer,
        storeForwardEnabled = s.StoreForwardEnabled, rangeTestEnabled = s.RangeTestEnabled,
        cannedMessagesEnabled = s.CannedMessagesEnabled, neighborInfoEnabled = s.NeighborInfoEnabled,
        neighborInfoInterval = s.NeighborInfoInterval, paxcounterEnabled = s.PaxcounterEnabled,
        ambientLightingEnabled = s.AmbientLightingEnabled, audioEnabled = s.AudioEnabled,
    };

    public static void Apply(AppSettings a, JsonElement s)
    {
        void Idx(Action<int> set, string key, int count) { var v = Int(s, key); if (v is >= 0 && v < count) set(v.Value); }
        void I(Action<int> set, string key, int min, int max) { var v = Int(s, key); if (v.HasValue) set(Math.Clamp(v.Value, min, max)); }
        void D(Action<double> set, string key) { var v = Dbl(s, key); if (v.HasValue) set(v.Value); }
        void C(Action<bool> set, string key) { var v = Bln(s, key); if (v.HasValue) set(v.Value); }
        void T(Action<string> set, string key) { var v = Txt(s, key); if (v != null) set(v); }

        Idx(v => a.Region = v, "region", RegionCount);
        Idx(v => a.Profile = v, "profile", ProfileCount);
        I(v => a.HopLimit = v, "hopLimit", 1, 7);
        I(v => a.TxPower = v, "txPower", 0, 30);
        I(v => a.ChannelNum = v, "channelNum", 0, 7);
        C(v => a.OverrideFrequencyEnabled = v, "overrideFreqEnabled");
        D(v => a.OverrideFrequencyValue = v, "overrideFreq");
        Idx(v => a.GpsMode = v, "gpsMode", GpsModeCount);
        C(v => a.GpsEnabled = v, "gpsEnabled");
        I(v => a.GpsUpdateSecs = v, "gpsUpdateSecs", 0, 86400);
        I(v => a.PositionBroadcastSecs = v, "positionBroadcastSecs", 0, 86400);
        C(v => a.FixedPosition = v, "fixedPosition");
        C(v => a.SmartBroadcast = v, "smartBroadcast");
        I(v => a.SmartMinDistance = v, "smartMinDistance", 0, 10000);
        I(v => a.SmartMinInterval = v, "smartMinInterval", 0, 3600);
        C(v => a.PowerSaving = v, "powerSaving");
        C(v => a.WifiEnabled = v, "wifiEnabled");
        T(v => a.WifiSsid = v, "wifiSsid");
        T(v => a.WifiPsk = v, "wifiPsk");
        Idx(v => a.Role = v, "role", RoleCount);
        Idx(v => a.RebroadcastMode = v, "rebroadcastMode", RebroadcastModeCount);
        I(v => a.NodeInfoBroadcastSecs = v, "nodeInfoBroadcastSecs", 0, 86400);
        I(v => a.ScreenOnSecs = v, "screenOnSecs", 0, 3600);
        I(v => a.CarouselInterval = v, "carouselInterval", 0, 3600);
        C(v => a.FlipScreen = v, "flipScreen");
        C(v => a.BluetoothEnabled = v, "bluetoothEnabled");
        Idx(v => a.BluetoothMode = v, "bluetoothMode", BluetoothModeCount);
        I(v => a.BluetoothPin = v, "bluetoothPin", 0, 999999);
        T(v => a.PrimaryChannelName = v, "primaryChannelName");
        T(v => a.PrimaryChannelPsk = v, "primaryChannelPsk");
        C(v => a.PrimaryUplinkEnabled = v, "primaryUplinkEnabled");
        C(v => a.PrimaryDownlinkEnabled = v, "primaryDownlinkEnabled");
        I(v => a.SecondaryChannelCount = v, "secondaryChannelCount", 0, 7);
        C(v => a.PrivateChannelEnabled = v, "privateChannelEnabled");
        T(v => a.PrivateChannelName = v, "privateChannelName");
        T(v => a.PrivateChannelPsk = v, "privateChannelPsk");
        C(v => a.Stealth = v, "stealth");
        C(v => a.RandomHop = v, "randomHop");
        C(v => a.RotateNodeId = v, "rotateNodeId");
        C(v => a.Jitter = v, "jitter");
        C(v => a.ShowNodes = v, "showNodes");
        I(v => a.NodeTimeout = v, "nodeTimeout", 1, 1440);
        T(v => a.PrivateGpsDestNode = v, "privateGpsDestNode");
        I(v => a.PrivateGpsIntervalSecs = v, "privateGpsIntervalSecs", 10, 3600);

        I(v => a.DeviceMetricsInterval = v, "deviceMetricsInterval", 0, 86400);
        C(v => a.EnvMetricsEnabled = v, "envMetricsEnabled");
        I(v => a.EnvMetricsInterval = v, "envMetricsInterval", 0, 86400);
        C(v => a.AirQualityEnabled = v, "airQualityEnabled");
        I(v => a.AirQualityInterval = v, "airQualityInterval", 0, 86400);
        C(v => a.PowerMetricsEnabled = v, "powerMetricsEnabled");
        I(v => a.PowerMetricsInterval = v, "powerMetricsInterval", 0, 86400);
        I(v => a.GpsAttemptTime = v, "gpsAttemptTime", 0, 86400);
        I(v => a.PositionFlags = v, "positionFlags", 0, 65535);
        D(v => a.FixedLat = v, "fixedLat");
        D(v => a.FixedLon = v, "fixedLon");
        I(v => a.FixedAlt = v, "fixedAlt", -10000, 100000);
        D(v => a.FreqOffset = v, "freqOffset");
        C(v => a.TxEnabled = v, "txEnabled");
        C(v => a.OverrideDutyCycle = v, "overrideDutyCycle");
        C(v => a.UseModemPreset = v, "useModemPreset");
        I(v => a.Bandwidth = v, "bandwidth", 0, 1000);
        I(v => a.SpreadFactor = v, "spreadFactor", 0, 12);
        I(v => a.CodingRate = v, "codingRate", 0, 8);
        Idx(v => a.DisplayMode = v, "displayMode", DisplayModeCount);
        Idx(v => a.Units = v, "units", UnitsCount);
        C(v => a.WakeOnMotion = v, "wakeOnMotion");
        C(v => a.Use12hClock = v, "use12hClock");
        C(v => a.CompassNorthTop = v, "compassNorthTop");
        C(v => a.SerialConsoleEnabled = v, "serialConsoleEnabled");
        C(v => a.LedHeartbeatDisabled = v, "ledHeartbeatDisabled");
        C(v => a.MqttModuleEnabled = v, "mqttModuleEnabled");
        T(v => a.MqttModuleAddress = v, "mqttModuleAddress");
        T(v => a.MqttModuleUsername = v, "mqttModuleUsername");
        T(v => a.MqttModulePassword = v, "mqttModulePassword");
        C(v => a.MqttEncryption = v, "mqttEncryption");
        C(v => a.MqttJson = v, "mqttJson");
        C(v => a.MqttTls = v, "mqttTls");
        C(v => a.MqttProxyToClient = v, "mqttProxyToClient");
        C(v => a.RemoteAdminEnabled = v, "remoteAdminEnabled");
        C(v => a.DebugLogEnabled = v, "debugLogEnabled");
        T(v => a.AdminKey = v, "adminKey");
        C(v => a.SerialModuleEnabled = v, "serialModuleEnabled");
        C(v => a.ExtNotifyEnabled = v, "extNotifyEnabled");
        C(v => a.ExtNotifyBuzzer = v, "extNotifyBuzzer");
        C(v => a.StoreForwardEnabled = v, "storeForwardEnabled");
        C(v => a.RangeTestEnabled = v, "rangeTestEnabled");
        C(v => a.CannedMessagesEnabled = v, "cannedMessagesEnabled");
        C(v => a.NeighborInfoEnabled = v, "neighborInfoEnabled");
        I(v => a.NeighborInfoInterval = v, "neighborInfoInterval", 0, 86400);
        C(v => a.PaxcounterEnabled = v, "paxcounterEnabled");
        C(v => a.AmbientLightingEnabled = v, "ambientLightingEnabled");
        C(v => a.AudioEnabled = v, "audioEnabled");
    }

    private static int? Int(JsonElement o, string k) =>
        o.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int v) ? v : null;
    private static double? Dbl(JsonElement o, string k) =>
        o.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double v) ? v : null;
    private static bool? Bln(JsonElement o, string k) =>
        o.TryGetProperty(k, out var e) && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False) ? e.GetBoolean() : null;
    private static string? Txt(JsonElement o, string k) =>
        o.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
