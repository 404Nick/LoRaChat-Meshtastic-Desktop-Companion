using System.Text.Json;
using LoRaChat.Core.Abstractions;

namespace LoRaChat.Core.Bridge;

/// <summary>
/// Helper for the host→JS direction: builds and runs <c>window.LoRaChatUI.&lt;method&gt;(...)</c> calls,
/// centralizing the null-guard and JSON-serialization the original Form1.PushToWebView did inline.
/// </summary>
public sealed class WebViewPush
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IWebViewHost _host;

    public WebViewPush(IWebViewHost host) => _host = host;

    /// <summary>Calls <c>window.LoRaChatUI.method(args...)</c>, guarding on both the object and the
    /// method existing (some UI methods are attached late, e.g. after the map loads).</summary>
    public void Call(string method, params object?[] args)
    {
        string argList = string.Join(", ", args.Select(a => JsonSerializer.Serialize(a, Json)));
        string js = $"window.LoRaChatUI && window.LoRaChatUI.{method} && window.LoRaChatUI.{method}({argList});";
        _ = _host.ExecuteScriptAsync(js);
    }

    /// <summary>Runs an arbitrary JS snippet (for the few pushes that pass an inline object literal).</summary>
    public void Raw(string js) => _ = _host.ExecuteScriptAsync(js);

    public static string Serialize(object? value) => JsonSerializer.Serialize(value, Json);
}
