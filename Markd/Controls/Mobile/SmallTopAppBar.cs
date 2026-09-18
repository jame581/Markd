using System.Windows.Input;

namespace Markd.Controls.Mobile;

/// <summary>Material 3 small top app bar for pushed views: 64dp, back arrow (or close), title 20/600, trailing icon buttons.</summary>
[ContentProperty(nameof(Trailing))]
public sealed class SmallTopAppBar : Grid
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(SmallTopAppBar), string.Empty, propertyChanged: (b, _, n) => ((SmallTopAppBar)b)._title.Text = (string)n);

    public static readonly BindableProperty LeadingGlyphProperty =
        BindableProperty.Create(nameof(LeadingGlyph), typeof(string), typeof(SmallTopAppBar), "Back", propertyChanged: (b, _, n) => ((SmallTopAppBar)b)._leading.Glyph = (string)n);

    public static readonly BindableProperty LeadingCommandProperty =
        BindableProperty.Create(nameof(LeadingCommand), typeof(ICommand), typeof(SmallTopAppBar));

    public static readonly BindableProperty TrailingProperty =
        BindableProperty.Create(nameof(Trailing), typeof(View), typeof(SmallTopAppBar), null, propertyChanged: (b, o, n) => ((SmallTopAppBar)b).SetTrailing(o as View, n as View));

    private readonly IconButton _leading = new() { Glyph = "Back", IconSize = 22 };
    private readonly Label _title = new() { Margin = new Thickness(6, 0, 0, 0) };

    public SmallTopAppBar()
    {
        HeightRequest = 64;
        Padding = new Thickness(12, 0, 8, 0);
        ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)];
        _title.Style = (Style)Application.Current!.Resources["M3SmallBarTitle"];
        ThemeColors.Bind(_leading, IconButton.IconColorProperty, "TextPrimary");
        _leading.VerticalOptions = LayoutOptions.Center;
        _leading.Clicked += async (_, _) =>
        {
            if (LeadingCommand is { } command)
            {
                if (command.CanExecute(null))
                    command.Execute(null);
                return;
            }

            await Shell.Current.GoToAsync("..");
        };
        this.Add(_leading, 0);
        this.Add(_title, 1);
    }

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string LeadingGlyph { get => (string)GetValue(LeadingGlyphProperty); set => SetValue(LeadingGlyphProperty, value); }
    public ICommand? LeadingCommand { get => (ICommand?)GetValue(LeadingCommandProperty); set => SetValue(LeadingCommandProperty, value); }
    public View? Trailing { get => (View?)GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }

    private void SetTrailing(View? old, View? view)
    {
        if (old is not null)
            Remove(old);
        if (view is null)
            return;

        view.VerticalOptions = LayoutOptions.Center;
        this.Add(view, 2);
    }
}
