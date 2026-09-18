using Markd.ViewModels;

namespace Markd.Desktop.Dialogs;

public partial class MilestoneEditorDialog : ContentView
{
    private readonly MilestoneEditorViewModel _viewModel;

    public MilestoneEditorDialog(MilestoneEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        Loaded += (_, _) => DaysEntry.Focus();
    }

    public void Cancel() => _viewModel.ResultSource.TrySetResult(null);

    private void OnCancelClicked(object? sender, EventArgs e) => Cancel();

    private void OnAddClicked(object? sender, EventArgs e) => Submit();

    private void OnCompleted(object? sender, EventArgs e) => Submit();

    private void Submit()
    {
        if (_viewModel.TryCreateResult() is { } result)
            _viewModel.ResultSource.TrySetResult(result);
    }
}
