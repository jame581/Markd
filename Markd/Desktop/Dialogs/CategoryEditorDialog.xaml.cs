using Markd.ViewModels;

namespace Markd.Desktop.Dialogs;

/// <summary>New / edit category over the Categories view; open while <see cref="CategoryViewModel.IsEditorOpen"/> is true.</summary>
public partial class CategoryEditorDialog : ContentView
{
    private readonly CategoryViewModel _viewModel;

    public CategoryEditorDialog(CategoryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        Loaded += (_, _) => NameEntry.Focus();
    }

    public void Cancel() => _viewModel.CancelEditorCommand.Execute(null);

    private async void OnCompleted(object? sender, EventArgs e) => await _viewModel.SaveEditorCommand.ExecuteAsync(null);
}
