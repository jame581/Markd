using Markd.Services.Sync;

namespace Markd.Pages;

/// <summary>
/// The phone waiting panel shown while a browser sign-in runs. Cancel and the back button only call the owner's cancel
/// action; <see cref="CloseAsync"/> closes it once the sign-in has stopped. Modal, so the page beneath
/// (the settings that started the sign-in) cannot be tapped meanwhile.
/// </summary>
public partial class SignInWaitPage : ContentPage, IAbandonablePrompt
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _appeared = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action _cancel;
    private bool _closing;

    public SignInWaitPage(string providerName, Action cancel)
    {
        InitializeComponent();
        TitleLabel.Text = providerName;
        _cancel = cancel;
    }

    /// <summary>Completes once the page is on screen, so a close cannot race the modal push.</summary>
    public Task Appeared => _appeared.Task;

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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _appeared.TrySetResult();
    }

    /// <summary>Back only cancels the sign-in: the owner closes the page once the sign-in has really stopped.</summary>
    protected override bool OnBackButtonPressed()
    {
        _cancel();
        return true;
    }

    private void OnCancelClicked(object? sender, EventArgs e) => _cancel();

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
