using LoRaChat.Core.Abstractions;

namespace LoRaChat.Host;

/// <summary>
/// Factory for the platform-specific service implementations. The partial method bodies live under
/// <c>Platforms/Desktop</c> and <c>Platforms/Android</c>, which the Uno single project compiles only
/// for their respective heads — so shared code (MainPage) can construct the right implementations
/// without any <c>#if</c> at the call site.
/// </summary>
public sealed partial class PlatformServices
{
    public partial ISecretStore CreateSecretStore();
    public partial ISerialProvider CreateSerialProvider();
    public partial IBleProvider CreateBleProvider();

    /// <summary>Returns a filesystem directory holding the web UI bundle (index.html + leaflet), ready
    /// for the WebView's virtual-host mapping. Desktop uses the copied output folder; Android extracts
    /// the packaged assets to the app's files dir.</summary>
    public partial string PrepareWebAssetsDir();
}
