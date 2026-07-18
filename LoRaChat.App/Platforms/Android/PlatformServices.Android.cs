using LoRaChat.Core.Abstractions;

namespace LoRaChat.Host;

/// <summary>
/// Android implementations of the platform service factory: Android Keystore for secrets, native BLE
/// GATT, and USB-OTG serial. Instantiated only on the Android head (this file compiles only for
/// net10.0-android).
/// </summary>
public sealed partial class PlatformServices
{
    public partial ISecretStore CreateSecretStore() => new AndroidSecretStore();
    public partial ISerialProvider CreateSerialProvider() => new AndroidSerialProvider();
    public partial IBleProvider CreateBleProvider() => new AndroidBleProvider();

    // The web UI bundle ships as packaged AndroidAssets under assets/WebAssets/. On Android,
    // SetVirtualHostNameToFolderMapping serves directly from the APK assets, so the "folder" is an
    // asset-relative path — NOT a filesystem path. (Passing a filesystem path makes the mapping fail and
    // the WebView fall back to file://, which Android denies with ERR_ACCESS_DENIED.)
    public partial string PrepareWebAssetsDir() => "WebAssets";
}
