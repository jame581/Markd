using Markd.ViewModels;

namespace Markd.Pages;

public partial class MilestoneEditorPage : ContentPage
{
    private readonly MilestoneEditorViewModel _viewModel;
    private bool _closing;

    public MilestoneEditorPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<MilestoneEditorViewModel>();
        BindingContext = _viewModel;
    }

    public Task<MilestoneEditorResult?> WaitForResultAsync() => _viewModel.WaitForResultAsync();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LabelEntry.Focus();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var result = _viewModel.TryCreateResult();
        if (result is not null)
            await CloseAsync(result);
    }

    private async Task CloseAsync(MilestoneEditorResult? result)
    {
        // Claimed before the await, so a second tap during the pop cannot pop the page underneath.
        if (_closing)
            return;

        _closing = true;
        await Navigation.PopModalAsync(false);
        _viewModel.ResultSource.TrySetResult(result);
    }
}
