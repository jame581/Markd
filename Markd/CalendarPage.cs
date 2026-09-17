namespace Markd;

public class CalendarPage : ContentPage
{
    public CalendarPage()
    {
        Title = "Calendar";
        Content = new VerticalStackLayout
        {
            Padding = 16,
            Children =
            {
                new Label
                {
                    Text = "Calendar",
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold
                },
                new Label
                {
                    Text = "Calendar view placeholder."
                }
            }
        };
    }
}
