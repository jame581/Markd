using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace Markd.Controls;

/// <summary>A round icon button (48 by default) drawing one of <see cref="Icons"/>.</summary>
public sealed class IconButton : Border
{
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(IconButton), null, propertyChanged: (b, _, n) => ((IconButton)b)._icon.Glyph = (string?)n);

    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(IconButton), Colors.Black, propertyChanged: (b, _, n) => ((IconButton)b)._icon.Color = (Color)n);

    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(nameof(IconSize), typeof(double), typeof(IconButton), 22d, propertyChanged: (b, _, n) => ((IconButton)b)._icon.Size = (double)n);

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(IconButton), 48d, propertyChanged: (b, _, n) => ((IconButton)b).ApplySize((double)n));

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(IconButton));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(IconButton));

    private readonly SvgIcon _icon = new() { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Size = 22 };

    public IconButton()
    {
        StrokeThickness = 0;
        BackgroundColor = Colors.Transparent;
        Content = _icon;
        ApplySize(48);
        ThemeColors.Bind(this, IconColorProperty, "TextSecondary");

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _ = PressAsync();
            if (Command?.CanExecute(CommandParameter) == true)
                Command.Execute(CommandParameter);
            Clicked?.Invoke(this, EventArgs.Empty);
        };
        GestureRecognizers.Add(tap);
    }

    public event EventHandler? Clicked;

    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public Color IconColor { get => (Color)GetValue(IconColorProperty); set => SetValue(IconColorProperty, value); }
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    private async Task PressAsync()
    {
        await this.FadeToAsync(0.55, 60);
        await this.FadeToAsync(1, 120);
    }

    private void ApplySize(double size)
    {
        WidthRequest = HeightRequest = size;
        StrokeShape = new RoundRectangle { CornerRadius = size / 2 };
    }
}
