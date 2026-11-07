using Markd.Core.Services;
using Markd.Core.Sync;
using Xunit;

namespace Markd.Core.Tests;

public class SyncContentTests
{
    private static ExportModel Model(string title = "t") => new()
    {
        Occasions = { new OccasionDto { SyncId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Title = title, AnchorDay = new DateOnly(2026, 1, 1), Direction = "Since" } },
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
    public async Task Same_day_on_different_devices_is_unchanged_content()
    {
        static async Task<OccasionDto> ReadIn(TimeZoneInfo zone)
        {
            var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
            using var _ = conn; using var __ = db;
            await new OccasionService(db, zone).CreateAsync(new Markd.Core.Domain.Occasion
            {
                Title = "Birthday",
                AnchorDate = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 3, 2), zone),
            });
            return Assert.Single((await new SnapshotService(db, zone).ReadForSyncAsync()).Occasions);
        }

        var west = await ReadIn(Zones.Utc(-5));
        var east = await ReadIn(Zones.Utc(9));

        Assert.Equal(SyncContent.Of(west), SyncContent.Of(east));
        Assert.Equal(SyncContent.CoreOf(west), SyncContent.CoreOf(east));
    }

    [Fact]
    public void Fingerprint_changes_when_content_changes()
        => Assert.NotEqual(SyncContent.Fingerprint(Model("a")), SyncContent.Fingerprint(Model("b")));
}
