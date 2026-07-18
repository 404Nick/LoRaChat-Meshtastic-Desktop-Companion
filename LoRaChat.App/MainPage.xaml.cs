using System;
using System.IO;
using System.Threading.Tasks;
using LoRaChat.Core.Bridge;
using LoRaChat.Core.Meshtastic;
using LoRaChat.Core.Services;
using LoRaChat.Host;
using Microsoft.UI.Xaml;

namespace LoRaChat;

public sealed partial class MainPage : Page
{
    private WebView2Host? _host;
    private UiBridge? _bridge;
    private IMeshBackend? _backend;

    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "lorachat_phase0.log");

    private static void Log(string line)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}"); }
        catch { /* logging must never throw */ }
    }

    public MainPage()
    {
        this.InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            File.WriteAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} OnLoaded{Environment.NewLine}");

            var env = new AppEnvironment();
            var platform = new PlatformServices();
            var secrets = platform.CreateSecretStore();
            var serial = platform.CreateSerialProvider();
            var ble = platform.CreateBleProvider();

            var settings = new SettingsService(secrets, env.AppDataDirectory);
            var nodes = new NodeDbService(settings, env.AppDataDirectory);
            var dispatcher = new UiDispatcher(DispatcherQueue);
            var dialogs = new DialogService(() => this.XamlRoot);
            var mqtt = new MqttService(settings);
            var tiles = new TileService(env.TileCacheDirectory);

            // Native backends: the Listen button opens the selected COM port (serial) or BLE device and
            // runs the real Meshtastic protocol. RoutingMeshBackend picks serial vs BLE by the connect
            // target. Set LORACHAT_DEMO=1 to use the hardware-free demo backend instead.
            bool demo = Environment.GetEnvironmentVariable("LORACHAT_DEMO") == "1";
            _backend = demo
                ? new FakeMeshBackend()
                : new RoutingMeshBackend(new SerialMeshBackend(serial), new BleMeshBackend(ble));

            string webAssetsDir = platform.PrepareWebAssetsDir();
            _host = new WebView2Host(Web, webAssetsDir, env.TileCacheDirectory, Log);

            // Construct the bridge before navigating so it is subscribed by the time the page posts "ready".
            _bridge = new UiBridge(_host, _backend, settings, nodes, dialogs, serial, ble, dispatcher, mqtt, tiles, env.AppDataDirectory);

            await _host.StartAsync();
            Log($"StartAsync: returned (backend={_backend.GetType().Name})");

            if (demo)
            {
                await _backend.ConnectAsync(ConnectTarget.Serial("DEMO"));
                Log("demo backend connected");
            }
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex}");
        }
    }
}
