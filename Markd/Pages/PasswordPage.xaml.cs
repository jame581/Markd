using Markd.Core.Localization;

namespace Markd.Pages;

public partial class PasswordPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _result = new();
    private bool _closing;

    public PasswordPage(string fileName, bool previousAttemptFailed)
    {
        InitializeComponent();
        MessageLabel.Text = string.Format(LocalizationManager.Instance.Culture, Strings.Import_PasswordMessage, fileName);
        ErrorLabel.IsVisible = previousAttemptFailed;
    }

    public Task<string?> Result => _result.Task;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PasswordEntry.Focus();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    private async void OnOpenClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(PasswordEntry.Text))
            await CloseAsync(PasswordEntry.Text);
    }

    private async Task CloseAsync(string? password)
    {
        if (_closing)
            return;

        _closing = true;
        PasswordEntry.Text = string.Empty;
        try
        {
            await Navigation.PopModalAsync(false);
        }
        finally
        {
            _result.TrySetResult(password);
        }
    }
}
