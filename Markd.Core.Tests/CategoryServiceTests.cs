using Markd.Core.Domain;
using Markd.Core.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Markd.Core.Tests;

public class CategoryServiceTests
{
    [Fact]
    public async Task UpdateAsync_AcceptsDetachedInstanceWhileOriginalIsTracked()
    {
        var (context, connection) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = connection;
        using var __ = context;
        var service = new CategoryService(context);

        var created = await service.CreateAsync(new Category { Name = "Health", Emoji = "🌿", ColorHex = "#0B8043" });

        var updated = await service.UpdateAsync(new Category { Id = created.Id, Name = "Wellbeing", Emoji = "🧘", ColorHex = "#039BE5" });

        Assert.Equal("Wellbeing", updated.Name);
        var reloaded = await service.GetByIdAsync(created.Id);
        Assert.Equal("🧘", reloaded!.Emoji);
        Assert.Equal("#039BE5", reloaded.ColorHex);
    }

    private static Category Snapshot(Category c) =>
        new() { SyncId = c.SyncId, Name = c.Name, Emoji = c.Emoji, ColorHex = c.ColorHex };

    [Fact]
    public async Task Restore_revives_the_deleted_category_with_its_identity()
    {
        var (context, connection) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = connection;
        using var __ = context;
        var service = new CategoryService(context);
        var created = await service.CreateAsync(new Category { Name = "Health", Emoji = "🌿", ColorHex = "#0B8043" });
        var snapshot = Snapshot(created);
        await service.DeleteAsync(created.Id);

        var restored = await service.RestoreAsync(snapshot);

        Assert.Equal(created.Id, restored.Id);
        Assert.Equal(created.SyncId, restored.SyncId);
        Assert.Null(restored.DeletedAt);
        Assert.Equal(created.Id, Assert.Single(await service.GetAllAsync()).Id);
    }

    [Fact]
    public async Task Restore_of_a_live_category_returns_it_without_a_copy()
    {
        var (context, connection) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = connection;
        using var __ = context;
        var service = new CategoryService(context);
        var created = await service.CreateAsync(new Category { Name = "Health" });
        var snapshot = Snapshot(created);
        await service.DeleteAsync(created.Id);

        var first = await service.RestoreAsync(snapshot);
        var second = await service.RestoreAsync(snapshot);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await service.GetAllAsync());
    }

    [Fact]
    public async Task Restore_after_the_tombstone_was_purged_creates_a_new_category()
    {
        var (context, connection) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = connection;
        using var __ = context;
        var service = new CategoryService(context);
        var created = await service.CreateAsync(new Category { Name = "Health", Emoji = "🌿", ColorHex = "#0B8043" });
        var snapshot = Snapshot(created);
        await service.DeleteAsync(created.Id);
        context.Categories.IgnoreQueryFilters().ToList().ForEach(c => context.Categories.Remove(c));
        await context.SaveChangesAsync();

        var restored = await service.RestoreAsync(snapshot);

        Assert.Equal("Health", restored.Name);
        Assert.Equal("🌿", restored.Emoji);
        Assert.Equal("#0B8043", restored.ColorHex);
        Assert.Null(restored.DeletedAt);
        Assert.Single(await service.GetAllAsync());
    }

    [Fact]
    public async Task Undoing_a_category_delete_keeps_an_assignment_made_on_another_device()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var category = await a.Categories.CreateAsync(new Category { Name = "Family" });
        await a.AddOccasionAsync("Z");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        var zOnB = (await b.Occasions.GetAllAsync()).Single();
        zOnB.CategoryId = (await b.Categories.GetAllAsync()).Single().Id;
        await b.Occasions.UpdateAsync(zOnB);
        Assert.True((await b.Sync.SyncAsync(cloud)).IsSuccess);

        var snapshot = Snapshot(category);
        await a.Categories.DeleteAsync(category.Id);
        var restored = await a.Categories.RestoreAsync(snapshot);
        Assert.Equal(category.Id, restored.Id);
        Assert.True((await a.Sync.SyncAsync(cloud)).IsSuccess);
        Assert.True((await b.Sync.SyncAsync(cloud)).IsSuccess);

        foreach (var device in new[] { a, b })
        {
            var only = Assert.Single(await device.Occasions.GetAllAsync());
            var family = Assert.Single(await device.Categories.GetAllAsync());
            Assert.Equal("Family", family.Name);
            Assert.Equal(family.Id, only.CategoryId);
        }
    }
}
