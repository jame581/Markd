using Microsoft.Maui.Controls.Shapes;

namespace Markd.Controls;

/// <summary>
/// Emoji on a tint of the occasion colour. The colour is an identity mark only, so it is drawn at
/// 13–15% alpha in light theme and 22–26% in dark (<see cref="LightAlpha"/> / <see cref="DarkAlpha"/>).
/// A negative <see cref="CornerRadius"/> draws a circle.
/// </summary>
public sealed class EmojiAvatar : Grid
{
    public static readonly BindableProperty EmojiProperty =
        BindableProperty.Create(nameof(Emoji), typeof(string), typeof(EmojiAvatar), null, propertyChanged: (b, _, _) => ((EmojiAvatar)b).Update());

    public static readonly BindableProperty ColorHexProperty =
        BindableProperty.Create(nameof(ColorHex), typeof(string), typeof(EmojiAvatar), null, propertyChanged: (b, _, _) => ((EmojiAvatar)b).Update());

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(EmojiAvatar), 40d, propertyChanged: (b, _, _) => ((EmojiAvatar)b).Update());

    public static readonly BindableProperty EmojiSizeProperty =
        BindableProperty.Create(nameof(EmojiSize), typeof(double), typeof(EmojiAvatar), 18d, propertyChanged: (b, _, _) => ((EmojiAvatar)b).Update());

    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(EmojiAvatar), -1d, propertyChanged: (b, _, _) => ((EmojiAvatar)b).Update());

    public static readonly BindableProperty LightAlphaProperty =
        BindableProperty.Create(nameof(LightAlpha), typeof(double), typeof(EmojiAvatar), 0.13d, propertyChanged: (b, _, _) => ((EmojiAvatar)b).Update());

    public static readonly BindableProperty DarkAlphaProperty =
        BindableProperty.Create(nameof(DarkAlpha), typeof(double), typeof(EmojiAvatar), 0.22d, propertyChanged: (b, _, _) => ((EmojiAvatar)b).Update());

    private readonly Border _tint = new() { StrokeThickness = 0 };
    private readonly Label _emoji = new()
    {
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
        HorizontalTextAlignment = TextAlignment.Center,
        VerticalTextAlignment = TextAlignment.Center
    };

    public EmojiAvatar()
    {
        InputTransparent = true;
        Children.Add(_tint);
        Children.Add(_emoji);
        Update();
    }

    public string? Emoji { get => (string?)GetValue(EmojiProperty); set => SetValue(EmojiProperty, value); }
    public string? ColorHex { get => (string?)GetValue(ColorHexProperty); set => SetValue(ColorHexProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double EmojiSize { get => (double)GetValue(EmojiSizeProperty); set => SetValue(EmojiSizeProperty, value); }
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double LightAlpha { get => (double)GetValue(LightAlphaProperty); set => SetValue(LightAlphaProperty, value); }
    public double DarkAlpha { get => (double)GetValue(DarkAlphaProperty); set => SetValue(DarkAlphaProperty, value); }

    private void Update()
    {
        WidthRequest = HeightRequest = Size;
        _tint.StrokeShape = CornerRadius < 0
            ? new Ellipse()
            : new RoundRectangle { CornerRadius = new Microsoft.Maui.CornerRadius(CornerRadius) };
        _tint.BackgroundColor = HexColor.Parse(ColorHex);
        _tint.SetAppTheme(OpacityProperty, LightAlpha, DarkAlpha);
        _emoji.Text = string.IsNullOrWhiteSpace(Emoji) ? "•" : Emoji;
        _emoji.FontSize = EmojiSize;
    }
}
