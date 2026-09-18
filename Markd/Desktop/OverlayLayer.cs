using Markd.Controls;

namespace Markd.Desktop;

/// <summary>
/// One modal layer: a scrim over the whole window with a card centred on it. Layers stack, so a confirm
/// dialog can open above the occasion form. Escape calls <see cref="Dismiss"/>.
/// </summary>
public sealed class OverlayLayer : Grid
{
    private readonly View _card;
    private readonly Action? _onDismiss;
    private bool _closed;

    public OverlayLayer(View card, Action? onDismiss)
    {
        _card = card;
        _onDismiss = onDismiss;

        var scrim = new BoxView();
        scrim.SetAppThemeColor(BoxView.ColorProperty, (Color)Application.Current!.Resources["DesktopScrim"], (Color)Application.Current.Resources["DesktopScrim"]);
        Children.Add(scrim);

        // The scrim swallows clicks so nothing behind the dialog reacts.
        scrim.GestureRecognizers.Add(new TapGestureRecognizer());

        card.HorizontalOptions = LayoutOptions.Center;
        card.VerticalOptions = LayoutOptions.Center;
        card.Shadow = new Shadow { Brush = new SolidColorBrush(Color.FromRgba(0, 0, 0, ThemeColors.IsDark ? 0.5 : 0.3)), Offset = new Point(0, 18), Radius = 40 };
        Children.Add(card);
    }

    public event EventHandler? Closed;

    public bool IsClosed => _closed;

    public async Task PresentAsync()
    {
        Opacity = 0;
        _card.Scale = 0.96;
        await Task.WhenAll(this.FadeToAsync(1, 140), _card.ScaleToAsync(1, 220, Easing.CubicOut));
    }

    /// <summary>Escape or a scrim-level cancel: runs the owner's cancel action, which is expected to call <see cref="CloseAsync"/>.</summary>
    public void Dismiss()
    {
        if (_onDismiss is null)
            _ = CloseAsync();
        else
            _onDismiss();
    }

    public async Task CloseAsync()
    {
        if (_closed)
            return;

        _closed = true;
        await this.FadeToAsync(0, 110);
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
