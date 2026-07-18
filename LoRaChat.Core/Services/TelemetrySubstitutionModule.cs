using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LoRaChat.Core.Services;

// --- Simulation Config Management Service / Telemetry Substitution Module ---
//
// Ported verbatim from the original TelemetrySubstitution.cs (already fully platform-agnostic:
// System.IO / System.Text.Json / FileSystemWatcher). Sits between telemetry collection (the device's
// real GPS/metrics) and transmission: before a metric is sent, the app asks whether an active
// substitution rule exists. Rules live in a hot-reloaded JSON file. Modes: static / offset / random.

public enum TelemetryMode { Static, Offset, Random }

public sealed class TelemetryRule
{
    public string Metric = "";
    public bool Enabled = true;
    public TelemetryMode Mode = TelemetryMode.Static;
    public double Value;
    public string? StringValue;
    public double Offset;
    public double Min;
    public double Max;
}

public sealed class TelemetryMockConfig
{
    public bool Enabled;
    public int RealTelemetryIntervalSecs;
    public List<TelemetryRule> Rules = new();

    public TelemetryRule? Find(string metric) =>
        Rules.FirstOrDefault(r => string.Equals(r.Metric, metric, StringComparison.OrdinalIgnoreCase));
}

public sealed class TelemetrySubstitutionModule : IDisposable
{
    private readonly string _path;
    private readonly object _rndLock = new();
    private readonly Random _rnd = new();
    private volatile TelemetryMockConfig _config = new();

    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;
    private const int DebounceMs = 300;

    public Action<string>? Log;
    public Action? OnConfigChanged;

    public TelemetrySubstitutionModule(string configPath) => _path = configPath;

    public bool Enabled => _config.Enabled;
    public int ActiveRuleCount => _config.Enabled ? _config.Rules.Count(r => r.Enabled) : 0;
    public int RealTelemetryIntervalWhileLive => _config.RealTelemetryIntervalSecs;

