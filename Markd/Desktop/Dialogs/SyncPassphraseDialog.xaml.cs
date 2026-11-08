using Markd.Core.Localization;
using Markd.Pages;
using Markd.Services.Sync;

namespace Markd.Desktop.Dialogs;

/// <summary>
/// Asks for the sync passphrase (desktop overlay). Entering one takes one field; choosing one (a new cloud file, or
/// changing it) takes two that must match, with the loss warning. Cancel, Escape and <see cref="Cancel"/> complete
/// with a cancelled result.
/// </summary>
public partial class SyncPassphraseDialog : ContentView
{
    private static readonly PassphrasePromptResult Cancelled = new(null, false);

    private readonly TaskCompletionSource<PassphrasePromptResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _choosing;

    public SyncPassphraseDialog(PassphrasePromptKind kind, bool previousAttemptFailed)
    {
        InitializeComponent();
        _choosing = kind is PassphrasePromptKind.CreateNew or PassphrasePromptKind.ChangeOwn;

        (TitleLabel.Text, MessageLabel.Text) = kind switch
        {
            PassphrasePromptKind.CreateNew => (Strings.Sync_PassphraseCreateTitle, Strings.Sync_PassphraseCreateMessage),
            PassphrasePromptKind.EnterExisting => (Strings.Sync_PassphraseEnterTitle, Strings.Sync_PassphraseEnterMessage),
            PassphrasePromptKind.EnterChanged => (Strings.Sync_PassphraseChangedTitle, Strings.Sync_PassphraseChangedMessage),
            _ => (Strings.Sync_PassphraseChangeTitle, Strings.Sync_PassphraseChangeMessage),
        };

        RepeatPanel.IsVisible = _choosing;
        WarningLabel.IsVisible = _choosing;
        ForgotButton.IsVisible = kind == PassphrasePromptKind.EnterExisting;

        if (previousAttemptFailed)
            ShowError(Strings.Sync_FailWrongPassphrase);

        Loaded += (_, _) => PassphraseEntry.Focus();
    }

    public Task<PassphrasePromptResult> Result => _result.Task;

    public void Cancel() => Complete(Cancelled);

    private void OnCancelClicked(object? sender, EventArgs e) => Cancel();

    private void OnForgotClicked(object? sender, EventArgs e) => Complete(new PassphrasePromptResult(null, true));

    private void OnPassphraseCompleted(object? sender, EventArgs e)
    {
        if (_choosing)
            RepeatEntry.Focus();
        else
            OnContinueClicked(sender, e);
    }

    private void OnContinueClicked(object? sender, EventArgs e)
    {
        var passphrase = PassphraseEntry.Text ?? string.Empty;
        if (passphrase.Length == 0)
            return;

        if (_choosing)
        {
            if (passphrase.Length < SyncPassphrasePage.MinimumLength)
            {
                ShowError(Strings.Sync_PassphraseTooShort);
                return;
            }

            if (passphrase != (RepeatEntry.Text ?? string.Empty))
            {
                ShowError(Strings.Sync_PassphraseMismatch);
                return;
            }
        }

        Complete(new PassphrasePromptResult(passphrase, false));
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    /// <summary>Clears both entries either way, like the phone page.</summary>
    private void Complete(PassphrasePromptResult result)
    {
        PassphraseEntry.Text = string.Empty;
        RepeatEntry.Text = string.Empty;
        _result.TrySetResult(result);
    }
}
