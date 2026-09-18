using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Markd.Pages;
using Markd.Services;
using Markd.ViewModels;

namespace Markd;

public partial class CategoryPage : ContentPage
{
    private const string RenameItem = "Rename";
    private const string ColourItem = "Change colour";
    private const string DeleteItem = "Delete";

    private readonly CategoryViewModel _viewModel;
    private CategoryEditorPage? _editor;

    public CategoryPage()
    {
        EditRowCommand = new RelayCommand<CategoryRow?>(row => { if (row is not null) _viewModel!.BeginEdit(row); });
        RowMenuCommand = new AsyncRelayCommand<View?>(anchor => anchor is { BindingContext: CategoryRow row } ? ShowRowMenuAsync(anchor, row) : Task.CompletedTask);
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<CategoryViewModel>();
        BindingContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SystemBars.Apply(withNavigationBar: true);
        SnackbarFeedbackService.Anchor = NavBar;
        await _viewModel.LoadAsync();
    }

    public ICommand EditRowCommand { get; }
    public ICommand RowMenuCommand { get; }

    private async void OnOverflowClicked(object? sender, EventArgs e)
    {
        if (sender is View { BindingContext: CategoryRow row } anchor)
            await ShowRowMenuAsync(anchor, row);
    }

    private async Task ShowRowMenuAsync(View anchor, CategoryRow row)
    {
        var menu = ServiceHelper.GetRequiredService<IActionMenuService>();
        switch (await menu.ShowAsync(anchor, [RenameItem, ColourItem, DeleteItem], DeleteItem))
        {
            case RenameItem:
            case ColourItem:
                _viewModel.BeginEdit(row);
                break;
            case DeleteItem:
                await _viewModel.DeleteCommand.ExecuteAsync(row);
                break;
        }
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CategoryViewModel.IsEditorOpen))
            return;

        if (_viewModel.IsEditorOpen && _editor is null)
        {
            _editor = new CategoryEditorPage(_viewModel);
            await Navigation.PushModalAsync(_editor, false);
        }
        else if (!_viewModel.IsEditorOpen && _editor is not null)
        {
            var editor = _editor;
            _editor = null;
            if (Navigation.ModalStack.Contains(editor))
                await Navigation.PopModalAsync(false);
        }
    }
}
