using System;
using System.IO;
using LoRaChat.Core.Abstractions;

namespace LoRaChat.Host;

/// <summary>
/// Cross-platform app data locations, replacing the original Windows-only
/// <c>LocalApplicationData\GMap.NET</c> root. Uses the per-user local-app-data folder on every
/// platform (on Android this resolves to the app-private files dir).
/// </summary>
public sealed class AppEnvironment : IAppEnvironment
{
    public string AppDataDirectory { get; }
    public string TileCacheDirectory { get; }

    public AppEnvironment()
    {
        string root;
        try { root = Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
        catch { root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoRaChat"); }

        AppDataDirectory = root;
        TileCacheDirectory = Path.Combine(root, "tiles");
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(TileCacheDirectory);
    }
}
