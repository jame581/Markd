using System.Globalization;

namespace Markd.Core.Localization;

/// <summary>Plural categories for the app's languages: English one/other, Czech one/few (2–4)/many.</summary>
public static class Plural
{
    public enum Category { One, Few, Many }

    public static Category Select(long n, CultureInfo? culture = null)
    {
        culture ??= LocalizationManager.Instance.Culture;
        var abs = Math.Abs(n);
        if (abs == 1)
            return Category.One;

        return culture.TwoLetterISOLanguageName == LanguageSetting.Czech && abs is >= 2 and <= 4
            ? Category.Few
            : Category.Many;
    }

    /// <summary>Looks up <c>{baseKey}_One/_Few/_Many</c> and formats it with <paramref name="n"/>.</summary>
    public static string Format(string baseKey, long n, CultureInfo? culture = null)
    {
        culture ??= LocalizationManager.Instance.Culture;
        var key = $"{baseKey}_{Select(n, culture)}";
        var pattern = Strings.ResourceManager.GetString(key, culture) ?? $"[{key}]";
        return string.Format(culture, pattern, n);
    }
}
