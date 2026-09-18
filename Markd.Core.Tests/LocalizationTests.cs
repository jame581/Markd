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
    [InlineData("system", "de-DE", "en-GB")]
    [InlineData("system", "en-US", "en-US")]
    [InlineData(null, "cs-CZ", "cs-CZ")]
    [InlineData("xx", "de-DE", "en-GB")]
    public void Resolve_PicksCulture(string? setting, string device, string expected) =>
        Assert.Equal(expected, LanguageSetting.Resolve(setting, CultureInfo.GetCultureInfo(device)).Name);

    [Theory]
    [InlineData("system", true)]
    [InlineData("en", true)]
    [InlineData("cs", true)]
    [InlineData("EN", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid(string? value, bool expected) => Assert.Equal(expected, LanguageSetting.IsValid(value));
}

[Collection(nameof(GlobalCultureCollection))]
public class LocalizationManagerTests
{
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
}

public partial class ResourceParityTests
{
    private static Dictionary<string, string> Load(CultureInfo culture) =>
        Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex Placeholder();

    [Fact]
    public void Czech_HasExactlyTheEnglishKeys_WithMatchingPlaceholders()
    {
        var english = Load(CultureInfo.InvariantCulture);
        var czech = Load(CultureInfo.GetCultureInfo("cs"));

        Assert.Empty(english.Keys.Except(czech.Keys));
        Assert.Empty(czech.Keys.Except(english.Keys));

        foreach (var (key, value) in english)
        {
            var expected = Placeholder().Matches(value).Select(m => m.Groups[1].Value).Distinct().Order();
            var actual = Placeholder().Matches(czech[key]).Select(m => m.Groups[1].Value).Distinct().Order();
            Assert.True(expected.SequenceEqual(actual), $"Placeholders differ for {key}");
        }
    }
}
