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
    public async Task Milestone_AddRemove_WorksWithSqlite()
    {
        using var scope = CreateScope();
        var service = new OccasionService(scope.Context);

        var occasion = await service.CreateAsync(new Occasion
        {
            Title = "Anniversary",
            AnchorDate = DateTime.UtcNow.Date,
            Direction = OccasionDirection.Since
        });

        var milestone = await service.AddMilestoneAsync(occasion.Id, 30, "30 days");
        Assert.True(milestone.Id > 0);

        var loaded = await service.GetByIdAsync(occasion.Id);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Milestones);

        await service.RemoveMilestoneAsync(milestone.Id);

        loaded = await service.GetByIdAsync(occasion.Id);
        Assert.NotNull(loaded);
        Assert.Empty(loaded.Milestones);
    }

    [Fact]
    public async Task AppSettings_NotificationTimeOfDay_PersistsWithSqlite()
    {
        using var scope = CreateScope();
        var service = new AppSettingsService(scope.Context);

        var settings = await service.GetAsync();
        settings.NotificationTimeOfDay = new TimeSpan(7, 45, 0);

        await service.SaveAsync(settings);

        var reloaded = await service.GetAsync();
        Assert.Equal(new TimeSpan(7, 45, 0), reloaded.NotificationTimeOfDay);
    }

    [Fact]
    public async Task DeleteAllAsync_RemovesOccasionsAndMilestones_WithSqlite()
    {
        using var scope = CreateScope();
        var service = new OccasionService(scope.Context);

        var occasion = await service.CreateAsync(new Occasion
        {
            Title = "Cleanup",
            AnchorDate = DateTime.UtcNow.Date,
            Direction = OccasionDirection.Since
        });

        await service.AddMilestoneAsync(occasion.Id, 5, "5 days");

        await service.DeleteAllAsync();

        Assert.Empty(await service.GetAllAsync());
        Assert.Empty(scope.Context.Milestones);
    }

    [Fact]
    public async Task GetCalendarMarksAsync_UsesLocalDisplayDates_WithSqlite()
    {
        using var scope = CreateScope();
        var service = new OccasionService(scope.Context);

        var utcInstant = new DateTime(2026, 1, 31, 23, 30, 0, DateTimeKind.Utc);
        var expectedLocalDate = DateOnly.FromDateTime(DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc).ToLocalTime());

        var anchorOccasion = await service.CreateAsync(new Occasion
        {
            Title = "Anchor",
            AnchorDate = utcInstant,
            Direction = OccasionDirection.Since,
            Emoji = "🎂",
            ColorHex = "#ff0000"
        });

        var milestoneOccasion = await service.CreateAsync(new Occasion
        {
            Title = "Milestone",
            AnchorDate = utcInstant.AddDays(-1),
            Direction = OccasionDirection.Since,
            Emoji = "💍",
            ColorHex = "#00ff00"
        });

        await service.AddMilestoneAsync(milestoneOccasion.Id, 1, "1 day");

        var marks = await service.GetCalendarMarksAsync(expectedLocalDate.Year, expectedLocalDate.Month);

        Assert.Equal(2, marks.Count);

        var anchorMark = Assert.Single(marks, mark => mark.Kind == CalendarMarkKind.Anchor);
        Assert.Equal(expectedLocalDate, anchorMark.Date);
        Assert.Equal(anchorOccasion.Id, anchorMark.OccasionId);
        Assert.Equal("Anchor", anchorMark.Title);

        var milestoneMark = Assert.Single(marks, mark => mark.Kind == CalendarMarkKind.Milestone);
        Assert.Equal(expectedLocalDate, milestoneMark.Date);
        Assert.Equal(milestoneOccasion.Id, milestoneMark.OccasionId);
        Assert.Equal("Milestone", milestoneMark.Title);
        Assert.Equal("1 day", milestoneMark.Label);
        Assert.Equal(1, milestoneMark.ThresholdDays);
    }

    [Fact]
    public async Task GetCalendarMarksAsync_MilestoneDateIgnoresDaylightSavingChanges()
    {
        using var scope = CreateScope();
        var service = new OccasionService(scope.Context);

        // Stored the way the form stores anchors: the UTC instant of local midnight. In a zone with DST the
        // anchor is in summer time and the milestone in winter time, which used to shift the dot a day early.
        var occasion = await service.CreateAsync(new Occasion
        {
            Title = "Our anniversary",
            AnchorDate = new DateTime(2022, 10, 14, 0, 0, 0, DateTimeKind.Local).ToUniversalTime(),
            Direction = OccasionDirection.Since
        });
        await service.AddMilestoneAsync(occasion.Id, 1500, "Fifteen hundred");

        var marks = await service.GetCalendarMarksAsync(2026, 11);

        var milestone = Assert.Single(marks, mark => mark.Kind == CalendarMarkKind.Milestone);
        Assert.Equal(new DateOnly(2026, 11, 22), milestone.Date);
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
