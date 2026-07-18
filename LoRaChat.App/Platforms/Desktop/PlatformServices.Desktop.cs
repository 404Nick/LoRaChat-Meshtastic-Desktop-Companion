using System;
using System.IO;
using LoRaChat.Core.Abstractions;

namespace LoRaChat.Host;

/// <summary>Desktop (Windows/macOS/Linux) implementations of the platform service factory.</summary>
public sealed partial class PlatformServices
{
    public partial ISecretStore CreateSecretStore() => new DesktopSecretStore();
    public partial ISerialProvider CreateSerialProvider() => new DesktopSerialProvider();
    public partial IBleProvider CreateBleProvider() => new DesktopBleProvider();

    // On desktop the WebAssets folder is copied next to the executable at build time.
    public partial string PrepareWebAssetsDir() => Path.Combine(AppContext.BaseDirectory, "WebAssets");
}
