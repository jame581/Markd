using Markd.Core.Data;
using Markd.Core.Domain;
using Markd.Core.Services;
using Xunit;

namespace Markd.Core.Tests;

public class CoreIntegrationTests
{
    [Fact]
    public async Task OccasionCrud_WorksWithSqlite()
    {
        using var scope = CreateScope();
        var service = new OccasionService(scope.Context);

        var created = await service.CreateAsync(new Occasion
        {
            Title = "Birthday",
            AnchorDate = DateTime.UtcNow.Date,
            Direction = OccasionDirection.Since,
            Notes = "note"
        });

        created.Title = "Birthday Updated";
        await service.UpdateAsync(created);

        var loaded = await service.GetByIdAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Birthday Updated", loaded.Title);

        await service.DeleteAsync(created.Id);

        var deleted = await service.GetByIdAsync(created.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task SetPinnedAsync_EnforcesSinglePinnedOccasion()
    {
        using var scope = CreateScope();
        var service = new OccasionService(scope.Context);

        var first = await service.CreateAsync(new Occasion
        {
            Title = "First",
            AnchorDate = DateTime.UtcNow.Date,
            Direction = OccasionDirection.Since,
            IsPinned = true
        });

        var second = await service.CreateAsync(new Occasion
        {
            Title = "Second",
            AnchorDate = DateTime.UtcNow.Date,
            Direction = OccasionDirection.Since
        });

        await service.SetPinnedAsync(second.Id);

        var all = await service.GetAllAsync();
        Assert.Single(all.Where(o => o.IsPinned));
        Assert.True(all.Single(o => o.Id == second.Id).IsPinned);
        Assert.False(all.Single(o => o.Id == first.Id).IsPinned);
    }

    [Fact]
    public async Task UpdateAsync_WorksWhenTrackedInstanceAlreadyExists()
    {
        using var scope = CreateScope();
        var service = new OccasionService(scope.Context);

        var created = await service.CreateAsync(new Occasion
        {
            Title = "Betka",
            AnchorDate = DateTime.UtcNow.Date,
            Direction = OccasionDirection.Since
        });

        _ = await service.GetByIdAsync(created.Id);

        var category = await new CategoryService(scope.Context).CreateAsync(new Category { Name = "Family" });

        var detachedUpdate = new Occasion
        {
            Id = created.Id,
            Title = "Betka",
            AnchorDate = created.AnchorDate,
            Direction = created.Direction,
            Notes = "updated",
            CategoryId = category.Id
        };

        var updated = await service.UpdateAsync(detachedUpdate);

        Assert.Equal(category.Id, updated.CategoryId);
        Assert.Equal("updated", updated.Notes);
    }

    [Fact]
    public async Task CategoryService_Crud_WorksWithSqlite()
    {
        using var scope = CreateScope();
        var service = new CategoryService(scope.Context);

        var created = await service.CreateAsync(new Category { Name = "Health", Emoji = "💪" });
        Assert.True(created.Id > 0);

        created.Name = "Wellness";
        await service.UpdateAsync(created);

        var loaded = await service.GetByIdAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Wellness", loaded.Name);

        await service.DeleteAsync(created.Id);

        var deleted = await service.GetByIdAsync(created.Id);
        Assert.Null(deleted);
    }

    private static Scope CreateScope()
    {
        var (context, connection) = TestDbFactory.CreateSqliteInMemoryContext();
        return new Scope(context, connection);
    }

    private sealed class Scope(MarkdDbContext context, Microsoft.Data.Sqlite.SqliteConnection connection) : IDisposable
    {
        public MarkdDbContext Context { get; } = context;
        public Microsoft.Data.Sqlite.SqliteConnection Connection { get; } = connection;

        public void Dispose()
        {
            Context.Dispose();
            Connection.Dispose();
        }
    }
}
