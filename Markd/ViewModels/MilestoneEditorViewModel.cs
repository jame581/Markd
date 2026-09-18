using CommunityToolkit.Mvvm.ComponentModel;
using Markd.Core.Domain;
using Markd.Core.Localization;

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
            ErrorMessage = Strings.Milestone_LabelRequired;
            return null;
        }

        if (!int.TryParse(ThresholdDays, out var thresholdDays) || thresholdDays <= 0)
        {
            ErrorMessage = Strings.Milestone_ThresholdInvalid;
            return null;
        }

        return new MilestoneEditorResult(Label.Trim(), thresholdDays);
    }
}

public sealed record MilestoneEditorResult(string Label, int ThresholdDays);
