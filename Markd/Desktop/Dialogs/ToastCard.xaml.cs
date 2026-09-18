namespace Markd.Desktop.Dialogs;

/// <summary>Slides in from the right, dismisses itself after 4.2 s ("Milestone removed", "Occasion deleted", "Saved").</summary>
public partial class ToastCard : ContentView
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4.2);
    private bool _closing;

    public ToastCard(string title, string? detail)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        DetailLabel.Text = detail;
        DetailLabel.IsVisible = !string.IsNullOrWhiteSpace(detail);
    }

    public event EventHandler? Closed;

    public async Task ShowAsync()
    {
        Opacity = 0;
        TranslationX = 24;
        await Task.WhenAll(this.FadeToAsync(1, 280, Easing.CubicOut), this.TranslateToAsync(0, 0, 280, Easing.CubicOut));
        await Task.Delay(Lifetime);
        await CloseAsync();
    }

    public async Task CloseAsync()
    {
        if (_closing)
            return;

        _closing = true;
        await Task.WhenAll(this.FadeToAsync(0, 180), this.TranslateToAsync(24, 0, 180, Easing.CubicIn));
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private async void OnCloseClicked(object? sender, EventArgs e) => await CloseAsync();
}
