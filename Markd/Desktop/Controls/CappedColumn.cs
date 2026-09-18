namespace Markd.Desktop.Controls;

/// <summary>
/// A left-aligned column that fills the available width up to <see cref="MaxColumnWidth"/>.
/// (MaximumWidthRequest alone centres the content, and HorizontalOptions="Start" shrinks it to its text.)
/// </summary>
public sealed class CappedColumn : ContentView
{
    public static readonly BindableProperty MaxColumnWidthProperty =
        BindableProperty.Create(nameof(MaxColumnWidth), typeof(double), typeof(CappedColumn), 760d, propertyChanged: (b, _, _) => ((CappedColumn)b).Apply());

    public CappedColumn()
    {
        SizeChanged += (_, _) => Apply();
    }

    public double MaxColumnWidth { get => (double)GetValue(MaxColumnWidthProperty); set => SetValue(MaxColumnWidthProperty, value); }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(Content))
            Apply();
    }

    private void Apply()
    {
        if (Content is not { } content || Width <= 0)
            return;

        content.HorizontalOptions = LayoutOptions.Start;
        content.WidthRequest = Math.Min(MaxColumnWidth, Width);
    }
}
