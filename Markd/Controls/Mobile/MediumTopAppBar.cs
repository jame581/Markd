namespace Markd.Controls.Mobile;

/// <summary>Material 3 medium top app bar for the four destinations: 96dp, title 28/700 bottom-left, trailing icon buttons.</summary>
[ContentProperty(nameof(Trailing))]
public sealed class MediumTopAppBar : Grid
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(MediumTopAppBar), string.Empty, propertyChanged: (b, _, n) => ((MediumTopAppBar)b)._title.Text = (string)n);

    public static readonly BindableProperty TrailingProperty =
        BindableProperty.Create(nameof(Trailing), typeof(View), typeof(MediumTopAppBar), null, propertyChanged: (b, o, n) => ((MediumTopAppBar)b).SetTrailing(o as View, n as View));

    private readonly Label _title = new() { VerticalOptions = LayoutOptions.End, Margin = new Thickness(20, 0, 0, 14) };

    public MediumTopAppBar()
    {
        HeightRequest = 96;
        ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)];
        _title.Style = (Style)Application.Current!.Resources["M3AppBarTitle"];
        Add(_title);
    }

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public View? Trailing { get => (View?)GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }

    private void SetTrailing(View? old, View? view)
    {
        if (old is not null)
            Remove(old);
        if (view is null)
            return;

        view.VerticalOptions = LayoutOptions.End;
        view.Margin = new Thickness(0, 0, 12, 14);
        this.Add(view, 1);
    }
}
