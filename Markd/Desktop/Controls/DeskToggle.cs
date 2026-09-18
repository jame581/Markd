using Markd.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Markd.Desktop.Controls;

/// <summary>
/// Fluent toggle switch drawn to the prototype: 44×24 track, 18px knob. On = accent track with a white knob,
/// off = pressed-surface track with a tertiary knob. The stock WinUI ToggleSwitch adds On/Off captions, so it is not used.
/// </summary>
public sealed class DeskToggle : HoverSurface
{
    public static readonly BindableProperty IsToggledProperty =
        BindableProperty.Create(nameof(IsToggled), typeof(bool), typeof(DeskToggle), false, BindingMode.TwoWay,
            propertyChanged: (b, _, _) => ((DeskToggle)b).Update(animate: true));

    private readonly Border _knob = new()
    {
        WidthRequest = 18,
        HeightRequest = 18,
        StrokeThickness = 0,
        StrokeShape = new Ellipse(),
        HorizontalOptions = LayoutOptions.Start,
        VerticalOptions = LayoutOptions.Center,
        Shadow = new Shadow { Brush = new SolidColorBrush(Color.FromRgba(0, 0, 0, 0.25)), Offset = new Point(0, 1), Radius = 2 }
    };

    public DeskToggle()
    {
        WidthRequest = 44;
        HeightRequest = 24;
        CornerRadius = 12;
        Padding = new Thickness(3, 0);
        HoverKey = null;
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.Center;
        Content = _knob;
        Update(animate: false);
    }

    public bool IsToggled { get => (bool)GetValue(IsToggledProperty); set => SetValue(IsToggledProperty, value); }

    protected override void OnClicked()
    {
        IsToggled = !IsToggled;
        base.OnClicked();
    }

    private void Update(bool animate)
    {
        NormalKey = IsToggled ? "AccentFill" : "SurfaceCardPressed";
        if (IsToggled)
            _knob.SetAppThemeColor(BackgroundColorProperty, Colors.White, Colors.White);
        else
            ThemeColors.Bind(_knob, BackgroundColorProperty, "TextTertiary");

        var x = IsToggled ? 20 : 0;
        if (animate)
            _ = _knob.TranslateToAsync(x, 0, 120, Easing.CubicOut);
        else
            _knob.TranslationX = x;
    }
}
