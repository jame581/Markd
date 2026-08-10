using Markd.Core.Domain;
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

        var milestoneHeader = new Label { Text = "Milestones", FontSize = 18, FontAttributes = FontAttributes.Bold };

        var milestoneThresholdEntry = new Entry { Placeholder = "Threshold days", Keyboard = Keyboard.Numeric };
        milestoneThresholdEntry.SetBinding(Entry.TextProperty, nameof(OccasionDetailViewModel.NewMilestoneThresholdDays));

        var milestoneLabelEntry = new Entry { Placeholder = "Milestone label" };
        milestoneLabelEntry.SetBinding(Entry.TextProperty, nameof(OccasionDetailViewModel.NewMilestoneLabel));

        var addMilestoneButton = new Button { Text = "Add Milestone" };
        addMilestoneButton.SetBinding(Button.CommandProperty, nameof(OccasionDetailViewModel.AddMilestoneCommand));

        var milestoneList = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            EmptyView = new Label { Text = "No milestones yet." }
        };
        milestoneList.SetBinding(ItemsView.ItemsSourceProperty, nameof(OccasionDetailViewModel.Milestones));
        milestoneList.ItemTemplate = new DataTemplate(() =>
        {
            var threshold = new Label { VerticalOptions = LayoutOptions.Center, FontAttributes = FontAttributes.Bold };
            threshold.SetBinding(Label.TextProperty, new Binding(nameof(Milestone.ThresholdDays), stringFormat: "{0} days"));

            var label = new Label { VerticalOptions = LayoutOptions.Center };
            label.SetBinding(Label.TextProperty, nameof(Milestone.Label));

            var textStack = new VerticalStackLayout
            {
                Spacing = 2,
                Children = { threshold, label }
            };

            var removeButton = new Button
            {
                Text = "Remove",
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#D9534F"),
                Padding = new Thickness(8, 4)
            };
            removeButton.SetBinding(Button.CommandProperty, new Binding(nameof(OccasionDetailViewModel.RemoveMilestoneCommand), source: _viewModel));
            removeButton.SetBinding(Button.CommandParameterProperty, new Binding("."));

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                Padding = new Thickness(0, 4)
            };

            row.Add(textStack);
            Grid.SetColumn(textStack, 0);

            row.Add(removeButton);
            Grid.SetColumn(removeButton, 1);

            return row;
        });

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
                Children =
                {
                    title,
                    emoji,
                    date,
                    direction,
                    days,
                    notes,
                    milestoneHeader,
                    milestoneThresholdEntry,
                    milestoneLabelEntry,
                    addMilestoneButton,
                    milestoneList,
                    error,
                    pin,
                    edit,
                    delete
                }
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
