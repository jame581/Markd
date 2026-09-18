using Microsoft.Maui.Controls.Shapes;

namespace Markd.Controls.Mobile;

/// <summary>Material 3 switch: 52×32 track; on = primary track and a 24dp thumb bearing a check, off = outlined track and a 16dp thumb.</summary>
public sealed class M3Switch : Grid
{
    public static readonly BindableProperty IsToggledProperty =
        BindableProperty.Create(nameof(IsToggled), typeof(bool), typeof(M3Switch), false, BindingMode.TwoWay, propertyChanged: (b, _, _) => ((M3Switch)b).Apply(true));

    private readonly Border _track = new() { StrokeShape = new RoundRectangle { CornerRadius = 16 } };
    private readonly Border _thumb = new() { StrokeThickness = 0, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center };
    private readonly SvgIcon _check = new() { Glyph = "Tick", Size = 14, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };

    public M3Switch()
    {
        WidthRequest = 52;
        HeightRequest = 32;
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.Center;
        _thumb.Content = _check;
        Children.Add(_track);
        Children.Add(_thumb);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => IsToggled = !IsToggled;
        GestureRecognizers.Add(tap);
        Loaded += (_, _) => Application.Current!.RequestedThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => Application.Current!.RequestedThemeChanged -= OnThemeChanged;
        Apply(false);
    }

    public bool IsToggled { get => (bool)GetValue(IsToggledProperty); set => SetValue(IsToggledProperty, value); }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Apply(false);

    private void Apply(bool animate)
    {
        var dark = ThemeColors.IsDark;
        var on = IsToggled;
        var size = on ? 24 : 16;

        _track.BackgroundColor = on ? ThemeColors.Get("AccentFill", dark) : ThemeColors.Get("SurfaceContainerHigh", dark);
        _track.Stroke = on ? Colors.Transparent : ThemeColors.Get("TextSecondary", dark);
        _track.StrokeThickness = on ? 0 : 2;
        _thumb.WidthRequest = _thumb.HeightRequest = size;
        _thumb.StrokeShape = new RoundRectangle { CornerRadius = size / 2d };
        _thumb.BackgroundColor = on ? ThemeColors.Get("OnPrimary", dark) : ThemeColors.Get("TextSecondary", dark);
        _check.IsVisible = on;
        _check.Color = ThemeColors.Get("AccentFill", dark);

        var x = on ? 52 - 4 - 24 : 8;
        if (animate)
            _ = _thumb.TranslateToAsync(x, 0, 150, Easing.CubicOut);
        else
            _thumb.TranslationX = x;
    }
}
