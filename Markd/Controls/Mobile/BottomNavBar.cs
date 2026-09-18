using Markd.Localization;
using Microsoft.Maui.Controls.Shapes;

namespace Markd.Controls.Mobile;

/// <summary>
/// Material 3 navigation bar: 80dp, four destinations, a 64×32 pill in secondaryContainer behind the active icon,
/// filled icon when active and outlined when not.
/// </summary>
public sealed class BottomNavBar : Grid
{
    private static readonly (string Route, string LabelKey, string On, string Off)[] Destinations =
    [
        ("home", "Shell_Home", "TabHomeOn", "TabHome"),
        ("calendar", "Shell_Calendar", "TabCalendarOn", "TabCalendar"),
        ("categories", "Shell_Categories", "TabTagOn", "TabTag"),
        ("settings", "Shell_Settings", "TabGearOn", "TabGear")
    ];

    public static readonly BindableProperty SelectedProperty =
        BindableProperty.Create(nameof(Selected), typeof(string), typeof(BottomNavBar), "home", propertyChanged: (b, _, _) => ((BottomNavBar)b).Build());

    public BottomNavBar()
    {
        HeightRequest = 80;
        Padding = new Thickness(0, 12, 0, 0);
        ThemeColors.Bind(this, BackgroundColorProperty, "NavBarSurface");
        ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)];
        Build();
    }

    public string Selected { get => (string)GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }

    private void Build()
    {
        Children.Clear();
        for (var i = 0; i < Destinations.Length; i++)
        {
            var (route, labelKey, on, off) = Destinations[i];
            var active = route == Selected;

            var icon = new SvgIcon { Glyph = active ? on : off, Size = 22, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            ThemeColors.Bind(icon, SvgIcon.ColorProperty, active ? "OnPrimaryContainer" : "TextSecondary");

            var pill = new Border
            {
                WidthRequest = 64,
                HeightRequest = 32,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                BackgroundColor = Colors.Transparent,
                Content = icon,
                HorizontalOptions = LayoutOptions.Center
            };
            if (active)
                ThemeColors.Bind(pill, BackgroundColorProperty, "PrimaryContainer");

            var text = new Label
            {
                FontSize = 11.5,
                FontFamily = active ? "FigtreeSemiBold" : "FigtreeMedium",
                HorizontalOptions = LayoutOptions.Center
            };
            text.SetBinding(Label.TextProperty, Tr.Bind(labelKey));
            ThemeColors.Bind(text, Label.TextColorProperty, active ? "TextPrimary" : "TextSecondary");

            var item = new VerticalStackLayout { Spacing = 4, Children = { pill, text } };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                if (!active)
                    await Shell.Current.GoToAsync($"//{route}", false);
            };
            item.GestureRecognizers.Add(tap);
            item.SetBinding(SemanticProperties.DescriptionProperty, Tr.Bind(labelKey));
            this.Add(item, i);
        }
    }
}