    public void Start()
    {
        try
        {
            if (!File.Exists(_path)) WriteDefaultFile();
            LoadFromDisk(initial: true);
        }
        catch (Exception ex) { Log?.Invoke($"[SIM] initial load failed: {ex.Message}"); }

        try
        {
            string? dir = Path.GetDirectoryName(_path);
            string name = Path.GetFileName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                _watcher = new FileSystemWatcher(dir, name)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };
                FileSystemEventHandler onChange = (_, _) => ScheduleReload();
                _watcher.Changed += onChange;
                _watcher.Created += onChange;
                _watcher.Renamed += (_, _) => ScheduleReload();
            }
        }
        catch (Exception ex) { Log?.Invoke($"[SIM] watcher setup failed: {ex.Message}"); }
    }

    private void ScheduleReload()
    {
        _debounce ??= new System.Threading.Timer(_ =>
        {
            try { LoadFromDisk(initial: false); }
            catch (Exception ex) { Log?.Invoke($"[SIM] reload failed: {ex.Message}"); }
        });
        _debounce.Change(DebounceMs, Timeout.Infinite);
    }

    private void LoadFromDisk(bool initial)
    {
        string json;
        try { json = File.ReadAllText(_path); }
        catch (IOException) { return; }

        var parsed = Parse(json);
        if (parsed == null)
        {
            Log?.Invoke("[SIM] config parse error; keeping previous rules");
            return;
        }
        _config = parsed;
        string verb = initial ? "loaded" : "hot-reloaded";
        Log?.Invoke(parsed.Enabled
            ? $"[SIM] config {verb}: ON, {ActiveRuleCount} active rule(s) [{Describe()}]"
            : $"[SIM] config {verb}: OFF (pass-through)");
        OnConfigChanged?.Invoke();
    }

    private static TelemetryMockConfig? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var cfg = new TelemetryMockConfig();
            if (root.TryGetProperty("enabled", out var enEl) &&
                (enEl.ValueKind == JsonValueKind.True || enEl.ValueKind == JsonValueKind.False))
                cfg.Enabled = enEl.GetBoolean();

            if (root.TryGetProperty("realTelemetryIntervalSecs", out var ivEl) && ivEl.ValueKind == JsonValueKind.Number)
                cfg.RealTelemetryIntervalSecs = Math.Max(0, ivEl.GetInt32());

            if (root.TryGetProperty("rules", out var rulesEl) && rulesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var rEl in rulesEl.EnumerateArray())
                {
                    if (rEl.ValueKind != JsonValueKind.Object) continue;
                    string? metric = Str(rEl, "metric");
                    if (string.IsNullOrWhiteSpace(metric)) continue;
                    var rule = new TelemetryRule { Metric = metric.Trim() };

                    if (rEl.TryGetProperty("enabled", out var re) &&
                        (re.ValueKind == JsonValueKind.True || re.ValueKind == JsonValueKind.False))
                        rule.Enabled = re.GetBoolean();

                    rule.Mode = ParseMode(Str(rEl, "mode"));
                    rule.Value = Num(rEl, "value") ?? 0;
                    rule.StringValue = Str(rEl, "stringValue");
                    rule.Offset = Num(rEl, "offset") ?? 0;
                    rule.Min = Num(rEl, "min") ?? 0;
                    rule.Max = Num(rEl, "max") ?? 0;
                    cfg.Rules.Add(rule);
                }
            }
            return cfg;
        }
        catch (JsonException) { return null; }
    }

    private static TelemetryMode ParseMode(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "offset" or "bias" => TelemetryMode.Offset,
        "random" or "noise" => TelemetryMode.Random,
        _ => TelemetryMode.Static,
    };

    private static string? Str(JsonElement o, string k) =>
        o.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    private static double? Num(JsonElement o, string k) =>
        o.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;

    public double Apply(string metric, double original, out bool substituted)
    {
        substituted = false;
        var cfg = _config;
        if (!cfg.Enabled) return original;
        var rule = cfg.Find(metric);
        if (rule == null || !rule.Enabled) return original;

        double result;
        switch (rule.Mode)
        {
            case TelemetryMode.Offset: result = original + rule.Offset; break;
            case TelemetryMode.Random:
                double lo = Math.Min(rule.Min, rule.Max), hi = Math.Max(rule.Min, rule.Max);
                lock (_rndLock) result = lo + _rnd.NextDouble() * (hi - lo);
                break;
            default: result = rule.Value; break;
        }
        substituted = true;
        return result;
    }

    public bool TryApply(string metric, ref double value)
    {
        double outv = Apply(metric, value, out bool sub);
        if (sub)
        {
            Log?.Invoke($"[SIM] {metric}: {value.ToString("0.#####", CultureInfo.InvariantCulture)} -> {outv.ToString("0.#####", CultureInfo.InvariantCulture)}");
            value = outv;
        }
        return sub;
    }

    public string? ApplyString(string metric, string? original, out bool substituted)
    {
        substituted = false;
        var cfg = _config;
        if (!cfg.Enabled) return original;
        var rule = cfg.Find(metric);
        if (rule == null || !rule.Enabled || rule.Mode != TelemetryMode.Static || rule.StringValue == null)
            return original;
        substituted = true;
        return rule.StringValue;
    }

    public string Describe()
    {
        var cfg = _config;
        var parts = cfg.Rules.Where(r => r.Enabled).Select(r => r.Mode switch
        {
            TelemetryMode.Offset => $"{r.Metric}{(r.Offset >= 0 ? "+" : "")}{r.Offset.ToString(CultureInfo.InvariantCulture)}",
            TelemetryMode.Random => $"{r.Metric}=rnd[{r.Min.ToString(CultureInfo.InvariantCulture)},{r.Max.ToString(CultureInfo.InvariantCulture)}]",
            _ => $"{r.Metric}={r.StringValue ?? r.Value.ToString(CultureInfo.InvariantCulture)}",
        });
        return string.Join(", ", parts);
    }

    public void SetEnabled(bool enabled)
    {
        var cfg = _config;
        cfg.Enabled = enabled;
        _config = cfg;
        SaveToDisk(cfg);
        Log?.Invoke($"[SIM] master {(enabled ? "ENABLED" : "disabled")} via UI");
        OnConfigChanged?.Invoke();
    }

    private void SaveToDisk(TelemetryMockConfig cfg)
    {
        try
        {
            if (_watcher != null) _watcher.EnableRaisingEvents = false;
            File.WriteAllText(_path, Serialize(cfg));
        }
        catch (Exception ex) { Log?.Invoke($"[SIM] save failed: {ex.Message}"); }
        finally { if (_watcher != null) _watcher.EnableRaisingEvents = true; }
    }

    private static string Serialize(TelemetryMockConfig cfg)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"enabled\": {(cfg.Enabled ? "true" : "false")},");
        sb.AppendLine($"  \"realTelemetryIntervalSecs\": {cfg.RealTelemetryIntervalSecs},");
        sb.AppendLine("  \"rules\": [");
        for (int i = 0; i < cfg.Rules.Count; i++)
        {
            var r = cfg.Rules[i];
            var fields = new List<string>
            {
                $"\"metric\": {JsonSerializer.Serialize(r.Metric)}",
                $"\"enabled\": {(r.Enabled ? "true" : "false")}",
                $"\"mode\": \"{r.Mode.ToString().ToLowerInvariant()}\""
            };
            if (r.Mode == TelemetryMode.Static)
            {
                if (r.StringValue != null) fields.Add($"\"stringValue\": {JsonSerializer.Serialize(r.StringValue)}");
                else fields.Add($"\"value\": {r.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            else if (r.Mode == TelemetryMode.Offset)
                fields.Add($"\"offset\": {r.Offset.ToString(CultureInfo.InvariantCulture)}");
            else
            {
                fields.Add($"\"min\": {r.Min.ToString(CultureInfo.InvariantCulture)}");
                fields.Add($"\"max\": {r.Max.ToString(CultureInfo.InvariantCulture)}");
            }
            sb.Append("    { " + string.Join(", ", fields) + " }");
            sb.AppendLine(i < cfg.Rules.Count - 1 ? "," : "");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private void WriteDefaultFile()
    {
        var cfg = new TelemetryMockConfig
        {
            Enabled = false,
            RealTelemetryIntervalSecs = 3600,
            Rules = new List<TelemetryRule>
            {
                new() { Metric = "latitude",  Enabled = false, Mode = TelemetryMode.Static, Value = 50.45000 },
                new() { Metric = "longitude", Enabled = false, Mode = TelemetryMode.Offset, Offset = 0.00050 },
                new() { Metric = "altitude",  Enabled = false, Mode = TelemetryMode.Static, Value = 300 },
                new() { Metric = "battery",   Enabled = false, Mode = TelemetryMode.Random, Min = 20, Max = 40 },
                new() { Metric = "voltage",   Enabled = false, Mode = TelemetryMode.Offset, Offset = -0.5 },
                new() { Metric = "temperature", Enabled = false, Mode = TelemetryMode.Static, Value = 100 },
            }
        };
        File.WriteAllText(_path, Serialize(cfg));
    }

    public void Dispose()
    {
        try { if (_watcher != null) { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); } } catch { }
        try { _debounce?.Dispose(); } catch { }
    }
}
