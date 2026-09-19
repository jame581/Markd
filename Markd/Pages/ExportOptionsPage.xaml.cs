using Markd.Services;
using Markd.ViewModels;

namespace Markd.Pages;

public partial class ExportOptionsPage : ContentPage
{
    private readonly ExportOptionsForm _form = new();
    private readonly TaskCompletionSource<ExportChoice?> _result = new();
    private bool _closing;

    public ExportOptionsPage()
    {
        InitializeComponent();
        BindingContext = _form;
    }

    public Task<ExportChoice?> Result => _result.Task;

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    private async void OnSaveClicked(object? sender, EventArgs e) => await SubmitAsync(ExportDestination.Save);

    private async void OnShareClicked(object? sender, EventArgs e) => await SubmitAsync(ExportDestination.Share);

    private async Task SubmitAsync(ExportDestination destination)
    {
        if (_form.TryCreate(destination) is { } choice)
            await CloseAsync(choice);
    }

    private async Task CloseAsync(ExportChoice? choice)
    {
        if (_closing)
            return;

        _closing = true;
        _form.Password = string.Empty;
        _form.ConfirmPassword = string.Empty;
        try
        {
            await Navigation.PopModalAsync(false);
        }
        finally
        {
            _result.TrySetResult(choice);
        }
    }
}
