using System.Globalization;
using LoRaChat.Core.Models;

namespace LoRaChat.Core.Meshtastic;

/// <summary>
/// Region metadata and channel-frequency math, ported from Form1 (RegionNames, RegionBaseFreq,
/// the modem profile table, UpdateCalculatedFreq, and RussianNodeWord).
/// </summary>
public static class MeshFrequency
{
    public static readonly string[] RegionNames =
    {
        "UNSET", "US", "EU_433", "EU_868", "CN", "JP", "ANZ", "KR", "TW", "RU",
        "IN", "NZ_865", "TH", "UA_433", "UA_868", "MY_433", "MY_919", "SG_923", "LORA_24"
    };

    // Band-start frequency (MHz) per region, index-aligned with RegionNames.
    private static readonly double[] RegionBaseFreq =
    {
        0.0, 902.0, 433.0, 869.4, 470.0, 920.8, 915.0, 920.0, 920.0, 868.7,
        865.0, 864.0, 920.0, 433.0, 868.0, 433.0, 919.0, 917.0, 2400.0
    };

    // Bandwidth (kHz) per modem profile index.
    private static readonly int[] ProfileBandwidth = { 250, 125, 62, 250, 250, 250, 250, 125, 500 };

    public static string Calculate(AppSettings s)
    {
        if (s.OverrideFrequencyEnabled)
            return s.OverrideFrequencyValue.ToString("F3", CultureInfo.InvariantCulture) + " MHz (override)";

        if (s.Profile < 0 || s.Profile >= ProfileBandwidth.Length) return "869.075 MHz";
        int bw = ProfileBandwidth[s.Profile];

        double freqStart = s.Region >= 0 && s.Region < RegionBaseFreq.Length ? RegionBaseFreq[s.Region] : 0.0;
        if (freqStart <= 0.0) return "— MHz";

        double bwMHz = bw / 1000.0;
        double freq = freqStart + bwMHz / 2.0 + s.ChannelNum * bwMHz;
        return freq.ToString("F3", CultureInfo.InvariantCulture) + " MHz";
    }

    public static string RussianNodeWord(int n)
    {
        int mod100 = n % 100;
        if (mod100 is >= 11 and <= 14) return "узлов";
        return (n % 10) switch { 1 => "узел", 2 or 3 or 4 => "узла", _ => "узлов" };
    }
}
