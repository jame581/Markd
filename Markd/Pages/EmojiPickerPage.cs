using Markd.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Markd.Pages;

/// <summary>Material 3 dialog with a grid of icon choices. Returns the picked emoji, or null when dismissed.</summary>
public sealed class EmojiPickerPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _result = new();
    private bool _closing;

    private EmojiPickerPage(IReadOnlyList<string> choices, string? current)
    {
        Style = (Style)Application.Current!.Resources["M3DialogPage"];
        Shell.SetPresentationMode(this, PresentationMode.ModalNotAnimated);

        var grid = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Center };
        foreach (var emoji in choices)
        {
            var cell = new Border
            {
                WidthRequest = 46,
                HeightRequest = 46,
                Margin = new Thickness(2),
                StrokeShape = new RoundRectangle { CornerRadius = 23 },
                StrokeThickness = emoji == current ? 2 : 0,
                BackgroundColor = Colors.Transparent,
                Content = new Label { Text = emoji, FontSize = 22, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            ThemeColors.Bind(cell, Border.StrokeProperty, "AccentFill");
            if (emoji == current)
                ThemeColors.Bind(cell, BackgroundColorProperty, "PrimaryContainer");

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await CloseAsync(emoji);
            cell.GestureRecognizers.Add(tap);
            grid.Children.Add(cell);
        }

        var cancel = new Button { Text = "Cancel", Style = (Style)Application.Current.Resources["M3TextButton"], HorizontalOptions = LayoutOptions.End };
        cancel.Clicked += async (_, _) => await CloseAsync(null);

        Content = new Grid
        {
            Children =
            {
                new Border
                {
                    Style = (Style)Application.Current.Resources["M3DialogCard"],
                    Padding = new Thickness(20, 24, 16, 12),
                    Content = new VerticalStackLayout
                    {
                        Spacing = 14,
                        Children =
                        {
                            new Label { Text = "Pick an icon", FontFamily = "FigtreeSemiBold", FontSize = 22, Margin = new Thickness(4, 0) },
                            grid,
                            cancel
                        }
                    }
                }
            }
        };
    }

    public static async Task<string?> PickAsync(INavigation navigation, IReadOnlyList<string> choices, string? current)
    {
        var page = new EmojiPickerPage(choices, current);
        await navigation.PushModalAsync(page, false);
        return await page._result.Task;
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private async Task CloseAsync(string? value)
    {
        // Claimed before the await, so a second tap during the pop cannot pop the page underneath.
        if (_closing)
            return;

        _closing = true;
        await Navigation.PopModalAsync(false);
        _result.TrySetResult(value);
    }
}
