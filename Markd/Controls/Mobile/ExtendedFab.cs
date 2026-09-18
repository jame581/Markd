using System.Windows.Input;
using Markd.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Markd.Controls.Mobile;

/// <summary>
/// Material 3 extended FAB: 56dp, radius 16, primary fill, 22dp plus and a 15/600 label.
/// Moves up 68dp while a snackbar is showing so it is never covered.
/// </summary>
public sealed class ExtendedFab : Border
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(ExtendedFab), string.Empty, propertyChanged: (b, _, n) => ((ExtendedFab)b)._label.Text = (string)n);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(ExtendedFab));

    private readonly Label _label = new() { FontFamily = "FigtreeSemiBold", FontSize = 15, VerticalOptions = LayoutOptions.Center };

    public ExtendedFab()
    {
        HeightRequest = 56;
        Padding = new Thickness(20, 0);
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = 16 };
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.End;
        Shadow = new Shadow { Brush = new SolidColorBrush(Color.FromRgba(20, 22, 30, 0.34)), Offset = new Point(0, 6), Radius = 16 };
        ThemeColors.Bind(this, BackgroundColorProperty, "AccentFill");

        var plus = new SvgIcon { Glyph = "Plus", Size = 22, VerticalOptions = LayoutOptions.Center };
        ThemeColors.Bind(plus, SvgIcon.ColorProperty, "OnPrimary");
        ThemeColors.Bind(_label, Label.TextColorProperty, "OnPrimary");
        Content = new HorizontalStackLayout { Spacing = 9, Children = { plus, _label } };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            await this.ScaleToAsync(0.96, 60);
            await this.ScaleToAsync(1, 100);
            if (Command?.CanExecute(null) == true)
                Command.Execute(null);
        };
        GestureRecognizers.Add(tap);

        Loaded += (_, _) => SnackbarFeedbackService.VisibilityChanged += OnSnackbarVisibilityChanged;
        Unloaded += (_, _) => SnackbarFeedbackService.VisibilityChanged -= OnSnackbarVisibilityChanged;
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    private void OnSnackbarVisibilityChanged(object? sender, bool visible) =>
        _ = this.TranslateToAsync(0, visible ? -68 : 0, 400, Easing.CubicOut);
}
