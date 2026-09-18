using Microsoft.Maui.Controls.Shapes;

namespace Markd.Controls;

/// <summary>A thin rounded progress track (next-milestone bar). Progress is 0–1.</summary>
public sealed class ProgressTrack : Grid
{
    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(ProgressTrack), 0d, propertyChanged: (b, _, _) => ((ProgressTrack)b).UpdateFill());

    public static readonly BindableProperty TrackColorProperty =
        BindableProperty.Create(nameof(TrackColor), typeof(Color), typeof(ProgressTrack), Colors.LightGray, propertyChanged: (b, _, n) => ((ProgressTrack)b)._track.BackgroundColor = (Color)n);

    public static readonly BindableProperty FillColorProperty =
        BindableProperty.Create(nameof(FillColor), typeof(Color), typeof(ProgressTrack), Colors.Purple, propertyChanged: (b, _, n) => ((ProgressTrack)b)._fill.BackgroundColor = (Color)n);

    private readonly Border _track = new() { StrokeThickness = 0 };
    private readonly Border _fill = new() { StrokeThickness = 0, HorizontalOptions = LayoutOptions.Start };

    public ProgressTrack()
    {
        HeightRequest = 6;
        Children.Add(_track);
        Children.Add(_fill);
        SizeChanged += (_, _) => UpdateFill();
        ThemeColors.Bind(this, FillColorProperty, "AccentFill");
    }

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Color TrackColor { get => (Color)GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }
    public Color FillColor { get => (Color)GetValue(FillColorProperty); set => SetValue(FillColorProperty, value); }

    private void UpdateFill()
    {
        var radius = new RoundRectangle { CornerRadius = HeightRequest / 2 };
        _track.StrokeShape = radius;
        _fill.StrokeShape = new RoundRectangle { CornerRadius = HeightRequest / 2 };
        if (Width > 0)
            _fill.WidthRequest = Math.Max(HeightRequest, Width * Math.Clamp(Progress, 0, 1));
    }
}
