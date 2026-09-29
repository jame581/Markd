using Markd.Core.Domain;
using Markd.Core.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Markd.Core.Tests;

public class SoftDeleteServiceTests
{
    private static (OccasionService Occasions, CategoryService Categories, Data.MarkdDbContext Db, IDisposable Conn) Create()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        return (new OccasionService(db), new CategoryService(db), db, conn);
    }

    private static Occasion NewOccasion(string title = "A") =>
        new() { Title = title, AnchorDate = DateTime.UtcNow.AddDays(-5), Direction = OccasionDirection.Since };

    [Fact]
    public async Task Deleting_an_occasion_tombstones_it_and_its_milestones_and_the_tracked_copy_forgets_them()
    {
        var (occasions, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var o = await occasions.CreateAsync(NewOccasion());
        await occasions.AddMilestoneAsync(o.Id, 3, "three");
        Assert.Single(o.Milestones);

        await occasions.DeleteAsync(o.Id);

        Assert.Empty(await occasions.GetAllAsync());
        Assert.Empty(o.Milestones);   // the tracked instance no longer lists the deleted milestone
        Assert.NotNull((await db.Occasions.IgnoreQueryFilters().SingleAsync()).DeletedAt);
        Assert.NotNull((await db.Milestones.IgnoreQueryFilters().SingleAsync()).DeletedAt);
    }

    [Fact]
    public async Task A_deleted_occasion_cannot_be_found_updated_or_pinned()
    {
        var (occasions, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var o = await occasions.CreateAsync(NewOccasion());
        await occasions.DeleteAsync(o.Id);

        Assert.Null(await occasions.GetByIdAsync(o.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => occasions.UpdateAsync(new Occasion { Id = o.Id, Title = "x", AnchorDate = DateTime.UtcNow }));
        await occasions.SetPinnedAsync(o.Id);
        Assert.False((await db.Occasions.IgnoreQueryFilters().SingleAsync()).IsPinned);
        await Assert.ThrowsAsync<InvalidOperationException>(() => occasions.AddMilestoneAsync(o.Id, 1, "late"));
    }

    [Fact]
    public async Task A_deleted_milestone_is_hidden_everywhere_including_the_tracked_occasion()
    {
        var (occasions, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var o = await occasions.CreateAsync(NewOccasion());
        var m = await occasions.AddMilestoneAsync(o.Id, 1, "one");
        Assert.Single(await occasions.GetPendingMilestonesAsync());

        await occasions.RemoveMilestoneAsync(m.Id);

        Assert.Empty(await occasions.GetPendingMilestonesAsync());
        Assert.Empty((await occasions.GetByIdAsync(o.Id))!.Milestones);
        Assert.Empty((await occasions.GetAllAsync()).Single().Milestones);
        await occasions.MarkMilestoneNotifiedAsync(m.Id);   // must not resurrect or throw
        Assert.False((await db.Milestones.IgnoreQueryFilters().SingleAsync()).Notified);
        Assert.NotNull((await db.Milestones.IgnoreQueryFilters().SingleAsync()).DeletedAt);
    }

    [Fact]
    public async Task DeleteAll_tombstones_everything_and_does_not_block_a_new_pin()
    {
        var (occasions, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var a = NewOccasion("a"); a.IsPinned = true;
        await occasions.CreateAsync(a);
        await occasions.CreateAsync(NewOccasion("b"));

        await occasions.DeleteAllAsync();

        Assert.Empty(await occasions.GetAllAsync());
        Assert.Equal(2, await db.Occasions.IgnoreQueryFilters().CountAsync(o => o.DeletedAt != null));
        var c = NewOccasion("c"); c.IsPinned = true;
        await occasions.CreateAsync(c);   // the tombstoned pin is outside the unique pin index
    }

    [Fact]
    public async Task Deleting_a_category_tombstones_it_and_uncategorises_its_occasions()
    {
        var (occasions, categories, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var cat = await categories.CreateAsync(new Category { Name = "Family" });
        var o = NewOccasion(); o.CategoryId = cat.Id;
        await occasions.CreateAsync(o);

        await categories.DeleteAsync(cat.Id);

        Assert.Empty(await categories.GetAllAsync());
        Assert.Null(await categories.GetByIdAsync(cat.Id));
        Assert.Null((await occasions.GetByIdAsync(o.Id))!.CategoryId);
        Assert.NotNull((await db.Categories.IgnoreQueryFilters().SingleAsync()).DeletedAt);
    }

    [Fact]
    public async Task Undo_revives_the_same_record_with_its_pin_and_milestones()
    {
        var (occasions, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var a = NewOccasion("a"); a.IsPinned = true;
        await occasions.CreateAsync(a);
        var milestone = await occasions.AddMilestoneAsync(a.Id, 2, "two");
        await occasions.MarkMilestoneNotifiedAsync(milestone.Id);
        var snapshot = (await occasions.GetByIdAsync(a.Id))!;   // the tracked instance, as the view model holds it
        var originalSyncId = snapshot.SyncId;
        var originalId = snapshot.Id;

        await occasions.DeleteAsync(a.Id);
        var b = NewOccasion("b"); b.IsPinned = true;
        await occasions.CreateAsync(b);                          // allowed: the deleted pin does not count

        var restored = await occasions.RestoreAsync(snapshot);

        Assert.Equal(originalId, restored.Id);
        Assert.Equal(originalSyncId, restored.SyncId);
        Assert.True(restored.IsPinned);
        var all = await occasions.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal("a", Assert.Single(all, o => o.IsPinned).Title);
        Assert.True(Assert.Single(all.Single(o => o.Title == "a").Milestones).Notified);
    }

    [Fact]
    public async Task Undo_of_a_record_that_no_longer_exists_inserts_a_fresh_copy()
    {
        var (occasions, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var detached = new Occasion { Title = "ghost", AnchorDate = DateTime.UtcNow };
        detached.Milestones.Add(new Milestone { ThresholdDays = 4, Label = "four" });

        var restored = await occasions.RestoreAsync(detached);

        Assert.Single(await occasions.GetAllAsync());
        Assert.Single((await occasions.GetByIdAsync(restored.Id))!.Milestones);
    }
}
