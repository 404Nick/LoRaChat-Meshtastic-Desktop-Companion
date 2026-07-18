namespace LoRaChat.Core.Abstractions;

/// <summary>
/// Platform-agnostic view of the WebView that hosts the HTML/JS UI. Mirrors the exact
/// two-primitive contract the original WinForms host used with WebView2:
///   * JS -> host : the page calls <c>window.chrome.webview.postMessage(obj)</c>; each raw JSON
///                  string surfaces here through <see cref="MessageReceived"/>.
///   * host -> JS : the host calls <see cref="ExecuteScriptAsync"/> with a snippet that invokes
///                  <c>window.LoRaChatUI.&lt;method&gt;(...)</c>.
/// The concrete implementation lives in the head project and wraps Uno's WebView2 control so that
/// nothing in LoRaChat.Core references a UI type.
/// </summary>
public interface IWebViewHost
{
    /// <summary>Raised (on the UI thread) with the raw JSON payload of each JS postMessage.</summary>
    event EventHandler<string>? MessageReceived;

    /// <summary>Runs a snippet of JavaScript in the page. Safe to call before the page finishes
    /// loading — implementations queue until the WebView is ready.</summary>
    Task ExecuteScriptAsync(string javaScript);
}
