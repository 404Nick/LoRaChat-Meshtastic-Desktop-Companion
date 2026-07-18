using System;
using System.Threading.Tasks;
using LoRaChat.Core.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LoRaChat.Host;

/// <summary>
/// Cross-platform dialogs via WinUI/Uno <see cref="ContentDialog"/>, replacing the WinForms
/// <c>MessageBox.Show</c> calls. Needs a <see cref="XamlRoot"/> to attach to (supplied by the page).
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly Func<XamlRoot?> _xamlRoot;

    public DialogService(Func<XamlRoot?> xamlRoot) => _xamlRoot = xamlRoot;

    public async Task ShowMessageAsync(string message, string title = "LoRaChat")
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = _xamlRoot(),
        };
        await dlg.ShowAsync();
    }

    public async Task<bool> ConfirmAsync(string message, string title = "LoRaChat")
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _xamlRoot(),
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }
}
