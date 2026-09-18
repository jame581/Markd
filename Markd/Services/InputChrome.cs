using Microsoft.Maui.Handlers;

namespace Markd.Services;

/// <summary>
/// Removes native underlines and borders from text inputs and pickers: the design draws its own
/// outlined (Android) or bordered (Windows) frames around every field.
/// </summary>
public static class InputChrome
{
    public static void Configure()
    {
#if ANDROID
        var transparent = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
        EntryHandler.Mapper.AppendToMapping("MarkdBorderless", (handler, _) => handler.PlatformView.BackgroundTintList = transparent);
        EditorHandler.Mapper.AppendToMapping("MarkdBorderless", (handler, _) => handler.PlatformView.BackgroundTintList = transparent);
        PickerHandler.Mapper.AppendToMapping("MarkdBorderless", (handler, _) => handler.PlatformView.BackgroundTintList = transparent);
        DatePickerHandler.Mapper.AppendToMapping("MarkdBorderless", (handler, _) => handler.PlatformView.BackgroundTintList = transparent);
        TimePickerHandler.Mapper.AppendToMapping("MarkdBorderless", (handler, _) => handler.PlatformView.BackgroundTintList = transparent);
#elif WINDOWS
        EntryHandler.Mapper.AppendToMapping("MarkdBorderless", (handler, _) => StripTextBox(handler.PlatformView));
        EditorHandler.Mapper.AppendToMapping("MarkdBorderless", (handler, _) => StripTextBox(handler.PlatformView));
        PickerHandler.Mapper.AppendToMapping("MarkdBorderless", (handler, _) => StripControl(handler.PlatformView, "ComboBox"));
        DatePickerHandler.Mapper.AppendToMapping("MarkdBorderless", (handler, _) =>
        {
            StripControl(handler.PlatformView, "CalendarDatePicker");
            handler.PlatformView.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        });
#endif
    }

#if WINDOWS
    private static void StripTextBox(Microsoft.UI.Xaml.Controls.TextBox textBox)
    {
        var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var zero = new Microsoft.UI.Xaml.Thickness(0);
        textBox.BorderThickness = zero;
        textBox.Background = clear;
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
                                    "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled" })
            textBox.Resources[key] = clear;
        textBox.Resources["TextControlBorderThemeThickness"] = zero;
        textBox.Resources["TextControlBorderThemeThicknessFocused"] = zero;
        textBox.Padding = new Microsoft.UI.Xaml.Thickness(0, 6, 0, 6);
    }

    /// <summary>Clears a WinUI control's own fill and border in every visual state; <paramref name="prefix"/> is its theme-resource prefix.</summary>
    private static void StripControl(Microsoft.UI.Xaml.Controls.Control control, string prefix)
    {
        control.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
        control.Background = Clear();
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Focused", "Unfocused", "Disabled" })
        {
            SetResource(control, $"{prefix}Background{state}", Clear());
            SetResource(control, $"{prefix}BorderBrush{state}", Clear());
        }
        SetResource(control, $"{prefix}BorderThemeThickness", new Microsoft.UI.Xaml.Thickness(0));
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Clear() => new(Microsoft.UI.Colors.Transparent);

    // The mapper runs again on later property changes; WinUI rejects re-inserting a key that is already in use.
    private static void SetResource(Microsoft.UI.Xaml.FrameworkElement element, string key, object value)
    {
        if (!element.Resources.ContainsKey(key))
            element.Resources.Add(key, value);
    }
#endif
}
