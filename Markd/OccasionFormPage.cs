using Markd.Core.Domain;
using Markd.ViewModels;

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

        var colorEntry = new Entry { Placeholder = "Color Hex (e.g. #FF0066)" };
        colorEntry.SetBinding(Entry.TextProperty, nameof(OccasionFormViewModel.ColorHex));

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
                    colorEntry,
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
