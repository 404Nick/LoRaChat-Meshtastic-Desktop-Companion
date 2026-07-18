using System.Text.Json;
using LoRaChat.Core.Abstractions;
using LoRaChat.Core.Models;

namespace LoRaChat.Core.Services;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> as <c>settings.json</c> under the app data directory
/// (ported from Form1.LoadSettings/SaveSettings). Secrets are encrypted at rest via
/// <see cref="ISecretStore"/> — the cross-platform replacement for the original Windows-only DPAPI.
/// An <c>ENC1:</c> prefix marks an encrypted file; a legacy plaintext file still loads and is
/// re-encrypted on the next save.
/// </summary>
public sealed class SettingsService
{
    private const string EncPrefix = "ENC1:";
    private readonly ISecretStore _secrets;
    private readonly string _path;

    public AppSettings Current { get; private set; } = new();

    public SettingsService(ISecretStore secrets, string appDataDir)
    {
        _secrets = secrets;
        _path = Path.Combine(appDataDir, "settings.json");
        Load();
    }

    /// <summary>Resets all settings to defaults but preserves identity/owner so the user isn't logged
    /// out of their own node (ported from Form1.ResetSettingsToDefaults intent).</summary>
    public void ResetToDefaults()
    {
        string myId = Current.MyNodeId, ownLn = Current.OwnerLongName, ownSn = Current.OwnerShortName;
        Current = new AppSettings { MyNodeId = myId, OwnerLongName = ownLn, OwnerShortName = ownSn };
        Save();
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Current);
            string protectedBlob = _secrets.Protect(json);
            // Protect may no-op on a platform without a secret store; only add the marker when the
            // payload actually differs from plaintext, so Load can tell the two apart.
            string payload = protectedBlob == json ? json : EncPrefix + protectedBlob;
            File.WriteAllText(_path, payload);
        }
        catch { /* never crash on a failed save */ }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            string raw = File.ReadAllText(_path);
            string json = raw.StartsWith(EncPrefix, StringComparison.Ordinal)
                ? _secrets.Unprotect(raw[EncPrefix.Length..]) ?? raw[EncPrefix.Length..]
                : raw;

            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings == null) return;
            settings.IgnoredNodeIds ??= new List<string>();
            Current = settings;
        }
        catch { /* corrupt/unreadable settings fall back to defaults */ }
    }
}
