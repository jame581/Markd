using System.Globalization;

namespace Markd.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#FF0066") to a MAUI Color.
/// Returns the ConverterParameter (fallback Color) when the string is null/invalid.
/// </summary>
public class ColorHexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try { return Color.FromArgb(hex); } catch { }
        }
        return parameter as Color ?? Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Returns true when the binding value string equals the ConverterParameter string.
/// Used to show/hide the checkmark on the selected color swatch.
/// </summary>
public class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && parameter is string p && s.Equals(p, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
