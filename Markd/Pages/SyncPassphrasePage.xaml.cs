using Markd.Core.Localization;
using Markd.Services.Sync;

namespace Markd.Pages;

/// <summary>
/// Asks for the sync passphrase. Entering one (an existing cloud file, or one changed on another device) takes one
/// field; choosing one (a new cloud file, or changing it) takes two that must match, with the loss warning.
/// </summary>
public partial class SyncPassphrasePage : ContentPage, IAbandonablePrompt
{
    public const int MinimumLength = 8;

    private static readonly PassphrasePromptResult Cancelled = new(null, false);

    private readonly TaskCompletionSource<PassphrasePromptResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _choosing;
    private bool _closing;

    public SyncPassphrasePage(PassphrasePromptKind kind, bool previousAttemptFailed)
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

        RepeatField.IsVisible = _choosing;
        WarningLabel.IsVisible = _choosing;
        ForgotButton.IsVisible = kind == PassphrasePromptKind.EnterExisting;
        PassphraseEntry.ReturnType = _choosing ? ReturnType.Next : ReturnType.Done;

        if (previousAttemptFailed)
            ShowError(Strings.Sync_FailWrongPassphrase);
    }

    public Task<PassphrasePromptResult> Result => _result.Task;

    public void Abandon()
    {
        // A close in progress completes the result itself, with the answer it closed with.
        if (_closing)
            return;

        _closing = true;
        _result.TrySetResult(Cancelled);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PassphraseEntry.Focus();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_closing)
            PhoneModalPrompt.AbandonIfGoneLater(this);
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(Cancelled);
        return true;
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(Cancelled);

    private async void OnForgotClicked(object? sender, EventArgs e) => await CloseAsync(new PassphrasePromptResult(null, true));

    private void OnPassphraseCompleted(object? sender, EventArgs e)
    {
        if (_choosing)
            RepeatEntry.Focus();
        else
            OnOkClicked(sender, e);
    }

    private async void OnOkClicked(object? sender, EventArgs e)
    {
        var passphrase = PassphraseEntry.Text ?? string.Empty;
        if (passphrase.Length == 0)
            return;

        if (_choosing)
        {
            if (passphrase.Length < MinimumLength)
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

        await CloseAsync(new PassphrasePromptResult(passphrase, false));
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    /// <summary>An error is about what was typed: editing either field clears it.</summary>
    private void OnFieldEdited(object? sender, TextChangedEventArgs e) => ErrorLabel.IsVisible = false;

    private async Task CloseAsync(PassphrasePromptResult result)
    {
        // Claimed before the await, so a second tap during the pop cannot pop the page underneath.
        if (_closing)
            return;

        _closing = true;
        PassphraseEntry.Text = string.Empty;
        RepeatEntry.Text = string.Empty;
        try
        {
            if (PhoneModalPrompt.IsShown(this))
                await Navigation.PopModalAsync(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Closing the passphrase page failed: {ex}");
        }
        finally
        {
            _result.TrySetResult(result);
        }
    }
}
