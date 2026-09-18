using Markd.Controls;

namespace Markd.Desktop.Controls;

public enum DeskButtonVariant
{
    /// <summary>AccentFill with white content; brightens under the pointer.</summary>
    Accent,

    /// <summary>Card fill, 1px border; pressed-surface fill under the pointer.</summary>
    Tonal,

    /// <summary>Transparent with a 1px border (calendar ‹ Today ›).</summary>
    Outline,

    /// <summary>Transparent, no border (icon buttons, rail items).</summary>
    Subtle,

    /// <summary>Danger text and border; fills with Danger under the pointer.</summary>
    Danger
}

/// <summary>
/// Desktop button that can carry a glyph and/or text, drawn with the Markd tokens. Plain text buttons use the Desk*Button
/// styles on <see cref="Button"/>; this covers the icon buttons and the accent "+ New occasion".
/// </summary>
public sealed class DeskButton : HoverSurface
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(DeskButton), null, propertyChanged: (b, _, _) => ((DeskButton)b).Rebuild());

    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(DeskButton), null, propertyChanged: (b, _, _) => ((DeskButton)b).Rebuild());

    public static readonly BindableProperty GlyphSizeProperty =
        BindableProperty.Create(nameof(GlyphSize), typeof(double), typeof(DeskButton), 14d, propertyChanged: (b, _, _) => ((DeskButton)b).Rebuild());

    public static readonly BindableProperty VariantProperty =
        BindableProperty.Create(nameof(Variant), typeof(DeskButtonVariant), typeof(DeskButton), DeskButtonVariant.Tonal, propertyChanged: (b, _, _) => ((DeskButton)b).ApplyVariant());

    /// <summary>Content colour token for Subtle/Outline buttons (TextSecondary by default).</summary>
    public static readonly BindableProperty ContentKeyProperty =
        BindableProperty.Create(nameof(ContentKey), typeof(string), typeof(DeskButton), null, propertyChanged: (b, _, _) => ((DeskButton)b).UpdateContentColor());

    /// <summary>Content colour token while hovered (e.g. Danger on a remove button).</summary>
    public static readonly BindableProperty HoverContentKeyProperty =
        BindableProperty.Create(nameof(HoverContentKey), typeof(string), typeof(DeskButton), null, propertyChanged: (b, _, _) => ((DeskButton)b).UpdateContentColor());

    private readonly SvgIcon _icon = new() { VerticalOptions = LayoutOptions.Center };
    private readonly Label _label = new() { VerticalOptions = LayoutOptions.Center, FontFamily = "FigtreeSemiBold", FontSize = 13, LineBreakMode = LineBreakMode.NoWrap };

    public DeskButton()
    {
        HeightRequest = 36;
        CornerRadius = 8;
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Center;
        ApplyVariant();
        Rebuild();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsHovered))
                UpdateContentColor();
        };
    }

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public double GlyphSize { get => (double)GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    public DeskButtonVariant Variant { get => (DeskButtonVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public string? ContentKey { get => (string?)GetValue(ContentKeyProperty); set => SetValue(ContentKeyProperty, value); }
    public string? HoverContentKey { get => (string?)GetValue(HoverContentKeyProperty); set => SetValue(HoverContentKeyProperty, value); }

    /// <summary>Font family of the text (FigtreeSemiBold by default; FigtreeMedium for secondary buttons).</summary>
    public string FontFamily { get => _label.FontFamily; set => _label.FontFamily = value; }

    public double FontSize { get => _label.FontSize; set => _label.FontSize = value; }

    private void Rebuild()
    {
        var hasText = !string.IsNullOrEmpty(Text);
        var hasGlyph = !string.IsNullOrEmpty(Glyph);
        _label.Text = Text;
        _icon.Glyph = Glyph;
        _icon.Size = GlyphSize;

        // Text buttons get side padding; icon-only buttons set their own square WidthRequest.
        Padding = hasText ? new Thickness(hasGlyph ? 13 : 15, 0, 15, 0) : new Thickness(0);

        View content = hasText && hasGlyph
            ? new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Children = { _icon, _label } }
            : hasGlyph ? _icon : _label;
        content.HorizontalOptions = LayoutOptions.Center;
        content.InputTransparent = true;
        Content = content;
        UpdateContentColor();
    }

    private void ApplyVariant()
    {
        switch (Variant)
        {
            case DeskButtonVariant.Accent:
                NormalKey = "AccentFill";
                HoverKey = "AccentFillHover";
                StrokeKey = null;
                break;
            case DeskButtonVariant.Tonal:
                NormalKey = "SurfaceCard";
                HoverKey = "SurfaceCardPressed";
                StrokeKey = "CardBorder";
                break;
            case DeskButtonVariant.Outline:
                NormalKey = null;
                HoverKey = "SurfaceCardPressed";
                StrokeKey = "CardBorder";
                break;
            case DeskButtonVariant.Subtle:
                NormalKey = null;
                HoverKey = "SurfaceCardPressed";
                StrokeKey = null;
                break;
            case DeskButtonVariant.Danger:
                NormalKey = null;
                HoverKey = "Danger";
                StrokeKey = "Danger";
                break;
        }

        UpdateContentColor();
    }

    private void UpdateContentColor()
    {
        string? key = Variant switch
        {
            DeskButtonVariant.Accent => null,
            DeskButtonVariant.Danger => IsHovered ? null : "Danger",
            DeskButtonVariant.Tonal => ContentKey ?? "TextPrimary",
            _ => IsHovered && HoverContentKey is not null ? HoverContentKey : ContentKey ?? "TextPrimary"
        };

        if (key is null)
        {
            _label.SetAppThemeColor(Label.TextColorProperty, Colors.White, Colors.White);
            _icon.SetAppThemeColor(SvgIcon.ColorProperty, Colors.White, Colors.White);
        }
        else
        {
            ThemeColors.Bind(_label, Label.TextColorProperty, key);
            ThemeColors.Bind(_icon, SvgIcon.ColorProperty, key);
        }
    }
}
