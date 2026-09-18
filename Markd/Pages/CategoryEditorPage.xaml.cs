using Markd.ViewModels;

namespace Markd.Pages;

/// <summary>Material 3 dialog for adding or editing a category's name, emoji and colour.</summary>
public partial class CategoryEditorPage : ContentPage
{
    private readonly CategoryViewModel _viewModel;

    public CategoryEditorPage(CategoryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override bool OnBackButtonPressed()
    {
        _viewModel.CancelEditorCommand.Execute(null);
        return true;
    }

    private async void OnEmojiTapped(object? sender, TappedEventArgs e)
    {
        var choice = await EmojiPickerPage.PickAsync(Navigation, CategoryViewModel.EmojiChoices, _viewModel.EditorEmoji);
        if (choice is not null)
            _viewModel.EditorEmoji = choice;
    }
}
