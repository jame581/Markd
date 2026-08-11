using Markd.Converters;
using Markd.Core.Domain;
using Markd.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace Markd;

[QueryProperty(nameof(OccasionIdQuery), "id")]
public class OccasionFormPage : ContentPage
{
    private readonly OccasionFormViewModel _viewModel;
    private string? _occasionIdQuery;

    public OccasionFormPage()
    {
        _viewModel = ServiceHelper.GetRequiredService<OccasionFormViewModel>();
        BindingContext = _viewModel;

        var titleEntry = new Entry { Placeholder = "Title" };
        titleEntry.SetBinding(Entry.TextProperty, nameof(OccasionFormViewModel.Title));

        var emojiEntry = new Entry { Placeholder = "Emoji" };
        emojiEntry.SetBinding(Entry.TextProperty, nameof(OccasionFormViewModel.Emoji));

        // ── Color swatch picker ──────────────────────────────────────────
        var colorLabel = new Label { Text = "Color", FontAttributes = FontAttributes.Bold };

        var colorPreview = new BoxView
        {
            WidthRequest = 28,
            HeightRequest = 28,
            CornerRadius = 14,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalOptions = LayoutOptions.Center
        };
        colorPreview.SetBinding(BoxView.ColorProperty,
            new Binding(nameof(OccasionFormViewModel.ColorHex),
                converter: new ColorHexConverter(), converterParameter: Colors.Gray));

        var colorPreviewLabel = new Label { VerticalOptions = LayoutOptions.Center };
        colorPreviewLabel.SetBinding(Label.TextProperty, nameof(OccasionFormViewModel.ColorHex));

        var colorPreviewRow = new HorizontalStackLayout
        {
            Spacing = 4,
            Children = { colorPreview, colorPreviewLabel }
        };

        // Build two rows of 6 swatches each
        var swatchRows = new VerticalStackLayout { Spacing = 6 };
        var swatches = OccasionFormViewModel.ColorSwatches;
        for (var row = 0; row < 2; row++)
        {
            var rowLayout = new HorizontalStackLayout { Spacing = 8 };
            for (var col = 0; col < 6; col++)
            {
                var hex = swatches[row * 6 + col];
                var swatch = new Border
                {
                    WidthRequest = 36,
                    HeightRequest = 36,
                    StrokeShape = new RoundRectangle { CornerRadius = 18 },
                    StrokeThickness = 0,
                    BackgroundColor = Color.FromArgb(hex)
                };

                var selectedIndicator = new Label
                {
                    Text = "✓",
                    TextColor = Colors.White,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                };

                swatch.Content = selectedIndicator;

                // Bind checkmark visibility to whether this hex is selected
                selectedIndicator.SetBinding(IsVisibleProperty,
                    new Binding(nameof(OccasionFormViewModel.ColorHex),
                        converter: new StringEqualsConverter(),
                        converterParameter: hex));

                var tapGesture = new TapGestureRecognizer
                {
                    CommandParameter = hex
                };
                tapGesture.SetBinding(TapGestureRecognizer.CommandProperty,
                    nameof(OccasionFormViewModel.SelectColorCommand));
                swatch.GestureRecognizers.Add(tapGesture);

                rowLayout.Children.Add(swatch);
            }
            swatchRows.Children.Add(rowLayout);
        }

        var colorSection = new VerticalStackLayout
        {
            Spacing = 6,
            Children = { colorLabel, colorPreviewRow, swatchRows }
        };

        var datePicker = new DatePicker();
        datePicker.SetBinding(DatePicker.DateProperty, nameof(OccasionFormViewModel.AnchorDate));

        var directionPicker = new Picker { Title = "Direction", ItemsSource = Enum.GetValues(typeof(OccasionDirection)).Cast<OccasionDirection>().ToList() };
        directionPicker.SetBinding(Picker.SelectedItemProperty, nameof(OccasionFormViewModel.Direction));

        var categoryPicker = new Picker { Title = "Category", ItemDisplayBinding = new Binding(nameof(Category.Name)) };
        categoryPicker.SetBinding(Picker.ItemsSourceProperty, nameof(OccasionFormViewModel.Categories));
        categoryPicker.SetBinding(Picker.SelectedItemProperty, nameof(OccasionFormViewModel.SelectedCategory));

        var notesEditor = new Editor { Placeholder = "Notes", HeightRequest = 120, AutoSize = EditorAutoSizeOption.TextChanges };
        notesEditor.SetBinding(Editor.TextProperty, nameof(OccasionFormViewModel.Notes));

        var pinSwitch = new Switch();
        pinSwitch.SetBinding(Switch.IsToggledProperty, nameof(OccasionFormViewModel.IsPinned));

        var pinLayout = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { new Label { Text = "Pin this occasion", VerticalOptions = LayoutOptions.Center }, pinSwitch }
        };

        var errorLabel = new Label { TextColor = Colors.Red };
        errorLabel.SetBinding(Label.TextProperty, nameof(OccasionFormViewModel.ErrorMessage));

        var saveButton = new Button { Text = "Save" };
        saveButton.SetBinding(Button.CommandProperty, nameof(OccasionFormViewModel.SaveCommand));

        var header = new Label { FontSize = 22, FontAttributes = FontAttributes.Bold };
        header.SetBinding(Label.TextProperty, nameof(OccasionFormViewModel.Header));

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 12,
                Children =
                {
                    header,
                    titleEntry,
                    emojiEntry,
                    colorSection,
                    datePicker,
                    directionPicker,
                    categoryPicker,
                    notesEditor,
                    pinLayout,
                    errorLabel,
                    saveButton
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

        if (string.IsNullOrWhiteSpace(OccasionIdQuery))
            await _viewModel.InitializeAsync(0);
    }

    private async Task InitializeFromQueryAsync(string? idValue)
    {
        if (int.TryParse(idValue, out var id))
            await _viewModel.InitializeAsync(id);
    }
}
