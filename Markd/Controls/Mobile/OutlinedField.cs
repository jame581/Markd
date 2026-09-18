using Microsoft.Maui.Controls.Shapes;

namespace Markd.Controls.Mobile;

/// <summary>
/// Material 3 outlined text field frame: 56dp, radius 8, 1px outline, the label floating on the surface at the top-left.
/// Put the input (Entry, Picker, DatePicker…) in <see cref="Field"/>.
/// </summary>
[ContentProperty(nameof(Field))]
public sealed class OutlinedField : Grid
{
    public static readonly BindableProperty LabelProperty =
        BindableProperty.Create(nameof(Label), typeof(string), typeof(OutlinedField), string.Empty, propertyChanged: (b, _, n) => ((OutlinedField)b)._label.Text = (string)n);

    public static readonly BindableProperty FieldProperty =
        BindableProperty.Create(nameof(Field), typeof(View), typeof(OutlinedField), null, propertyChanged: (b, _, n) => ((OutlinedField)b)._frame.Content = (View?)n);

    private readonly Border _frame = new()
    {
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 8 },
        MinimumHeightRequest = 56,
        Padding = new Thickness(16, 0, 12, 0),
        Margin = new Thickness(0, 7, 0, 0)
    };

    private readonly Label _label = new()
    {
        FontFamily = "FigtreeMedium",
        FontSize = 12,
        Padding = new Thickness(5, 0),
        Margin = new Thickness(12, 0, 0, 0),
        HorizontalOptions = LayoutOptions.Start,
        VerticalOptions = LayoutOptions.Start
    };

    public OutlinedField()
    {
        ThemeColors.Bind(_frame, Border.StrokeProperty, "CardBorder");
        ThemeColors.Bind(_label, Microsoft.Maui.Controls.Label.TextColorProperty, "TextSecondary");
        ThemeColors.Bind(_label, BackgroundColorProperty, "PageBackground");
        Children.Add(_frame);
        Children.Add(_label);
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public View? Field { get => (View?)GetValue(FieldProperty); set => SetValue(FieldProperty, value); }
}
