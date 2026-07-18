using System.Net.Http;

namespace LoRaChat.Core.Services;

/// <summary>
/// Downloads world map tiles (zoom 0–7) from OpenStreetMap into the offline tile cache, ported from
/// Form1.BtnDownloadWorld_Click / SaveTileToCache. Tiles are saved as <c>{cache}/{z}/{x}/{y}.png</c>,
/// which the WebView serves via the <c>tiles.lorachat.local</c> virtual host. Uses HttpClient, so it
/// works on every desktop head; on Android the offline download is optional.
/// </summary>
public sealed class TileService
{
    private readonly string _tileCacheDir;
    private readonly HttpClient _http;
    private CancellationTokenSource? _cts;

    public bool IsDownloading { get; private set; }

    public TileService(string tileCacheDir)
    {
        _tileCacheDir = tileCacheDir;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LoRaChat/1.0");
    }

    /// <summary>Downloads the world tile pyramid. <paramref name="onProgress"/> is called with a short
    /// status string; <paramref name="onComplete"/> with the final count (or null on error/cancel).</summary>
    public async Task DownloadWorldAsync(Action<string> onProgress, Action<int?> onComplete)
    {
        if (IsDownloading) return;
        IsDownloading = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        const int minZoom = 0, maxZoom = 7, delayMs = 250;

        try
        {
            int totalTiles = 0;
            for (int z = minZoom; z <= maxZoom; z++) totalTiles += (1 << z) * (1 << z);
            int downloaded = 0;

            for (int z = minZoom; z <= maxZoom && !token.IsCancellationRequested; z++)
            {
                int tilesPerSide = 1 << z;
                for (int x = 0; x < tilesPerSide && !token.IsCancellationRequested; x++)
                for (int y = 0; y < tilesPerSide && !token.IsCancellationRequested; y++)
                {
                    string url = $"https://tile.openstreetmap.org/{z}/{x}/{y}.png";
                    try
                    {
                        var resp = await _http.GetAsync(url, token);
                        if (resp.IsSuccessStatusCode) SaveTile(z, x, y, await resp.Content.ReadAsByteArrayAsync(token));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* skip a failed tile */ }

                    downloaded++;
                    if (downloaded % 25 == 0)
                    {
                        int pct = (int)(downloaded * 100.0 / totalTiles);
                        onProgress($"{pct}% ({downloaded}/{totalTiles})");
                    }
                    await Task.Delay(delayMs, token);
                }
            }
            onComplete(token.IsCancellationRequested ? null : downloaded);
        }
        catch (OperationCanceledException) { onComplete(null); }
        catch { onComplete(null); }
        finally
        {
            IsDownloading = false;
            _cts?.Dispose();
            _cts = null;
            onProgress("");
        }
    }

    public void Cancel() => _cts?.Cancel();

    private void SaveTile(int zoom, int x, int y, byte[] imageData)
    {
        try
        {
            string dir = Path.Combine(_tileCacheDir, zoom.ToString(), x.ToString());
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, y + ".png"), imageData);
        }
        catch { /* best-effort cache write */ }
    }
}
