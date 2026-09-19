using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Markd.Core.Localization;
using Xunit;

namespace Markd.Core.Tests;

public class PluralTests
{
    [Theory]
    [InlineData(1, Plural.Category.One)]
    [InlineData(0, Plural.Category.Many)]
    [InlineData(2, Plural.Category.Many)]
    [InlineData(5, Plural.Category.Many)]
    [InlineData(-1, Plural.Category.One)]
    public void English_HasOneAndOther(long n, Plural.Category expected) =>
        Assert.Equal(expected, Plural.Select(n, CultureInfo.GetCultureInfo("en-GB")));

    [Theory]
    [InlineData(1, Plural.Category.One)]
    [InlineData(2, Plural.Category.Few)]
    [InlineData(4, Plural.Category.Few)]
    [InlineData(0, Plural.Category.Many)]
    [InlineData(5, Plural.Category.Many)]
    [InlineData(21, Plural.Category.Many)]
    [InlineData(22, Plural.Category.Many)]
    [InlineData(-3, Plural.Category.Few)]
    public void Czech_HasOneFewMany(long n, Plural.Category expected) =>
        Assert.Equal(expected, Plural.Select(n, CultureInfo.GetCultureInfo("cs-CZ")));

    [Theory]
    [InlineData(1, Plural.Category.One)]
    [InlineData(0, Plural.Category.Many)]
    [InlineData(2, Plural.Category.Many)]
    [InlineData(5, Plural.Category.Many)]
    public void German_HasOneAndOther(long n, Plural.Category expected) =>
        Assert.Equal(expected, Plural.Select(n, CultureInfo.GetCultureInfo("de-DE")));

    [Theory]
    [InlineData(0, Plural.Category.One)]
    [InlineData(1, Plural.Category.One)]
    [InlineData(-1, Plural.Category.One)]
    [InlineData(2, Plural.Category.Many)]
    [InlineData(5, Plural.Category.Many)]
    public void French_HasOneAndOther(long n, Plural.Category expected) =>
        Assert.Equal(expected, Plural.Select(n, CultureInfo.GetCultureInfo("fr-FR")));

    [Fact]
    public void Format_PicksKeyAndFormatsCount()
    {
        var cs = CultureInfo.GetCultureInfo("cs-CZ");
        Assert.Equal("1 den", Plural.Format("Unit_Days", 1, cs));
        Assert.Equal("3 dny", Plural.Format("Unit_Days", 3, cs));
        Assert.Equal("7 dní", Plural.Format("Unit_Days", 7, cs));
        Assert.Equal("1 day", Plural.Format("Unit_Days", 1, CultureInfo.GetCultureInfo("en-GB")));
        Assert.Equal("7 days", Plural.Format("Unit_Days", 7, CultureInfo.GetCultureInfo("en-GB")));
    }
}

public class LanguageSettingTests
{
    [Theory]
    [InlineData("en", "cs-CZ", "en-GB")]
    [InlineData("en", "en-US", "en-US")]
    [InlineData("cs", "en-US", "cs-CZ")]
    [InlineData("cs", "cs-CZ", "cs-CZ")]
    [InlineData("system", "cs-CZ", "cs-CZ")]
    [InlineData("system", "de-DE", "de-DE")]
    [InlineData("system", "en-US", "en-US")]
    [InlineData(null, "cs-CZ", "cs-CZ")]
    [InlineData("xx", "de-DE", "de-DE")]
    [InlineData("de", "en-US", "de-DE")]
    [InlineData("fr", "en-US", "fr-FR")]
    [InlineData("system", "de-AT", "de-AT")]
    [InlineData("system", "fr-CA", "fr-CA")]
    [InlineData("system", "de", "de-DE")]
    [InlineData("de", "de-CH", "de-CH")]
    public void Resolve_PicksCulture(string? setting, string device, string expected) =>
        Assert.Equal(expected, LanguageSetting.Resolve(setting, CultureInfo.GetCultureInfo(device)).Name);

