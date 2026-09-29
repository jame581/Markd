using Markd.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Markd.Core.Tests;

public class SyncFoundationTests
{
    private static readonly DateTime Old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task New_entities_get_a_SyncId_and_are_stamped_on_add()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;

        var occasion = new Occasion { Title = "A", AnchorDate = DateTime.UtcNow };
        db.Occasions.Add(occasion);
        await db.SaveChangesAsync();

        Assert.NotEqual(Guid.Empty, occasion.SyncId);
        Assert.True(occasion.UpdatedAt > Old);
        Assert.Null(occasion.DeletedAt);
    }

    [Fact]
    public async Task Editing_a_field_bumps_UpdatedAt()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;

        var occasion = new Occasion { Title = "A", AnchorDate = DateTime.UtcNow };
        db.Occasions.Add(occasion);
        db.StampTimestamps = false;
        occasion.UpdatedAt = Old;
        await db.SaveChangesAsync();
        db.StampTimestamps = true;

        occasion.Title = "B";
        await db.SaveChangesAsync();

        Assert.True(occasion.UpdatedAt > Old);
    }

    [Fact]
    public async Task Changing_only_Notified_does_not_bump_UpdatedAt()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;

        var occasion = new Occasion { Title = "A", AnchorDate = DateTime.UtcNow };
        var milestone = new Milestone { Label = "M", ThresholdDays = 10, UpdatedAt = Old };
        occasion.Milestones.Add(milestone);
        db.Occasions.Add(occasion);
        db.StampTimestamps = false;
        await db.SaveChangesAsync();
        db.StampTimestamps = true;

        milestone.Notified = true;
        await db.SaveChangesAsync();

        Assert.Equal(Old, milestone.UpdatedAt);
    }

    [Fact]
    public async Task StampTimestamps_false_keeps_the_supplied_UpdatedAt()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;

        db.StampTimestamps = false;
        var category = new Category { Name = "C", UpdatedAt = Old };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        Assert.Equal(Old, category.UpdatedAt);
    }

    [Fact]
    public async Task Tombstones_are_hidden_by_default_and_visible_when_filters_are_ignored()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;

        db.Categories.Add(new Category { Name = "live" });
        db.Categories.Add(new Category { Name = "dead", DeletedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        Assert.Single(await db.Categories.ToListAsync());
        Assert.Equal(2, await db.Categories.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task SyncId_is_unique()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;

        var id = Guid.NewGuid();
        db.Categories.Add(new Category { Name = "a", SyncId = id });
        db.Categories.Add(new Category { Name = "b", SyncId = id });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
