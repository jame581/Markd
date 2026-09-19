using System.ComponentModel;
using System.Globalization;

namespace Markd.Core.Localization;

/// <summary>
/// The app's current language and the single source of truth for it. XAML binds to the indexer (<c>[Key]</c>);
/// code reads <see cref="Culture"/> (or the generated <c>Strings.*</c>, which follow <c>Strings.Culture</c>).
/// <see cref="SetCulture"/> also sets the thread and default cultures and raises a change for every property.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private LocalizationManager()
    {
    }

    public static LocalizationManager Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CultureChanged;

    public CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    public string this[string key] => Strings.ResourceManager.GetString(key, Culture) ?? $"[{key}]";

    public void SetCulture(CultureInfo culture)
    {
        if (Equals(culture, Culture) && Equals(Strings.Culture, culture)
            && Equals(CultureInfo.CurrentCulture, culture) && Equals(CultureInfo.CurrentUICulture, culture))
            return;

        // CurrentCulture/CurrentUICulture are async-local: set inside an async method (e.g. an import) they revert
        // when it returns. Everything that renders text therefore reads Culture / Strings.Culture, which do not.
        Culture = culture;
        Strings.Culture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // Every {loc:Tr} binding subscribes here. Invoke them one by one: a single control that fails to update
        // must not leave every binding after it in the old language.
        var args = new PropertyChangedEventArgs(string.Empty);
        foreach (var handler in PropertyChanged?.GetInvocationList() ?? [])
            InvokeIsolated(handler, () => ((PropertyChangedEventHandler)handler)(this, args));

        foreach (var handler in CultureChanged?.GetInvocationList() ?? [])
            InvokeIsolated(handler, () => ((EventHandler)handler)(this, EventArgs.Empty));
    }

    private static void InvokeIsolated(Delegate handler, Action invoke)
    {
        try
        {
            invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Markd.Localization] {handler.Method.DeclaringType?.FullName}.{handler.Method.Name} failed on culture change: {ex}");
        }
    }
}
