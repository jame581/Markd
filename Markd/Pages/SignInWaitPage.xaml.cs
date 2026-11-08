using Markd.Services.Sync;

namespace Markd.Pages;

/// <summary>
/// The phone waiting panel shown while a browser sign-in runs. Cancel and the back button call the owner's cancel
/// action and close the page; <see cref="CloseAsync"/> closes it when the sign-in ends. Modal, so the page beneath
/// (the settings that started the sign-in) cannot be tapped meanwhile.
/// </summary>
public partial class SignInWaitPage : ContentPage, IAbandonablePrompt
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action _cancel;
    private bool _closing;

    public SignInWaitPage(string providerName, Action cancel)
    {
        InitializeComponent();
        TitleLabel.Text = providerName;
        _cancel = cancel;
    }

    /// <summary>Completes once the page has closed (or was abandoned).</summary>
    public Task Closed => _closed.Task;

    public void Abandon()
    {
        // A close in progress completes the result itself.
        if (_closing)
            return;

        _closing = true;
        _cancel();
        _closed.TrySetResult();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_closing)
            PhoneModalPrompt.AbandonIfGoneLater(this);
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CancelAsync();
        return true;
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await CancelAsync();

    private async Task CancelAsync()
    {
        if (_closing)
            return;

        _cancel();
        await CloseAsync();
    }

    public async Task CloseAsync()
    {
        // Claimed before the await, so a second call during the pop cannot pop the page underneath.
        if (_closing)
            return;

        _closing = true;
        try
        {
            if (PhoneModalPrompt.IsShown(this))
                await Navigation.PopModalAsync(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Closing the sign-in wait page failed: {ex}");
        }
        finally
        {
            _closed.TrySetResult();
        }
    }
}
