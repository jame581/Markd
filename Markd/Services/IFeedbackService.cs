namespace Markd.Services;

/// <summary>
/// Transient feedback after an action: a snackbar on phones, a toast card on the desktop shell.
/// </summary>
public interface IFeedbackService
{
    Task ShowAsync(string title, string? detail = null);

    /// <summary>
    /// Shows a message with an UNDO action. Completes with true when the user tapped UNDO
    /// before the message timed out. Platforms without undo complete with false.
    /// </summary>
    Task<bool> ShowUndoAsync(string message);
}
