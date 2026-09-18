using Markd.Pages;
using Markd.Services;
using Markd.ViewModels;

namespace Markd;

[QueryProperty(nameof(OccasionIdQuery), "id")]
public partial class OccasionFormPage : ContentPage
{
    private readonly OccasionFormViewModel _viewModel;
    private bool _initialized;

    public OccasionFormPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<OccasionFormViewModel>();
        BindingContext = _viewModel;
    }

    public string? OccasionIdQuery { get; set; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SystemBars.Apply(withNavigationBar: false, this);
        if (_initialized)
            return;

        _initialized = true;
        await _viewModel.InitializeAsync(int.TryParse(OccasionIdQuery, out var id) ? id : 0);
    }

    private async void OnEmojiTapped(object? sender, TappedEventArgs e)
    {
        var choice = await EmojiPickerPage.PickAsync(Navigation, OccasionFormViewModel.EmojiChoices, _viewModel.Emoji);
        if (choice is not null)
            _viewModel.Emoji = choice;
    }
}
