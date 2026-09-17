namespace Markd.Pages;

public partial class MilestoneEditorPage : ContentPage
{
    private readonly TaskCompletionSource<MilestoneEditorResult?> _resultSource = new();

    public MilestoneEditorPage()
    {
        InitializeComponent();
    }

    public Task<MilestoneEditorResult?> WaitForResultAsync() => _resultSource.Task;

    protected override bool OnBackButtonPressed()
    {
        _resultSource.TrySetResult(null);
        return base.OnBackButtonPressed();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        _resultSource.TrySetResult(null);
        await Navigation.PopModalAsync();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;

        if (string.IsNullOrWhiteSpace(LabelEntry.Text))
        {
            ErrorLabel.Text = "Milestone label is required.";
            ErrorLabel.IsVisible = true;
            return;
        }

        if (!int.TryParse(ThresholdEntry.Text, out var thresholdDays) || thresholdDays <= 0)
        {
            ErrorLabel.Text = "Milestone threshold must be a positive number.";
            ErrorLabel.IsVisible = true;
            return;
        }

        _resultSource.TrySetResult(new MilestoneEditorResult(LabelEntry.Text.Trim(), thresholdDays));
        await Navigation.PopModalAsync();
    }
}

public sealed record MilestoneEditorResult(string Label, int ThresholdDays);
