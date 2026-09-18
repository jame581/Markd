using Markd.ViewModels;

namespace Markd.Desktop.Dialogs;

public partial class OccasionFormDialog : ContentView
{
    private readonly OccasionFormViewModel _viewModel;

    public OccasionFormDialog(OccasionFormViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        Loaded += (_, _) => TitleEntry.Focus();
    }

    /// <summary>Keeps the dialog inside the window; the field list scrolls when the window is short.</summary>
    public void FitTo(double windowHeight) => Card.MaximumHeightRequest = Math.Max(360, windowHeight - 72);

    public void Cancel() => _viewModel.CancelCommand.Execute(null);

    private async void OnTitleCompleted(object? sender, EventArgs e) => await _viewModel.SaveCommand.ExecuteAsync(null);

    private void OnEmojiButtonClicked(object? sender, EventArgs e) => EmojiPanel.IsVisible = !EmojiPanel.IsVisible;

    private void OnEmojiPicked(object? sender, EventArgs e) => EmojiPanel.IsVisible = false;
}
