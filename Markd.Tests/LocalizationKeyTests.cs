using System.Globalization;
using System.Text.RegularExpressions;
using Markd.Core.Localization;
using Xunit;

namespace Markd.Tests;

public partial class LocalizationKeyTests
{
    [GeneratedRegex(@"\{loc:Tr\s+(?:Key=)?([A-Za-z0-9_]+)")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"Tr\.Bind\(""([A-Za-z0-9_]+)""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"Plural\.Format\(\s*""([A-Za-z0-9_]+)""")]
    private static partial Regex PluralFormatKey();

    [GeneratedRegex(@"PluralWord\(\s*""([A-Za-z0-9_]+)""")]
    private static partial Regex PluralWordKey();

    [GeneratedRegex(@"\$""([A-Za-z][A-Za-z0-9_]*)_\{")]
    private static partial Regex InterpolatedPluralKey();

    [GeneratedRegex(@"new ThemeOption\(\s*""[^""]*""\s*,\s*""([A-Za-z0-9_]+)""\s*,\s*""([A-Za-z0-9_]+)""")]
    private static partial Regex ThemeOptionKeys();

    private static string AppDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Markd.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repo root not found."), "Markd");
    }

    [Fact]
    public void EveryKeyUsedInXamlAndCode_ExistsInStrings()
    {
        var used = Directory.EnumerateFiles(AppDirectory(), "*.xaml", SearchOption.AllDirectories)
            .SelectMany(f => XamlKey().Matches(File.ReadAllText(f)).Select(m => (File: f, Key: m.Groups[1].Value)))
            .Concat(Directory.EnumerateFiles(AppDirectory(), "*.cs", SearchOption.AllDirectories)
                .SelectMany(f => CodeKey().Matches(File.ReadAllText(f)).Select(m => (File: f, Key: m.Groups[1].Value))))
            .ToList();

        Assert.NotEmpty(used);
        var missing = used
            .Where(u => Strings.ResourceManager.GetString(u.Key, CultureInfo.InvariantCulture) is null)
            .Select(u => $"{Path.GetFileName(u.File)}: {u.Key}")
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryPluralBaseKeyUsedInCode_HasOneFewAndManyVariants()
    {
        var files = Directory.EnumerateFiles(AppDirectory(), "*.cs", SearchOption.AllDirectories).ToList();
        var baseKeys = files
            .SelectMany(f => PluralFormatKey().Matches(File.ReadAllText(f)).Select(m => (File: f, Key: m.Groups[1].Value)))
            .Concat(files.SelectMany(f => PluralWordKey().Matches(File.ReadAllText(f)).Select(m => (File: f, Key: m.Groups[1].Value))))
            .Concat(files.SelectMany(f => InterpolatedPluralKey().Matches(File.ReadAllText(f)).Select(m => (File: f, Key: m.Groups[1].Value))))
            .ToList();

        Assert.NotEmpty(baseKeys);
        var missing = baseKeys
            .SelectMany(u => new[] { "One", "Few", "Many" }
                .Where(suffix => Strings.ResourceManager.GetString($"{u.Key}_{suffix}", CultureInfo.InvariantCulture) is null)
                .Select(suffix => $"{Path.GetFileName(u.File)}: {u.Key}_{suffix}"))
            .Distinct()
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryThemeOptionKey_ExistsInStrings()
    {
        var used = Directory.EnumerateFiles(AppDirectory(), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => ThemeOptionKeys().Matches(File.ReadAllText(f))
                .SelectMany(m => new[] { (File: f, Key: m.Groups[1].Value), (File: f, Key: m.Groups[2].Value) }))
            .ToList();

        Assert.NotEmpty(used);
        var missing = used
            .Where(u => Strings.ResourceManager.GetString(u.Key, CultureInfo.InvariantCulture) is null)
            .Select(u => $"{Path.GetFileName(u.File)}: {u.Key}")
            .ToList();
        Assert.Empty(missing);
    }
}
