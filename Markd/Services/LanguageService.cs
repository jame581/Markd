using System.Globalization;
using Markd.Core.Localization;

namespace Markd.Services;

/// <summary>Applies the stored language. The device culture is captured before Markd first changes it.</summary>
public static class LanguageService
{
    private static readonly CultureInfo DeviceCulture = CultureInfo.CurrentUICulture;

    /// <summary>Returns true when the culture actually changed.</summary>
    public static bool Apply(string? setting)
    {
        var culture = LanguageSetting.Resolve(setting, DeviceCulture);
        var changed = !Equals(LocalizationManager.Instance.Culture, culture);

        // Always call SetCulture: on an English-UI Windows with Czech regional format the UI culture already matches
        // but CurrentCulture does not, and dates would come out Czech. SetCulture itself no-ops when all match.
        LocalizationManager.Instance.SetCulture(culture);
        return changed;
    }
}
