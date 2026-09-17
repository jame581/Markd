using Markd.Pages;
using Markd.ViewModels;

namespace Markd;

[QueryProperty(nameof(OccasionIdQuery), "id")]
public partial class OccasionDetailPage : ContentPage
{
    private readonly OccasionDetailViewModel _viewModel;
    private string? _occasionIdQuery;

    public OccasionDetailPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<OccasionDetailViewModel>();
        BindingContext = _viewModel;
    }

    public string? OccasionIdQuery
    {
        get => _occasionIdQuery;
        set => _occasionIdQuery = value;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (int.TryParse(OccasionIdQuery, out var id))
        {
            await _viewModel.LoadAsync(id);
            Title = _viewModel.CurrentOccasion?.Title ?? "Occasion";
        }

        HeroCard.Opacity = 0;
        HeroCard.Scale = 0.92;
        await Task.WhenAll(
            HeroCard.FadeToAsync(1, 250, Easing.CubicOut),
            HeroCard.ScaleToAsync(1, 250, Easing.CubicOut));

        _viewModel.StartTimer();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopTimer();
    }

    private async void OnAddMilestoneClicked(object? sender, EventArgs e)
    {
        var editorPage = new MilestoneEditorPage();
        await Navigation.PushModalAsync(editorPage);
        var result = await editorPage.WaitForResultAsync();
        if (result is null)
            return;

        _viewModel.NewMilestoneLabel = result.Label;
        _viewModel.NewMilestoneThresholdDays = result.ThresholdDays.ToString();
        await _viewModel.AddMilestoneCommand.ExecuteAsync(null);
    }
}
