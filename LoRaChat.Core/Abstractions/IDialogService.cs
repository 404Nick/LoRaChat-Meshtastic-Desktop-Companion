namespace LoRaChat.Core.Abstractions;

/// <summary>
/// Cross-platform replacement for the WinForms <c>MessageBox.Show</c> calls scattered through the
/// original Form1. All members are async because non-Windows UI toolkits present dialogs
/// asynchronously.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows an informational message with a single OK button.</summary>
    Task ShowMessageAsync(string message, string title = "LoRaChat");

    /// <summary>Shows a Yes/No confirmation. Returns true when the user confirms.</summary>
    Task<bool> ConfirmAsync(string message, string title = "LoRaChat");
}
