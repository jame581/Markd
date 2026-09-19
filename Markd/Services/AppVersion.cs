using System.Reflection;

namespace Markd.Services;

/// <summary>
/// The version from Markd.csproj (<c>ApplicationDisplayVersion</c>, which a release tag must match)
/// and the build number. AppInfo reports these differently per platform (unpackaged Windows appends
/// the build to the version), so the project embeds both as assembly metadata and they are read here.
/// </summary>
public static class AppVersion
{
    public static string Version { get; } = Read("Markd.Version") ?? "0.0.0";

    public static string Build { get; } = Read("Markd.Build") ?? "0";

    private static string? Read(string key) =>
        typeof(AppVersion).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value;
}
