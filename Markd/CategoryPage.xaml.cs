using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Markd.Core.Localization;
using Markd.Pages;
using Markd.Services;
using Markd.ViewModels;

namespace Markd;

public partial class CategoryPage : ContentPage
{
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
        string[] items = [Strings.Category_Rename, Strings.Category_ChangeColour, Strings.Common_Delete];
        var chosen = await menu.ShowAsync(anchor, items, items[2]);
        if (chosen == items[0] || chosen == items[1])
            _viewModel.BeginEdit(row);
        else if (chosen == items[2])
            await _viewModel.DeleteCommand.ExecuteAsync(row);
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
