namespace Markd.Desktop.Dialogs;

/// <summary>Confirmation or message dialog. A destructive accept ("Delete", "Erase all", "Remove") is drawn in Danger.</summary>
public partial class ConfirmDialog : ContentView
{
    private readonly TaskCompletionSource<bool> _result = new();

    public ConfirmDialog(string title, string message, string accept, string? cancel)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        AcceptButton.Text = accept;
        CancelButton.Text = cancel;
        CancelButton.IsVisible = cancel is not null;

        if (IsDestructive(accept))
            AcceptButton.Style = (Style)Application.Current!.Resources["DeskDangerFilledButton"];
    }

    public Task<bool> Result => _result.Task;

    public void Cancel() => _result.TrySetResult(false);

    private void OnCancelClicked(object? sender, EventArgs e) => Cancel();

    private void OnAcceptClicked(object? sender, EventArgs e) => _result.TrySetResult(true);

    private static bool IsDestructive(string accept) =>
        accept.StartsWith("Delete", StringComparison.OrdinalIgnoreCase)
        || accept.StartsWith("Erase", StringComparison.OrdinalIgnoreCase)
        || accept.StartsWith("Remove", StringComparison.OrdinalIgnoreCase);
}
