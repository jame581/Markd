using Markd.ViewModels;

namespace Markd.Desktop.Dialogs;

/// <summary>
/// The conflict list as an overlay dialog. The answer is the view model's <see cref="ConflictListViewModel.Result"/>:
/// Apply completes it with the choices, Later, Escape and <see cref="Cancel"/> with null. Compare asks the host to
/// show the two versions on top of the list.
/// </summary>
public partial class ConflictListDialog : ContentView
{
    private readonly ConflictListViewModel _viewModel;
    private readonly Func<ConflictCompareViewModel, Task> _showCompare;
    private bool _comparing;

    public ConflictListDialog(ConflictListViewModel viewModel, Func<ConflictCompareViewModel, Task> showCompare)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _showCompare = showCompare;
        BindingContext = viewModel;
        foreach (var row in viewModel.Rows)
            row.CompareRequested += OnCompareRequested;
    }

    /// <summary>Keeps the dialog inside the window; the rows scroll.</summary>
    public void FitTo(double windowHeight) => Card.MaximumHeightRequest = Math.Max(360, windowHeight - 72);

    /// <summary>"Later": the coordinator keeps the conflicts and sync stays paused.</summary>
    public void Cancel() => _viewModel.LaterCommand.Execute(null);

    private async void OnCompareRequested(object? sender, ConflictCompareViewModel compare)
    {
        // One compare view at a time, and none once the list has its answer.
        if (_comparing || _viewModel.Result.IsCompleted)
            return;

        _comparing = true;
        try
        {
            await _showCompare(compare);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Showing the compare view failed: {ex}");
        }
        finally
        {
            _comparing = false;
        }
    }
}
