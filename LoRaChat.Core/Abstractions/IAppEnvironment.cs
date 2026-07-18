namespace LoRaChat.Core.Abstractions;

/// <summary>
/// Per-platform filesystem locations. Replaces the original app's hard-coded
/// <c>SpecialFolder.LocalApplicationData\GMap.NET</c> root, which does not exist on macOS/Linux/Android.
/// </summary>
public interface IAppEnvironment
{
    /// <summary>Writable per-user directory for settings.json, the node database, chat history and
    /// the map tile cache. Guaranteed to exist by the time this is handed to the bridge.</summary>
    string AppDataDirectory { get; }

    /// <summary>Directory that map tiles are cached under (a subfolder of
    /// <see cref="AppDataDirectory"/>).</summary>
    string TileCacheDirectory { get; }
}
