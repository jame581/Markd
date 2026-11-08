using Markd.Core.Sync;
using Markd.Services.Sync;
using Markd.ViewModels;

namespace Markd.Pages;

/// <summary>
/// The conflict list (phone, full modal page). Completes <see cref="Result"/> after the page is closed: the answers
/// after Apply, null after Later, the close button, back, or the page going away without an answer.
/// </summary>
public partial class ConflictListPage : ContentPage, IAbandonablePrompt
{
    private readonly ConflictListViewModel _viewModel;
    private readonly TaskCompletionSource<IReadOnlyList<ConflictResolution>?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closing;
    private bool _comparing;

    public ConflictListPage(ConflictListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        foreach (var row in viewModel.Rows)
            row.CompareRequested += OnCompareRequested;

        _ = CloseWhenDecidedAsync();
    }

    public Task<IReadOnlyList<ConflictResolution>?> Result => _result.Task;

    public void Abandon()
    {
        // A close in progress completes the result itself, with the answers it closed with.
        if (_closing)
            return;

        _closing = true;
        _viewModel.LaterCommand.Execute(null);
        _result.TrySetResult(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_closing)
            PhoneModalPrompt.AbandonIfGoneLater(this);
    }

    protected override bool OnBackButtonPressed()
    {
        _viewModel.LaterCommand.Execute(null);
        return true;
    }

    private async Task CloseWhenDecidedAsync()
    {
        var answers = await _viewModel.Result;
        if (_closing)
        {
            _result.TrySetResult(answers);
            return;
        }

        _closing = true;
        try
        {
            if (PhoneModalPrompt.IsShown(this))
                await Navigation.PopModalAsync(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Closing the conflict list failed: {ex}");
        }
        finally
        {
            _result.TrySetResult(answers);
        }
    }

    private async void OnCompareRequested(object? sender, ConflictCompareViewModel compare)
    {
        // One compare page at a time, and none once the list is closing.
        if (_comparing || _closing)
            return;

        _comparing = true;
        try
        {
            await Navigation.PushModalAsync(new ConflictComparePage(compare), false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Showing the compare page failed: {ex}");
        }
        finally
        {
            _comparing = false;
        }
    }
}
