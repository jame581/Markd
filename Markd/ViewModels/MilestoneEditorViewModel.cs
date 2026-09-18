using CommunityToolkit.Mvvm.ComponentModel;
using Markd.Core.Domain;

namespace Markd.ViewModels;

public partial class MilestoneEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _thresholdDays = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public TaskCompletionSource<MilestoneEditorResult?> ResultSource { get; } = new();

    public Task<MilestoneEditorResult?> WaitForResultAsync() => ResultSource.Task;

    public MilestoneEditorResult? TryCreateResult()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Label))
        {
            ErrorMessage = "Milestone label is required.";
            return null;
        }

        if (!int.TryParse(ThresholdDays, out var thresholdDays) || thresholdDays <= 0)
        {
            ErrorMessage = "Milestone threshold must be a positive number.";
            return null;
        }

        return new MilestoneEditorResult(Label.Trim(), thresholdDays);
    }
}

public sealed record MilestoneEditorResult(string Label, int ThresholdDays);
