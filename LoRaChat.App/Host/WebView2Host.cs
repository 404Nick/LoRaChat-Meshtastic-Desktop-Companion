using System;
using System.IO;
using System.Threading.Tasks;
using LoRaChat.Core.Abstractions;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace LoRaChat.Host;

/// <summary>
/// Wraps Uno's <see cref="WebView2"/> control as the platform-agnostic <see cref="IWebViewHost"/>
/// the Core bridge talks to. This is the single place that knows about the concrete WebView type;
/// everything in LoRaChat.Core stays UI-free.
///
/// Loading strategy mirrors the original WinForms host: map the bundled HTML folder (and the offline
/// tile cache) to https virtual hosts so the page's relative <c>leaflet.js</c> refs and its
/// <c>https://tiles.lorachat.local/...</c> tile URLs resolve. If a platform's WebView doesn't support
/// virtual-host mapping we fall back to navigating a <c>file://</c> URL, which still resolves the
/// relative asset refs.
/// </summary>
public sealed class WebView2Host : IWebViewHost
{
    public const string AppHost = "app.lorachat.local";
    public const string TilesHost = "tiles.lorachat.local";

    private readonly WebView2 _web;
    private readonly string _webAssetsDir;
    private readonly string _tileCacheDir;
    private readonly Action<string>? _log;
    private readonly TaskCompletionSource<bool> _coreReady = new();

    public event EventHandler<string>? MessageReceived;

    public WebView2Host(WebView2 web, string webAssetsDir, string tileCacheDir, Action<string>? log = null)
    {
        _web = web;
        _webAssetsDir = webAssetsDir;
        _tileCacheDir = tileCacheDir;
        _log = log;
    }

    /// <summary>Ensures the underlying WebView2 core is created, wires messaging, and navigates to
    /// the bundled UI. Call once from the page's Loaded handler.</summary>
    public async Task StartAsync()
    {
        _log?.Invoke("StartAsync: begin EnsureCoreWebView2Async");
        await _web.EnsureCoreWebView2Async();
        CoreWebView2 core = _web.CoreWebView2;
        _log?.Invoke("StartAsync: core created");

        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationCompleted += OnNavigationCompleted;

        // On Android, SetVirtualHostNameToFolderMapping serves from the APK assets: the folder must be an
        // asset-relative path (PrepareWebAssetsDir returns "WebAssets"), and navigation must use http:// —
        // https to a virtual host triggers certificate/CORS errors, and a file:// fallback is denied
        // (ERR_ACCESS_DENIED). On desktop the folder is a real filesystem path and https works.
        _isAndroid = OperatingSystem.IsAndroid();
        string scheme = _isAndroid ? "http" : "https";

        bool mapped = TryMapVirtualHosts(core);
        _coreReady.TrySetResult(true);

        // The file:// fallback only applies to desktop; on Android _webAssetsDir is an asset path (not a
        // filesystem path) and file:// access is blocked anyway.
        _fileUri = _isAndroid ? null : new Uri(new Uri(Path.Combine(_webAssetsDir, "index.html")).AbsoluteUri);
        _usedVirtualHost = mapped;
        Uri target = mapped
            ? new Uri($"{scheme}://{AppHost}/index.html")
            : (_fileUri ?? new Uri($"{scheme}://{AppHost}/index.html"));

        _log?.Invoke($"StartAsync: android={_isAndroid} mapped={mapped} navigating to {target}");
        _web.Source = target;
    }

    private Uri? _fileUri;
    private bool _usedVirtualHost;
    private bool _triedFileFallback;
    private bool _isAndroid;

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _log?.Invoke($"NavigationCompleted: success={e.IsSuccess} status={e.WebErrorStatus}");
        // Some non-Windows WebView backends don't honor virtual-host mapping. If the https load failed,
        // retry once from file:// (which resolves the page's relative asset refs on every platform).
        if (!e.IsSuccess && _usedVirtualHost && !_triedFileFallback && _fileUri != null)
        {
            _triedFileFallback = true;
            _log?.Invoke($"NavigationCompleted: retrying via {_fileUri}");
            _web.Source = _fileUri;
        }
    }

    private bool TryMapVirtualHosts(CoreWebView2 core)
    {
        try
        {
            core.SetVirtualHostNameToFolderMapping(AppHost, _webAssetsDir, CoreWebView2HostResourceAccessKind.Allow);
            // Offline tiles map a runtime *filesystem* folder. On Android the mapping serves APK assets
            // only (not arbitrary filesystem paths), so skip it there — offline tiles are desktop-only;
            // online map tiles still work on Android.
            if (!_isAndroid && Directory.Exists(_tileCacheDir))
            {
                core.SetVirtualHostNameToFolderMapping(TilesHost, _tileCacheDir, CoreWebView2HostResourceAccessKind.Allow);
            }
            return true;
        }
        catch
        {
            // Not supported on this platform's WebView backend — caller falls back to file:// (desktop).
            return false;
        }
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string json;
        try
        {
            json = args.WebMessageAsJson;
        }
        catch
        {
            try { json = args.TryGetWebMessageAsString(); }
            catch { return; }
        }

        if (!string.IsNullOrEmpty(json))
        {
            MessageReceived?.Invoke(this, json);
        }
    }

    public async Task ExecuteScriptAsync(string javaScript)
    {
        await _coreReady.Task;
        // WebView2 calls must run on the UI thread. The bridge already marshals backend events, but
        // guard here too so any caller is safe.
        if (_web.DispatcherQueue.HasThreadAccess)
        {
            await RunScriptAsync(javaScript);
        }
        else
        {
            _web.DispatcherQueue.TryEnqueue(() => _ = RunScriptAsync(javaScript));
        }
    }

    private async Task RunScriptAsync(string javaScript)
    {
        try
        {
            await _web.CoreWebView2.ExecuteScriptAsync(javaScript);
        }
        catch
        {
            // Page may not have finished loading a given global yet; the UI guards every call with
            // `window.LoRaChatUI && ...`, so a dropped early push is harmless.
        }
    }
}