    [Theory]
    [InlineData("system", true)]
    [InlineData("en", true)]
    [InlineData("cs", true)]
    [InlineData("de", true)]
    [InlineData("fr", true)]
    [InlineData("EN", false)]
    [InlineData("DE", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid(string? value, bool expected) => Assert.Equal(expected, LanguageSetting.IsValid(value));
}

[Collection(nameof(GlobalCultureCollection))]
public class LocalizationManagerTests
{
    [Fact]
    public void SetCulture_OneThrowingSubscriber_DoesNotStopTheOthers()
    {
        // Every {loc:Tr} binding subscribes to PropertyChanged; one native control failing to update must not
        // leave every binding after it in the old language.
        var manager = LocalizationManager.Instance;
        var original = manager.Culture;
        var reached = 0;
        var cultureChanged = 0;
        PropertyChangedEventHandler throwing = (_, _) => throw new InvalidOperationException("native control refused");
        PropertyChangedEventHandler counting = (_, _) => reached++;
        EventHandler throwingCulture = (_, _) => throw new InvalidOperationException("header refused");
        EventHandler countingCulture = (_, _) => cultureChanged++;
        manager.PropertyChanged += throwing;
        manager.PropertyChanged += counting;
        manager.CultureChanged += throwingCulture;
        manager.CultureChanged += countingCulture;
        try
        {
            manager.SetCulture(CultureInfo.GetCultureInfo(Equals(original, CultureInfo.GetCultureInfo("cs-CZ")) ? "en-GB" : "cs-CZ"));

            Assert.Equal(1, reached);
            Assert.Equal(1, cultureChanged);
        }
        finally
        {
            manager.PropertyChanged -= throwing;
            manager.PropertyChanged -= counting;
            manager.CultureChanged -= throwingCulture;
            manager.CultureChanged -= countingCulture;
            manager.SetCulture(original);
        }
    }

    [Fact]
    public void Indexer_ReturnsTextForCurrentCulture_AndRaisesChange()
    {
        var manager = LocalizationManager.Instance;
        var original = manager.Culture;
        var raised = 0;
        PropertyChangedEventHandler handler = (_, _) => raised++;
        manager.PropertyChanged += handler;
        try
        {
            manager.SetCulture(CultureInfo.GetCultureInfo("cs-CZ"));
            Assert.Equal("Zrušit", manager["Common_Cancel"]);
            manager.SetCulture(CultureInfo.GetCultureInfo("en-GB"));
            Assert.Equal("Cancel", manager["Common_Cancel"]);
            Assert.Equal(2, raised);

            manager.SetCulture(CultureInfo.GetCultureInfo("en-GB"));
            Assert.Equal(2, raised); // same culture: no notification
        }
        finally
        {
            manager.PropertyChanged -= handler;
            manager.SetCulture(original);
        }
    }

    [Fact]
    public void Indexer_MissingKey_IsVisible() =>
        Assert.Equal("[No_Such_Key]", LocalizationManager.Instance["No_Such_Key"]);

    [Fact]
    public void CultureChanged_FiresOnce_ForRealChange_NotForNoOp()
    {
        var manager = LocalizationManager.Instance;
        var original = manager.Culture;
        var raised = 0;
        EventHandler handler = (_, _) => raised++;
        manager.CultureChanged += handler;
        try
        {
            var target = Equals(original, CultureInfo.GetCultureInfo("cs-CZ"))
                ? CultureInfo.GetCultureInfo("en-GB")
                : CultureInfo.GetCultureInfo("cs-CZ");

            manager.SetCulture(target);
            Assert.Equal(1, raised);

            manager.SetCulture(target); // no-op: culture is already `target`, so no event should fire
            Assert.Equal(1, raised);
        }
        finally
        {
            manager.CultureChanged -= handler;
            manager.SetCulture(original);
        }
    }
}

public partial class ResourceParityTests
{
    private static Dictionary<string, string> Load(CultureInfo culture) =>
        Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex Placeholder();

    [Theory]
    [InlineData("cs")]
    [InlineData("de")]
    [InlineData("fr")]
    public void Language_HasExactlyTheEnglishKeys_WithMatchingPlaceholders(string language)
    {
        var english = Load(CultureInfo.InvariantCulture);
        var translated = Load(CultureInfo.GetCultureInfo(language));

        Assert.Empty(english.Keys.Except(translated.Keys));
        Assert.Empty(translated.Keys.Except(english.Keys));

        foreach (var (key, value) in english)
        {
            var expected = Placeholder().Matches(value).Select(m => m.Groups[1].Value).Distinct().Order();
            var actual = Placeholder().Matches(translated[key]).Select(m => m.Groups[1].Value).Distinct().Order();
            Assert.True(expected.SequenceEqual(actual), $"Placeholders differ for {key}");
        }
    }
}
