using Markd.Core.Data;
using Markd.Core.Services;
using Markd.Core.Sync;

namespace Markd.Core.Tests;

internal sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    public DateTime Now { get; set; } = utcNow;
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
}

internal sealed class FakeSecrets : ISyncSecretStore
{
    public string? Passphrase { get; set; }
    public Task<string?> GetPassphraseAsync() => Task.FromResult(Passphrase);
    public Task SetPassphraseAsync(string? passphrase) { Passphrase = passphrase; return Task.CompletedTask; }
}

/// <summary>One simulated phone or PC: its own database, state and passphrase store, sharing a cloud.</summary>
internal sealed class SyncDevice : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    public MarkdDbContext Db { get; }
    public OccasionService Occasions { get; }
    public CategoryService Categories { get; }
    public SnapshotService Snapshots { get; }
    public SyncStateStore State { get; }
    public FakeSecrets Secrets { get; } = new();
    public FixedTimeProvider Clock { get; } = new(DateTime.UtcNow);
    public SyncService Sync { get; }

    /// <summary>The device's local time zone: anchors are stored as the UTC instant of a local midnight here.</summary>
    public TimeZoneInfo Zone { get; }

    public SyncDevice(string passphrase = "pass", TimeZoneInfo? zone = null)
    {
        Zone = zone ?? TimeZoneInfo.Utc;
        (Db, _connection) = TestDbFactory.CreateSqliteInMemoryContext();
        Occasions = new OccasionService(Db);
        Categories = new CategoryService(Db);
        Snapshots = new SnapshotService(Db, Zone);
        State = new SyncStateStore(Db);
        Secrets.Passphrase = passphrase;
        Sync = new SyncService(Snapshots, State, Secrets, Clock, new SyncOptions { KdfIterations = MarkdPackage.MinIterations }, new SyncGate());
    }

    public async Task<Markd.Core.Domain.Occasion> AddOccasionAsync(string title, int daysAgo = 10) =>
        await Occasions.CreateAsync(new Markd.Core.Domain.Occasion
        {
            Title = title,
            AnchorDate = DateTime.UtcNow.Date.AddDays(-daysAgo),
            Direction = Markd.Core.Domain.OccasionDirection.Since,
        });

    /// <summary>An occasion typed on this device for a calendar day, stored the way the form stores it.</summary>
    public async Task<Markd.Core.Domain.Occasion> AddOccasionOnAsync(string title, DateOnly day) =>
        await Occasions.CreateAsync(new Markd.Core.Domain.Occasion
        {
            Title = title,
            AnchorDate = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), Zone),
            Direction = Markd.Core.Domain.OccasionDirection.Since,
        });

    /// <summary>The calendar day this device shows for an occasion.</summary>
    public DateOnly DayOf(Markd.Core.Domain.Occasion occasion) =>
        DateOnly.FromDateTime(Markd.Core.Domain.OccasionDates.ToLocalDate(occasion.AnchorDate, Zone));

    public async Task<string[]> TitlesAsync() =>
        (await Occasions.GetAllAsync()).Select(o => o.Title).OrderBy(t => t).ToArray();

    public void Dispose() { Db.Dispose(); _connection.Dispose(); }
}
