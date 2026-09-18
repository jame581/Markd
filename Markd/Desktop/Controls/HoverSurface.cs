using System.Windows.Input;
using Markd.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Markd.Desktop.Controls;

/// <summary>
/// A clickable desktop surface. Hover is a first-class state: the fill swaps to <see cref="HoverKey"/>
/// under the pointer, and a selected surface takes <see cref="SelectedKey"/> / <see cref="SelectedStrokeKey"/>.
/// Keys are colour tokens from Colors.xaml ("SurfaceCard" → SurfaceCardLight / SurfaceCardDark); null means transparent.
/// </summary>
public class HoverSurface : Border
{
    public static readonly BindableProperty NormalKeyProperty = Create(nameof(NormalKey), null);
    public static readonly BindableProperty HoverKeyProperty = Create(nameof(HoverKey), "SurfaceCardPressed");
    public static readonly BindableProperty SelectedKeyProperty = Create(nameof(SelectedKey), null);
    public static readonly BindableProperty StrokeKeyProperty = Create(nameof(StrokeKey), null);
    public static readonly BindableProperty SelectedStrokeKeyProperty = Create(nameof(SelectedStrokeKey), null);

    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(HoverSurface), 6d,
            propertyChanged: (b, _, n) => ((HoverSurface)b).StrokeShape = new RoundRectangle { CornerRadius = (double)n });

    public static readonly BindableProperty StrokeWidthProperty =
        BindableProperty.Create(nameof(StrokeWidth), typeof(double), typeof(HoverSurface), 1d, propertyChanged: (b, _, _) => ((HoverSurface)b).Apply());

    public static readonly BindableProperty IsSelectedProperty =
        BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(HoverSurface), false, propertyChanged: (b, _, _) => ((HoverSurface)b).Apply());

    public static readonly BindableProperty IsHoveredProperty =
        BindableProperty.Create(nameof(IsHovered), typeof(bool), typeof(HoverSurface), false, propertyChanged: (b, _, _) => ((HoverSurface)b).Apply());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(HoverSurface));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(HoverSurface));

    public HoverSurface()
    {
        StrokeShape = new RoundRectangle { CornerRadius = CornerRadius };
        Apply();

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => IsHovered = true;
        pointer.PointerExited += (_, _) => IsHovered = false;
        GestureRecognizers.Add(pointer);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OnClicked();
        GestureRecognizers.Add(tap);
    }

    public event EventHandler? Clicked;

    public string? NormalKey { get => (string?)GetValue(NormalKeyProperty); set => SetValue(NormalKeyProperty, value); }
    public string? HoverKey { get => (string?)GetValue(HoverKeyProperty); set => SetValue(HoverKeyProperty, value); }
    public string? SelectedKey { get => (string?)GetValue(SelectedKeyProperty); set => SetValue(SelectedKeyProperty, value); }
    public string? StrokeKey { get => (string?)GetValue(StrokeKeyProperty); set => SetValue(StrokeKeyProperty, value); }
    public string? SelectedStrokeKey { get => (string?)GetValue(SelectedStrokeKeyProperty); set => SetValue(SelectedStrokeKeyProperty, value); }
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }

    /// <summary>True while the pointer is over the surface; bind hover-revealed children to it.</summary>
    public bool IsHovered { get => (bool)GetValue(IsHoveredProperty); private set => SetValue(IsHoveredProperty, value); }

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    protected virtual void OnClicked()
    {
        if (!IsEnabled)
            return;

        if (Command?.CanExecute(CommandParameter) == true)
            Command.Execute(CommandParameter);
        Clicked?.Invoke(this, EventArgs.Empty);
    }

    private void Apply()
    {
        var fill = IsHovered && HoverKey is not null ? HoverKey
            : IsSelected && SelectedKey is not null ? SelectedKey
            : NormalKey;
        SetToken(BackgroundColorProperty, fill);

        var stroke = IsSelected && SelectedStrokeKey is not null ? SelectedStrokeKey : StrokeKey;
        StrokeThickness = stroke is null ? 0 : StrokeWidth;
        if (stroke is not null)
            SetToken(StrokeProperty, stroke);
    }

    private void SetToken(BindableProperty property, string? key)
    {
        if (key is null)
            this.SetAppThemeColor(property, Colors.Transparent, Colors.Transparent);
        else
            ThemeColors.Bind(this, property, key);
    }

    private static BindableProperty Create(string name, string? defaultKey) =>
        BindableProperty.Create(name, typeof(string), typeof(HoverSurface), defaultKey, propertyChanged: (b, _, _) => ((HoverSurface)b).Apply());
}
