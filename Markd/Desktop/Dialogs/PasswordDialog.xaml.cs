using Markd.Core.Localization;

namespace Markd.Desktop.Dialogs;

public partial class PasswordDialog : ContentView
{
    private readonly TaskCompletionSource<string?> _result = new();

    public PasswordDialog(string fileName, bool previousAttemptFailed)
    {
        InitializeComponent();
        MessageLabel.Text = string.Format(LocalizationManager.Instance.Culture, Strings.Import_PasswordMessage, fileName);
        ErrorLabel.IsVisible = previousAttemptFailed;
        Loaded += (_, _) => PasswordEntry.Focus();
    }

    public Task<string?> Result => _result.Task;

    public void Cancel() => Complete(null);

    private void OnCancelClicked(object? sender, EventArgs e) => Cancel();

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(PasswordEntry.Text))
            Complete(PasswordEntry.Text);
    }

    /// <summary>Clears the entry either way, matching the phone's PasswordPage.</summary>
    private void Complete(string? password)
    {
        PasswordEntry.Text = string.Empty;
        _result.TrySetResult(password);
    }
}
