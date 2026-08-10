using Markd.ViewModels;

namespace Markd;

[QueryProperty(nameof(OccasionIdQuery), "id")]
public class OccasionDetailPage : ContentPage
{
    private readonly OccasionDetailViewModel _viewModel;
    private string? _occasionIdQuery;

    public OccasionDetailPage()
    {
        _viewModel = ServiceHelper.GetRequiredService<OccasionDetailViewModel>();
        BindingContext = _viewModel;

        var title = new Label { FontSize = 24, FontAttributes = FontAttributes.Bold };
        title.SetBinding(Label.TextProperty, "CurrentOccasion.Title");

        var emoji = new Label { FontSize = 20 };
        emoji.SetBinding(Label.TextProperty, "CurrentOccasion.Emoji");

        var date = new Label();
        date.SetBinding(Label.TextProperty, new Binding("CurrentOccasion.AnchorDate", stringFormat: "Date: {0:D}"));

        var direction = new Label();
        direction.SetBinding(Label.TextProperty, new Binding("CurrentOccasion.Direction", stringFormat: "Direction: {0}"));

        var days = new Label { FontSize = 20 };
        days.SetBinding(Label.TextProperty, new Binding(nameof(OccasionDetailViewModel.Days), stringFormat: "Days: {0}"));

        var notes = new Label();
        notes.SetBinding(Label.TextProperty, "CurrentOccasion.Notes");

        var error = new Label { TextColor = Colors.Red };
        error.SetBinding(Label.TextProperty, nameof(OccasionDetailViewModel.ErrorMessage));

        var pin = new Button { Text = "Pin" };
        pin.SetBinding(Button.CommandProperty, nameof(OccasionDetailViewModel.PinCommand));

        var edit = new Button { Text = "Edit" };
        edit.SetBinding(Button.CommandProperty, nameof(OccasionDetailViewModel.EditCommand));

        var delete = new Button { Text = "Delete", TextColor = Colors.White, BackgroundColor = Color.FromArgb("#D9534F") };
        delete.SetBinding(Button.CommandProperty, nameof(OccasionDetailViewModel.DeleteCommand));

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 10,
                Children = { title, emoji, date, direction, days, notes, error, pin, edit, delete }
            }
        };
    }

    public string? OccasionIdQuery
    {
        get => _occasionIdQuery;
        set
        {
            _occasionIdQuery = value;
            _ = InitializeFromQueryAsync(value);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (int.TryParse(OccasionIdQuery, out var id))
            await _viewModel.LoadAsync(id);
    }

    private async Task InitializeFromQueryAsync(string? idValue)
    {
        if (int.TryParse(idValue, out var id))
            await _viewModel.LoadAsync(id);
    }
}
