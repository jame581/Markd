using System.Globalization;

namespace Markd.Core.Localization;

/// <summary>The stored language setting and how it maps to a culture.</summary>
public static class LanguageSetting
{
    public const string System = "system";
    public const string English = "en";
    public const string Czech = "cs";
    public const string German = "de";
    public const string French = "fr";

    public static bool IsValid(string? value) => value is System or English or Czech or German or French;

    /// <summary>
    /// "system" follows the device (Czech/German/French if the device is Czech/German/French, English otherwise).
    /// The device's own regional culture is kept when its language matches and it isn't neutral, so an en-US phone
    /// keeps US formats; otherwise the language's default regional culture (en-GB / cs-CZ / de-DE / fr-FR).
    /// </summary>
    public static CultureInfo Resolve(string? setting, CultureInfo deviceCulture)
    {
        var deviceLanguage = deviceCulture.TwoLetterISOLanguageName;
        var language = setting switch
        {
            English => English,
            Czech => Czech,
            German => German,
            French => French,
            _ => deviceLanguage switch
            {
                Czech => Czech,
                German => German,
                French => French,
                _ => English
            }
        };

        if (deviceLanguage == language && !deviceCulture.IsNeutralCulture)
            return deviceCulture;

        return CultureInfo.GetCultureInfo(language switch
        {
            Czech => "cs-CZ",
            German => "de-DE",
            French => "fr-FR",
            _ => "en-GB"
        });
    }
}
