namespace LoRaChat.Core.Abstractions;

/// <summary>
/// Marshals work onto the UI thread, the cross-platform equivalent of the original Form1.SafeInvoke.
/// The mesh backend raises events from background threads; the bridge routes state mutation and all
/// WebView calls through here so they run on the UI thread.
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
