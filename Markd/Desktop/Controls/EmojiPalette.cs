using Markd.Localization;

namespace Markd.Desktop.Controls;

/// <summary>
/// Icon choice for the desktop dialogs: the first six emoji as 36px buttons plus a "more" toggle that
/// reveals the rest, or every choice at once with <see cref="ShowAll"/>. The selected emoji gets the
/// accent tint and an accent border. Rows are laid out on a fixed <see cref="Columns"/> grid.
/// </summary>
public sealed class EmojiPalette : VerticalStackLayout
{
    public static readonly BindableProperty ChoicesProperty =
        BindableProperty.Create(nameof(Choices), typeof(IReadOnlyList<string>), typeof(EmojiPalette), null, propertyChanged: (b, _, _) => ((EmojiPalette)b).Build());

    public static readonly BindableProperty SelectedEmojiProperty =
        BindableProperty.Create(nameof(SelectedEmoji), typeof(string), typeof(EmojiPalette), null, BindingMode.TwoWay, propertyChanged: (b, _, _) => ((EmojiPalette)b).Sync());

    public static readonly BindableProperty ShowAllProperty =
        BindableProperty.Create(nameof(ShowAll), typeof(bool), typeof(EmojiPalette), false, propertyChanged: (b, _, _) => ((EmojiPalette)b).Build());

    public static readonly BindableProperty ColumnsProperty =
        BindableProperty.Create(nameof(Columns), typeof(int), typeof(EmojiPalette), 10, propertyChanged: (b, _, _) => ((EmojiPalette)b).Build());

    private const int QuickCount = 6;
    private const double Cell = 36;
    private const double Gap = 6;
    private readonly Grid _quick = new() { ColumnSpacing = Gap, RowSpacing = Gap, HorizontalOptions = LayoutOptions.Start };
    private readonly Grid _more = new() { ColumnSpacing = Gap, RowSpacing = Gap, HorizontalOptions = LayoutOptions.Start, IsVisible = false };
    private readonly List<(string Emoji, HoverSurface Button)> _buttons = [];
    private DeskButton? _toggle;

    public EmojiPalette()
    {
        Spacing = Gap;
        Children.Add(_quick);
        Children.Add(_more);
    }

    public IReadOnlyList<string>? Choices { get => (IReadOnlyList<string>?)GetValue(ChoicesProperty); set => SetValue(ChoicesProperty, value); }
    public string? SelectedEmoji { get => (string?)GetValue(SelectedEmojiProperty); set => SetValue(SelectedEmojiProperty, value); }
    public bool ShowAll { get => (bool)GetValue(ShowAllProperty); set => SetValue(ShowAllProperty, value); }
    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }

    /// <summary>Raised when the user clicks an emoji (not when the selection changes from a binding).</summary>
    public event EventHandler? EmojiPicked;

    private void Build()
    {
        Reset(_quick);
        Reset(_more);
        _buttons.Clear();
        _toggle = null;
        if (Choices is null)
            return;

        var columns = Math.Max(1, Columns);
        var quick = ShowAll ? Choices.ToList() : Choices.Take(QuickCount).ToList();
        var rest = ShowAll ? [] : Choices.Skip(QuickCount).ToList();

        Place(_quick, quick, columns);
        Place(_more, rest, columns);

        if (rest.Count > 0)
        {
            _toggle = new DeskButton
            {
                Variant = DeskButtonVariant.Outline,
                Glyph = "WinChevronDown",
                GlyphSize = 12,
                WidthRequest = Cell,
                HeightRequest = Cell,
                ContentKey = "TextSecondary"
            };
            _toggle.SetBinding(ToolTipProperties.TextProperty, Tr.Bind("Desk_MoreIcons"));
            _toggle.Clicked += (_, _) => SetExpanded(!_more.IsVisible);
            EnsureColumns(_quick, quick.Count + 1);
            _quick.Add(_toggle, quick.Count, 0);
        }

        Sync();
    }

    private void Place(Grid grid, IReadOnlyList<string> emoji, int columns)
    {
        EnsureColumns(grid, Math.Min(columns, emoji.Count));
        for (var i = 0; i < emoji.Count; i++)
        {
            var row = i / columns;
            while (grid.RowDefinitions.Count <= row)
                grid.RowDefinitions.Add(new RowDefinition(Cell));
            grid.Add(CreateButton(emoji[i]), i % columns, row);
        }
    }

    private HoverSurface CreateButton(string emoji)
    {
        var button = new HoverSurface
        {
            WidthRequest = Cell,
            HeightRequest = Cell,
            CornerRadius = 8,
            NormalKey = "PageBackground",
            StrokeKey = "CardBorder",
            SelectedKey = "AccentTint",
            SelectedStrokeKey = "AccentBorderColor",
            Content = new Label { Text = emoji, FontSize = 17, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, InputTransparent = true }
        };
        button.Clicked += (_, _) =>
        {
            SelectedEmoji = emoji;
            EmojiPicked?.Invoke(this, EventArgs.Empty);
        };
        _buttons.Add((emoji, button));
        return button;
    }

    private static void EnsureColumns(Grid grid, int count)
    {
        while (grid.ColumnDefinitions.Count < count)
            grid.ColumnDefinitions.Add(new ColumnDefinition(Cell));
    }

    private static void Reset(Grid grid)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Add(new RowDefinition(Cell));
    }

    private void Sync()
    {
        foreach (var (emoji, button) in _buttons)
            button.IsSelected = emoji == SelectedEmoji;

        // An icon from the longer list opens the panel so the selection is visible.
        if (!ShowAll && Choices is not null && SelectedEmoji is not null && Choices.Skip(QuickCount).Contains(SelectedEmoji))
            SetExpanded(true);
    }

    private void SetExpanded(bool expanded)
    {
        _more.IsVisible = expanded;
        if (_toggle is not null)
            _toggle.Glyph = expanded ? "WinChevronUp" : "WinChevronDown";
    }
}
