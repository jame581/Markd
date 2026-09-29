using Markd.Core.Services;
using Markd.Core.Sync;
using Xunit;

namespace Markd.Core.Tests;

public class SyncContentTests
{
    private static ExportModel Model(string title = "t") => new()
    {
        Occasions = { new OccasionDto { SyncId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Title = title, AnchorDate = new DateTime(2026, 1, 1), Direction = "Since" } },
    };

    [Fact]
    public void Fingerprint_ignores_timestamps_notified_and_tombstones()
    {
        var a = Model();
        var b = Model();
        b.Occasions[0].UpdatedAt = DateTime.UtcNow;
        b.Occasions.Add(new OccasionDto { SyncId = Guid.NewGuid(), Title = "gone", DeletedAt = DateTime.UtcNow });
        Assert.Equal(SyncContent.Fingerprint(a), SyncContent.Fingerprint(b));
    }

    [Fact]
    public void Fingerprint_changes_when_content_changes()
        => Assert.NotEqual(SyncContent.Fingerprint(Model("a")), SyncContent.Fingerprint(Model("b")));
}
