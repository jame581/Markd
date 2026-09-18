using System.ComponentModel;
using System.Windows.Input;
using Markd.Controls;
using Markd.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace Markd.Desktop.Controls;

/// <summary>
/// The twelve identity swatches as two rows of six 36px circles with 11px gaps. The selected swatch gets a
/// surface-coloured gap ring, an accent ring and a white check.
/// </summary>
public sealed class SwatchGrid : Grid
{
    public static readonly BindableProperty ChoicesProperty =
        BindableProperty.Create(nameof(Choices), typeof(IEnumerable<ColorChoice>), typeof(SwatchGrid), null, propertyChanged: (b, _, _) => ((SwatchGrid)b).Build());

    public static readonly BindableProperty SelectCommandProperty =
        BindableProperty.Create(nameof(SelectCommand), typeof(ICommand), typeof(SwatchGrid));

    // The choices usually belong to a long-lived view model, so the handlers are removed when the grid unloads.
    private readonly List<(ColorChoice Choice, PropertyChangedEventHandler Handler)> _subscriptions = [];
    private bool _detached;

    public SwatchGrid()
    {
        Loaded += (_, _) =>
        {
            if (_detached)
                Build();
        };
        Unloaded += (_, _) =>
        {
            Detach();
            _detached = true;
        };
        ColumnSpacing = 3;
        RowSpacing = 3;
        HorizontalOptions = LayoutOptions.Start;
        for (var i = 0; i < 6; i++)
            ColumnDefinitions.Add(new ColumnDefinition(44));
        RowDefinitions.Add(new RowDefinition(44));
        RowDefinitions.Add(new RowDefinition(44));
    }

    public IEnumerable<ColorChoice>? Choices { get => (IEnumerable<ColorChoice>?)GetValue(ChoicesProperty); set => SetValue(ChoicesProperty, value); }
    public ICommand? SelectCommand { get => (ICommand?)GetValue(SelectCommandProperty); set => SetValue(SelectCommandProperty, value); }

    private void Build()
    {
        Detach();
        _detached = false;
        Children.Clear();
        if (Choices is null)
            return;

        var index = 0;
        foreach (var choice in Choices.Take(12))
        {
            var swatch = CreateSwatch(choice);
            this.Add(swatch, index % 6, index / 6);
            index++;
        }
    }

    private View CreateSwatch(ColorChoice choice)
    {
        // 44px cell: a 2px accent ring at the edge, a 2px gap, then the 36px swatch.
        var ring = new Border
        {
            StrokeShape = new Ellipse(),
            StrokeThickness = 2,
            BackgroundColor = Colors.Transparent,
            Stroke = (Color)Application.Current!.Resources["AccentBorderColor"]
        };
        var swatch = new Border
        {
            WidthRequest = 36,
            HeightRequest = 36,
            StrokeShape = new Ellipse(),
            StrokeThickness = 0,
            BackgroundColor = choice.Color,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        var check = new SvgIcon { Glyph = "WinCheck", Size = 16, Color = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };

        var cell = new Grid { WidthRequest = 44, HeightRequest = 44, Children = { ring, swatch, check } };
        ToolTipProperties.SetText(cell, choice.Hex);

        void Sync()
        {
            ring.IsVisible = choice.IsSelected;
            check.IsVisible = choice.IsSelected;
        }

        Sync();
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == nameof(ColorChoice.IsSelected))
                Sync();
        };
        choice.PropertyChanged += handler;
        _subscriptions.Add((choice, handler));

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (SelectCommand?.CanExecute(choice.Hex) == true)
                SelectCommand.Execute(choice.Hex);
        };
        cell.GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => swatch.Scale = 1.06;
        pointer.PointerExited += (_, _) => swatch.Scale = 1;
        cell.GestureRecognizers.Add(pointer);
        return cell;
    }

    private void Detach()
    {
        foreach (var (choice, handler) in _subscriptions)
            choice.PropertyChanged -= handler;
        _subscriptions.Clear();
    }
}
