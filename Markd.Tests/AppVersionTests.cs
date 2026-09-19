using Markd.Services;
using Xunit;

namespace Markd.Tests;

public class AppVersionTests
{
    // Markd.Tests.csproj embeds the same metadata keys as Markd.csproj, with fixed values.
    [Fact]
    public void Version_ReadsDisplayVersionFromAssemblyMetadata()
    {
        Assert.Equal("9.8.7", AppVersion.Version);
    }

    [Fact]
    public void Build_ReadsBuildNumberFromAssemblyMetadata()
    {
        Assert.Equal("65", AppVersion.Build);
    }
}
