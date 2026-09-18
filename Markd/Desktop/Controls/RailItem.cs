using Markd.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Markd.Desktop.Controls;

/// <summary>
/// A 38px nav-rail button: 16px glyph, 6px radius, pressed-surface fill on hover. The active item keeps
/// that fill and shows a 3×20 accent bar pinned to its left edge; its glyph switches from TextSecondary to TextPrimary.
/// </summary>
public sealed class RailItem : HoverSurface
{
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(RailItem), null, propertyChanged: (b, _, n) => ((RailItem)b)._icon.Glyph = (string?)n);

    private readonly SvgIcon _icon = new() { Size = 16, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };

    private readonly Border _bar = new()
    {
        WidthRequest = 3,
        HeightRequest = 20,
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = 2 },
        HorizontalOptions = LayoutOptions.Start,
        VerticalOptions = LayoutOptions.Center,
        IsVisible = false
    };

    public RailItem()
    {
        WidthRequest = 38;
        HeightRequest = 38;
        CornerRadius = 6;
        SelectedKey = "SurfaceCardPressed";
        ThemeColors.Bind(_bar, BackgroundColorProperty, "AccentFill");
        Content = new Grid { InputTransparent = true, CascadeInputTransparent = true, Children = { _bar, _icon } };
        UpdateState();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsSelected))
                UpdateState();
        };
    }

    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    /// <summary>The section key this item opens ("home", "calendar"…); null for the theme toggle.</summary>
    public string? Section { get; set; }

    private void UpdateState()
    {
        _bar.IsVisible = IsSelected;
        ThemeColors.Bind(_icon, SvgIcon.ColorProperty, IsSelected ? "TextPrimary" : "TextSecondary");
    }
}
