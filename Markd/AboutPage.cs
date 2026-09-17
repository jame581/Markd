namespace Markd;

public class AboutPage : ContentPage
{
    public AboutPage()
    {
        Title = "About";
        Content = new VerticalStackLayout
        {
            Padding = 16,
            Children =
            {
                new Label
                {
                    Text = "About",
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold
                },
                new Label
                {
                    Text = "About page placeholder."
                }
            }
        };
    }
}
