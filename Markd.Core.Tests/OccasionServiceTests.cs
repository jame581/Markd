using Markd.Core.Data;
using Markd.Core.Domain;
using Markd.Core.Services;
using Xunit;

namespace Markd.Core.Tests;

public class OccasionServiceTests
{
    [Fact]
    public void GetDays_Since_ReturnsElapsedDays()
    {
        using var tuple = CreateService();

        var occasion = new Occasion
        {
            Title = "Test",
            AnchorDate = DateTime.UtcNow.Date.AddDays(-10),
            Direction = OccasionDirection.Since
        };

        var days = tuple.Service.GetDays(occasion);

        Assert.Equal(10, days);
    }

    [Fact]
    public void GetDays_Until_ReturnsRemainingDays()
    {
        using var tuple = CreateService();

        var occasion = new Occasion
        {
            Title = "Test",
            AnchorDate = DateTime.UtcNow.Date.AddDays(5),
            Direction = OccasionDirection.Until
        };

        var days = tuple.Service.GetDays(occasion);

        Assert.Equal(5, days);
    }

    [Fact]
    public async Task GetPendingMilestonesAsync_ReturnsOnlyUnnotifiedHitMilestonesForSinceOccasions()
    {
        using var tuple = CreateService();

        var sinceOccasion = new Occasion
        {
            Title = "Since",
            AnchorDate = DateTime.UtcNow.Date.AddDays(-30),
            Direction = OccasionDirection.Since,
            Milestones =
            [
                new Milestone { Label = "7 days", ThresholdDays = 7, Notified = false },
                new Milestone { Label = "40 days", ThresholdDays = 40, Notified = false },
                new Milestone { Label = "notified", ThresholdDays = 5, Notified = true }
            ]
        };

        var untilOccasion = new Occasion
        {
            Title = "Until",
            AnchorDate = DateTime.UtcNow.Date.AddDays(10),
            Direction = OccasionDirection.Until,
            Milestones =
            [
                new Milestone { Label = "ignored", ThresholdDays = 7, Notified = false }
            ]
        };

        await tuple.Context.Occasions.AddRangeAsync(sinceOccasion, untilOccasion);
        await tuple.Context.SaveChangesAsync();

        var pending = await tuple.Service.GetPendingMilestonesAsync();

        Assert.Single(pending);
        Assert.Equal("Since", pending[0].Item1.Title);
        Assert.Equal("7 days", pending[0].Item2.Label);
    }

    [Fact]
    public async Task MarkMilestoneNotifiedAsync_SetsMilestoneNotifiedTrue()
    {
        using var tuple = CreateService();

        var occasion = new Occasion
        {
            Title = "Test",
            AnchorDate = DateTime.UtcNow.Date.AddDays(-10),
            Direction = OccasionDirection.Since,
            Milestones = [new Milestone { Label = "5 days", ThresholdDays = 5, Notified = false }]
        };

        await tuple.Context.Occasions.AddAsync(occasion);
        await tuple.Context.SaveChangesAsync();

        var milestoneId = occasion.Milestones.First().Id;
        await tuple.Service.MarkMilestoneNotifiedAsync(milestoneId);

        var updated = await tuple.Context.Milestones.FindAsync(milestoneId);
        Assert.NotNull(updated);
        Assert.True(updated.Notified);

        // Should no longer appear in pending list
        var pending = await tuple.Service.GetPendingMilestonesAsync();
        Assert.Empty(pending);
    }

    private static ServiceScope CreateService()
    {
        var (context, connection) = TestDbFactory.CreateSqliteInMemoryContext();
        var service = new OccasionService(context);
        return new ServiceScope(context, connection, service);
    }

    private sealed class ServiceScope(MarkdDbContext context, Microsoft.Data.Sqlite.SqliteConnection connection, OccasionService service) : IDisposable
    {
        public MarkdDbContext Context { get; } = context;
        public Microsoft.Data.Sqlite.SqliteConnection Connection { get; } = connection;
        public OccasionService Service { get; } = service;

        public void Dispose()
        {
            Context.Dispose();
            Connection.Dispose();
        }
    }
}
