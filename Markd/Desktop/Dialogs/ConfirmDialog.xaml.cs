namespace Markd.Desktop.Dialogs;

/// <summary>Confirmation or message dialog. A destructive confirmation is drawn in Danger.</summary>
public partial class ConfirmDialog : ContentView
{
    private readonly TaskCompletionSource<bool> _result = new();

    public ConfirmDialog(string title, string message, string accept, string? cancel, bool destructive)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        AcceptButton.Text = accept;
        CancelButton.Text = cancel;
        CancelButton.IsVisible = cancel is not null;

        if (destructive)
            AcceptButton.Style = (Style)Application.Current!.Resources["DeskDangerFilledButton"];
    }

    public Task<bool> Result => _result.Task;

    public void Cancel() => _result.TrySetResult(false);

    private void OnCancelClicked(object? sender, EventArgs e) => Cancel();

    private void OnAcceptClicked(object? sender, EventArgs e) => _result.TrySetResult(true);
}
