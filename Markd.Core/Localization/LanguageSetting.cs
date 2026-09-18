using System.Globalization;

namespace Markd.Core.Localization;

/// <summary>The stored language setting and how it maps to a culture.</summary>
public static class LanguageSetting
{
    public const string System = "system";
    public const string English = "en";
    public const string Czech = "cs";

    public static bool IsValid(string? value) => value is System or English or Czech;

    /// <summary>
    /// "system" follows the device (Czech if the device is Czech, English otherwise). The device's own regional
    /// culture is kept when its language matches, so an en-US phone keeps US formats; otherwise en-GB / cs-CZ.
    /// </summary>
    public static CultureInfo Resolve(string? setting, CultureInfo deviceCulture)
    {
        var deviceLanguage = deviceCulture.TwoLetterISOLanguageName;
        var language = setting switch
        {
            English => English,
            Czech => Czech,
            _ => deviceLanguage == Czech ? Czech : English
        };

        if (deviceLanguage == language && !deviceCulture.IsNeutralCulture)
            return deviceCulture;

        return CultureInfo.GetCultureInfo(language == Czech ? "cs-CZ" : "en-GB");
    }
}
