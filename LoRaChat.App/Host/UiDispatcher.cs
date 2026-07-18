using System;
using LoRaChat.Core.Abstractions;
using Microsoft.UI.Dispatching;

namespace LoRaChat.Host;

/// <summary>Marshals actions onto the UI thread via the WinUI/Uno <see cref="DispatcherQueue"/>.
/// The cross-platform replacement for Form1.SafeInvoke.</summary>
public sealed class UiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue;

    public UiDispatcher(DispatcherQueue queue) => _queue = queue;

    public void Post(Action action)
    {
        if (_queue.HasThreadAccess) action();
        else _queue.TryEnqueue(() => action());
    }
}
