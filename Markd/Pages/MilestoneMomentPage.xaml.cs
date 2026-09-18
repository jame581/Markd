using Markd.Services;

namespace Markd.Pages;

/// <summary>Material basic dialog for a reached milestone: rings expanding behind the emoji, right-aligned actions.</summary>
public partial class MilestoneMomentPage : ContentPage
{
    private readonly MilestoneMoment _moment;
    private readonly TaskCompletionSource _closed = new();
    private bool _animating;
    private bool _closing;

    public MilestoneMomentPage(MilestoneMoment moment)
    {
        InitializeComponent();
        _moment = moment;
        BindingContext = moment;
    }

    public Task Closed => _closed.Task;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Card.Scale = 0.72;
        Card.Opacity = 0;
        await Task.WhenAll(Card.ScaleToAsync(1, 320, Easing.CubicOut), Card.FadeToAsync(1, 200));

        _animating = true;
        _ = PulseAsync(RingA, 0);
        _ = PulseAsync(RingB, 1100);
    }

    protected override void OnDisappearing()
    {
        _animating = false;
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync();
        return true;
    }

    private async Task PulseAsync(VisualElement ring, int delay)
    {
        ring.Opacity = 0;
        await Task.Delay(delay);
        while (_animating)
        {
            ring.Scale = 0.45;
            ring.Opacity = 0.85;
            await Task.WhenAll(ring.ScaleToAsync(1.6, 2200, Easing.CubicOut), ring.FadeToAsync(0, 2200, Easing.CubicOut));
        }
    }

    private async void OnNotNowClicked(object? sender, EventArgs e) => await CloseAsync();

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        await CloseAsync();
        await ServiceHelper.GetRequiredService<IShareService>().ShareMilestoneAsync(new MilestoneShareRequest(
            _moment.Occasion, _moment.Milestone.Label, _moment.Milestone.ThresholdDays, _moment.Next?.Label));
    }

    private async Task CloseAsync()
    {
        // Claimed before the await, so a second tap during the pop cannot pop the page underneath.
        if (_closing)
            return;

        _closing = true;
        _animating = false;
        await Navigation.PopModalAsync(false);
        _closed.TrySetResult();
    }
}
