using Markd.Services;

namespace Markd.Desktop.Dialogs;

/// <summary>The desktop milestone moment: three rings expand from behind the emoji every 2.4 s, staggered by 0.8 s.</summary>
public partial class MilestoneMomentCard : ContentView
{
    private readonly MilestoneMoment _moment;
    private readonly TaskCompletionSource _closed = new();
    private bool _animating;

    public MilestoneMomentCard(MilestoneMoment moment)
    {
        InitializeComponent();
        _moment = moment;
        BindingContext = moment;
        Loaded += (_, _) => StartRings();
    }

    public Task Closed => _closed.Task;

    public void Close()
    {
        _animating = false;
        _closed.TrySetResult();
    }

    private void StartRings()
    {
        if (_animating)
            return;

        _animating = true;
        _ = PulseAsync(RingA, 0);
        _ = PulseAsync(RingB, 800);
        _ = PulseAsync(RingC, 1600);
    }

    private async Task PulseAsync(VisualElement ring, int delay)
    {
        ring.Opacity = 0;
        await Task.Delay(delay);
        while (_animating)
        {
            ring.Scale = 0.4;
            ring.Opacity = 0.9;
            await Task.WhenAll(ring.ScaleToAsync(1.9, 2400, Easing.CubicOut), ring.FadeToAsync(0, 2400, Easing.CubicOut));
        }
    }

    private void OnNotNowClicked(object? sender, EventArgs e) => Close();

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        Close();
        await ServiceHelper.GetRequiredService<IShareService>().ShareMilestoneAsync(new MilestoneShareRequest(
            _moment.Occasion, _moment.Milestone.Label, _moment.Milestone.ThresholdDays, _moment.Next?.Label));
    }
}
