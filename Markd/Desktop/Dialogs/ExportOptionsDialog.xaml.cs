using Markd.Services;
using Markd.ViewModels;

namespace Markd.Desktop.Dialogs;

public partial class ExportOptionsDialog : ContentView
{
    private readonly ExportOptionsForm _form = new();
    private readonly TaskCompletionSource<ExportChoice?> _result = new();

    public ExportOptionsDialog()
    {
        InitializeComponent();
        BindingContext = _form;
        Loaded += (_, _) => PasswordEntry.Focus();
    }

    public Task<ExportChoice?> Result => _result.Task;

    public void Cancel() => Complete(null);

    private void OnCancelClicked(object? sender, EventArgs e) => Cancel();

    private void OnSaveClicked(object? sender, EventArgs e) => Submit(ExportDestination.Save);

    private void OnShareClicked(object? sender, EventArgs e) => Submit(ExportDestination.Share);

    private void Submit(ExportDestination destination)
    {
        if (_form.TryCreate(destination) is { } choice)
            Complete(choice);
    }

    /// <summary>The password never leaves this dialog once a result exists; the form fields are cleared either way.</summary>
    private void Complete(ExportChoice? choice)
    {
        _form.Password = string.Empty;
        _form.ConfirmPassword = string.Empty;
        _result.TrySetResult(choice);
    }
}
