namespace Markd.Desktop.Dialogs;

/// <summary>
/// The desktop waiting panel (overlay) shown while a browser sign-in runs. Cancel, Escape and the window closing call
/// the owner's cancel action; the owner closes the overlay when the sign-in ends.
/// </summary>
public partial class SignInWaitDialog : ContentView
{
    private readonly Action _cancel;

    public SignInWaitDialog(string providerName, Action cancel)
    {
        InitializeComponent();
        TitleLabel.Text = providerName;
        _cancel = cancel;
    }

    public void Cancel() => _cancel();

    private void OnCancelClicked(object? sender, EventArgs e) => Cancel();
}
