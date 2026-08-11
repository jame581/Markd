using Markd.Core.Domain;
using Markd.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace Markd;

[QueryProperty(nameof(OccasionIdQuery), "id")]
public class OccasionDetailPage : ContentPage
{
    private readonly OccasionDetailViewModel _viewModel;
    private string? _occasionIdQuery;

    // Kept as fields so OnAppearing can animate them
    private readonly Label _daysNumberLabel;
    private readonly Label _daysUnitLabel;
    private readonly Border _counterCard;

    public OccasionDetailPage()
    {
        _viewModel = ServiceHelper.GetRequiredService<OccasionDetailViewModel>();
        BindingContext = _viewModel;

        // ── Counter card ────────────────────────────────────────────────
        _daysNumberLabel = new Label
        {
            FontSize = 72,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = Color.FromArgb("#1B6B3A")
        };
        _daysNumberLabel.SetBinding(Label.TextProperty, nameof(OccasionDetailViewModel.Days));

        _daysUnitLabel = new Label
        {
            Text = "days",
            FontSize = 20,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = Color.FromArgb("#2E7D4F")
        };

        var timeBreakdownLabel = new Label
        {
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = Color.FromArgb("#2E7D4F"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        timeBreakdownLabel.SetBinding(Label.TextProperty, nameof(OccasionDetailViewModel.TimeBreakdown));

        var directionBadge = new Label
        {
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = Color.FromArgb("#1B6B3A")
        };
        directionBadge.SetBinding(Label.TextProperty,
            new Binding("CurrentOccasion.Direction", stringFormat: "{0}"));

        _counterCard = new Border
        {
            Padding = new Thickness(24, 20),
            Margin = new Thickness(0, 0, 0, 8),
            BackgroundColor = Color.FromArgb("#F0FFF4"),
            Stroke = Color.FromArgb("#1B6B3A"),
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children = { _daysNumberLabel, _daysUnitLabel, timeBreakdownLabel, directionBadge }
            }
        };

        // ── Header row: emoji + title ────────────────────────────────────
        var emoji = new Label { FontSize = 28 };
        emoji.SetBinding(Label.TextProperty, "CurrentOccasion.Emoji");

        var title = new Label { FontSize = 22, FontAttributes = FontAttributes.Bold };
        title.SetBinding(Label.TextProperty, "CurrentOccasion.Title");

        var headerRow = new HorizontalStackLayout { Spacing = 10, Children = { emoji, title } };

        // ── Meta row: anchor date ────────────────────────────────────────
        var date = new Label { FontSize = 14, TextColor = Colors.Gray };
        date.SetBinding(Label.TextProperty,
            new Binding("CurrentOccasion.AnchorDate", stringFormat: "Anchor: {0:D}"));

        // ── Notes ────────────────────────────────────────────────────────
        var notes = new Label { FontSize = 14 };
        notes.SetBinding(Label.TextProperty, "CurrentOccasion.Notes");

        // ── Milestones ───────────────────────────────────────────────────
        var milestoneHeader = new Label
        {
            Text = "Milestones",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 12, 0, 4)
        };

        var milestoneThresholdEntry = new Entry { Placeholder = "Threshold days", Keyboard = Keyboard.Numeric };
        milestoneThresholdEntry.SetBinding(Entry.TextProperty, nameof(OccasionDetailViewModel.NewMilestoneThresholdDays));

        var milestoneLabelEntry = new Entry { Placeholder = "Milestone label" };
        milestoneLabelEntry.SetBinding(Entry.TextProperty, nameof(OccasionDetailViewModel.NewMilestoneLabel));

        var addMilestoneButton = new Button { Text = "Add Milestone" };
        addMilestoneButton.SetBinding(Button.CommandProperty, nameof(OccasionDetailViewModel.AddMilestoneCommand));

        var milestoneList = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            EmptyView = new Label { Text = "No milestones yet.", TextColor = Colors.Gray }
        };
        milestoneList.SetBinding(ItemsView.ItemsSourceProperty, nameof(OccasionDetailViewModel.Milestones));
        milestoneList.ItemTemplate = new DataTemplate(() =>
        {
            var threshold = new Label { VerticalOptions = LayoutOptions.Center, FontAttributes = FontAttributes.Bold };
            threshold.SetBinding(Label.TextProperty, new Binding(nameof(Milestone.ThresholdDays), stringFormat: "{0} days"));

            var label = new Label { VerticalOptions = LayoutOptions.Center };
            label.SetBinding(Label.TextProperty, nameof(Milestone.Label));

            var textStack = new VerticalStackLayout { Spacing = 2, Children = { threshold, label } };

            var removeButton = new Button
            {
                Text = "Remove",
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#D9534F"),
                Padding = new Thickness(8, 4)
            };
            removeButton.SetBinding(Button.CommandProperty,
                new Binding(nameof(OccasionDetailViewModel.RemoveMilestoneCommand), source: _viewModel));
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
            row.Add(textStack);   Grid.SetColumn(textStack, 0);
            row.Add(removeButton); Grid.SetColumn(removeButton, 1);
            return row;
        });

        // ── Action buttons ───────────────────────────────────────────────
        var error = new Label { TextColor = Colors.Red };
        error.SetBinding(Label.TextProperty, nameof(OccasionDetailViewModel.ErrorMessage));

        var pin = new Button { Text = "Pin" };
        pin.SetBinding(Button.CommandProperty, nameof(OccasionDetailViewModel.PinCommand));

        var edit = new Button { Text = "Edit" };
        edit.SetBinding(Button.CommandProperty, nameof(OccasionDetailViewModel.EditCommand));

        var delete = new Button
        {
            Text = "Delete",
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#D9534F")
        };
        delete.SetBinding(Button.CommandProperty, nameof(OccasionDetailViewModel.DeleteCommand));

        var actionRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        actionRow.Add(pin);    Grid.SetColumn(pin, 0);
        actionRow.Add(edit);   Grid.SetColumn(edit, 1);
        actionRow.Add(delete); Grid.SetColumn(delete, 2);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 8,
                Children =
                {
                    headerRow,
                    date,
                    _counterCard,
                    notes,
                    milestoneHeader,
                    milestoneThresholdEntry,
                    milestoneLabelEntry,
                    addMilestoneButton,
                    milestoneList,
                    error,
                    actionRow
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

        // Entrance animation: counter card pops in
        _counterCard.Opacity = 0;
        _counterCard.Scale = 0.75;
        await Task.WhenAll(
            _counterCard.FadeTo(1, 350, Easing.CubicOut),
            _counterCard.ScaleTo(1, 350, Easing.SpringOut)
        );

        _viewModel.StartTimer();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopTimer();
    }

    private async Task InitializeFromQueryAsync(string? idValue)
    {
        if (int.TryParse(idValue, out var id))
            await _viewModel.LoadAsync(id);
    }
}
