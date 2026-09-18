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
}
