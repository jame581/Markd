using System.ComponentModel;
using Markd.Desktop.Dialogs;
using Markd.ViewModels;

namespace Markd.Desktop.Views;

/// <summary>Category rows with hover Edit / Delete; the editor opens as a dialog while the view model's editor is open.</summary>
public partial class CategoriesView : ContentView, IDesktopSection
{
    private readonly CategoryViewModel _viewModel;
    private readonly Func<View, Action, OverlayLayer> _showOverlay;
    private OverlayLayer? _editorLayer;

    public CategoriesView(Func<View, Action, OverlayLayer> showOverlay)
    {
        InitializeComponent();
        _showOverlay = showOverlay;
        _viewModel = ServiceHelper.GetRequiredService<CategoryViewModel>();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BindingContext = _viewModel;
    }

    public event EventHandler? HeaderChanged
    {
        add { }
        remove { }
    }

    public string Title => "Categories";
    public string Subtitle => "Group occasions however you think about them";
    public bool ShowsNewOccasion => false;
    public bool HasHeaderRule => true;
    public string? BackLabel => null;

    public Task ShowAsync() => _viewModel.LoadAsync();

    public void Hide()
    {
    }

    public bool GoBack() => false;

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CategoryViewModel.IsEditorOpen))
            return;

        if (_viewModel.IsEditorOpen && _editorLayer is null)
        {
            var dialog = new CategoryEditorDialog(_viewModel);
            _editorLayer = _showOverlay(dialog, dialog.Cancel);
        }
        else if (!_viewModel.IsEditorOpen && _editorLayer is { } layer)
        {
            _editorLayer = null;
            await layer.CloseAsync();
        }
    }
}
