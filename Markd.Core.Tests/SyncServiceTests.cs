using Markd.Core.Domain;
using Markd.Core.Services;
using Markd.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Markd.Core.Tests;

public class SyncServiceTests
{
    [Fact]
    public async Task First_sync_of_a_device_with_data_uploads_an_encrypted_snapshot()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("Wedding");

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.NotNull(cloud.Data);
        Assert.True(MarkdPackage.IsEncrypted(cloud.Data!));
        Assert.DoesNotContain("Wedding", System.Text.Encoding.UTF8.GetString(cloud.Data!));
        var state = await a.State.LoadAsync();
        Assert.Equal("test", state.ProviderId);
        Assert.Equal(cloud.Revision, state.RemoteRevision);
        Assert.NotNull(state.BaseSnapshotJson);
    }

    [Fact]
    public async Task Undo_after_a_sync_applied_another_devices_change_keeps_the_pin()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var x = await a.AddOccasionAsync("X");
        await a.Occasions.SetPinnedAsync(x.Id);
        await a.AddOccasionAsync("Y");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        var yOnB = (await b.Occasions.GetAllAsync()).Single(o => o.Title == "Y");
        yOnB.Notes = "from b";
        await b.Occasions.UpdateAsync(yOnB);
        await b.Sync.SyncAsync(cloud);

        var snapshot = (await a.Occasions.GetByIdAsync(x.Id))!;
        await a.Occasions.DeleteAsync(x.Id);
        Assert.Equal(SyncOutcome.Synced, (await a.Sync.SyncAsync(cloud)).Outcome);   // applies b's note while X is a tombstone
        var restored = await a.Occasions.RestoreAsync(snapshot);

        Assert.Equal(x.Id, restored.Id);
        Assert.True(restored.IsPinned);
        Assert.True((await a.Occasions.GetByIdAsync(x.Id))!.IsPinned);
    }

    [Fact]
    public async Task A_second_empty_device_downloads_everything()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var wedding = await a.AddOccasionAsync("Wedding");
        await a.Occasions.AddMilestoneAsync(wedding.Id, 100, "100 days");
        await a.Sync.SyncAsync(cloud);

        var result = await b.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "Wedding" }, await b.TitlesAsync());
        Assert.Single((await b.Occasions.GetAllAsync())[0].Milestones);
    }

    [Fact]
    public async Task Syncing_again_with_nothing_changed_does_no_work()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("x");
        await a.Sync.SyncAsync(cloud);
        var uploads = cloud.Uploads;
        var downloads = cloud.Downloads;

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.NoChanges, result.Outcome);
        Assert.Equal(uploads, cloud.Uploads);
        Assert.Equal(downloads, cloud.Downloads);
    }

    [Fact]
    public async Task An_edit_on_one_device_reaches_the_other_without_conflict()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var o = await a.AddOccasionAsync("Trip");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        var edit = (await a.Occasions.GetByIdAsync(o.Id))!;
        edit.Title = "Trip to Rome";
        await a.Occasions.UpdateAsync(edit);
        await a.Sync.SyncAsync(cloud);
        var result = await b.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "Trip to Rome" }, await b.TitlesAsync());
    }

    [Fact]
    public async Task Edits_to_different_records_on_both_devices_merge_silently()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("One");
        await a.AddOccasionAsync("Two");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        var aOne = (await a.Occasions.GetAllAsync()).Single(o => o.Title == "One");
        aOne.Title = "One (a)";
        await a.Occasions.UpdateAsync(aOne);
        var bTwo = (await b.Occasions.GetAllAsync()).Single(o => o.Title == "Two");
        bTwo.Title = "Two (b)";
        await b.Occasions.UpdateAsync(bTwo);

        await a.Sync.SyncAsync(cloud);
        var second = await b.Sync.SyncAsync(cloud);
        var third = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, second.Outcome);
        Assert.Equal(new[] { "One (a)", "Two (b)" }, await b.TitlesAsync());
        Assert.Equal(new[] { "One (a)", "Two (b)" }, await a.TitlesAsync());
        Assert.True(third.IsSuccess);
        Assert.Equal(SyncOutcome.NoChanges, (await a.Sync.SyncAsync(cloud)).Outcome);
        Assert.Equal(SyncOutcome.NoChanges, (await b.Sync.SyncAsync(cloud)).Outcome);
    }

    [Fact]
    public async Task A_delete_propagates()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var o = await a.AddOccasionAsync("Doomed");
        await a.AddOccasionAsync("Stays");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        await a.Occasions.DeleteAsync(o.Id);
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        Assert.Equal(new[] { "Stays" }, await b.TitlesAsync());
    }

    [Fact]
    public async Task Both_devices_holding_the_same_events_do_not_duplicate_them_or_reset_Notified()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var ao = await a.AddOccasionAsync("Wedding", daysAgo: 200);
        await a.Occasions.AddMilestoneAsync(ao.Id, 100, "100 days");
        var bo = await b.AddOccasionAsync("Wedding", daysAgo: 200);
        var bm = await b.Occasions.AddMilestoneAsync(bo.Id, 100, "100 days");
        await b.Occasions.MarkMilestoneNotifiedAsync(bm.Id);
        await a.Sync.SyncAsync(cloud);

        var result = await b.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        var all = await b.Occasions.GetAllAsync();
        var only = Assert.Single(all);
        Assert.Equal(bo.Id, only.Id);                  // renamed in place, not recreated
        Assert.True(Assert.Single(only.Milestones).Notified);
        await a.Sync.SyncAsync(cloud);
        Assert.Single(await a.Occasions.GetAllAsync());
        Assert.Equal(SyncOutcome.NoChanges, (await a.Sync.SyncAsync(cloud)).Outcome);
        Assert.Equal(SyncOutcome.NoChanges, (await b.Sync.SyncAsync(cloud)).Outcome);
    }

    [Fact]
    public async Task Syncing_does_not_touch_local_Notified_or_settings()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var o = await a.AddOccasionAsync("x");
        var m = await a.Occasions.AddMilestoneAsync(o.Id, 1000, "far away");   // not reached yet, so b must not treat it as announced
        await a.Occasions.MarkMilestoneNotifiedAsync(m.Id);
        var themeBefore = (await b.Db.AppSettings.AsNoTracking().SingleAsync()).Theme;
        await b.Db.Database.ExecuteSqlRawAsync("UPDATE AppSettings SET Theme = 'Dark'");

        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        Assert.False((await b.Occasions.GetAllAsync())[0].Milestones.Single().Notified);
        Assert.Equal("Dark", (await b.Db.AppSettings.AsNoTracking().SingleAsync()).Theme);
        Assert.NotEqual("Dark", themeBefore);
    }

    [Fact]
    public async Task A_milestone_that_arrives_already_reached_does_not_fire_again_on_the_new_device()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var o = await a.AddOccasionAsync("Wedding", daysAgo: 200);
        await a.Occasions.AddMilestoneAsync(o.Id, 100, "100 days");
        await a.Sync.SyncAsync(cloud);

        await b.Sync.SyncAsync(cloud);

        Assert.Empty(await b.Occasions.GetPendingMilestonesAsync());
    }

    [Fact]
    public async Task An_occasion_typed_while_a_sync_is_running_is_not_lost()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await b.AddOccasionAsync("From B");
        await b.Sync.SyncAsync(cloud);
        await a.AddOccasionAsync("Old on A");

        // the user adds one on A after the sync has read local data but before it applies the merge
        cloud.OnDownload = () => Task.Run(() => a.AddOccasionAsync("Typed meanwhile")).GetAwaiter().GetResult();
        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "From B", "Old on A", "Typed meanwhile" }, await a.TitlesAsync());
        await b.Sync.SyncAsync(cloud);
        Assert.Equal(new[] { "From B", "Old on A", "Typed meanwhile" }, await b.TitlesAsync());
    }

    [Fact]
    public async Task Concurrent_sync_calls_are_serialized_and_produce_no_duplicates()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("x");

        var results = await Task.WhenAll(a.Sync.SyncAsync(cloud), a.Sync.SyncAsync(cloud), a.Sync.SyncAsync(cloud));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(1, cloud.Uploads);
        Assert.Single(await a.Occasions.GetAllAsync());
    }

    [Fact]
    public async Task A_deleted_cloud_file_re_uploaded_by_a_lagging_device_does_not_erase_the_other_devices_data()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        await b.AddOccasionAsync("R");
        await b.Sync.SyncAsync(cloud);

        await cloud.DeleteAsync(default);
        await a.Sync.SyncAsync(cloud);   // re-uploads X only
        var result = await b.Sync.SyncAsync(cloud);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "R", "X" }, await b.TitlesAsync());
        await a.Sync.SyncAsync(cloud);
        Assert.Equal(new[] { "R", "X" }, await a.TitlesAsync());
    }

    [Fact]
    public async Task Two_service_instances_sharing_a_gate_never_overlap()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("Wedding");
        var gate = new SyncGate();
        var options = new SyncOptions { KdfIterations = MarkdPackage.MinIterations };
        var first = new SyncService(a.Snapshots, a.State, a.Secrets, a.Clock, options, gate);
        var second = new SyncService(a.Snapshots, a.State, a.Secrets, a.Clock, options, gate);

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        cloud.BeforeUpload = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); };

        var sync1 = Task.Run(() => first.SyncAsync(cloud));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var callsWhileHeld = cloud.GetInfoCalls;

        var sync2 = Task.Run(() => second.SyncAsync(cloud));
        await Task.Delay(200);
        Assert.Equal(callsWhileHeld, cloud.GetInfoCalls);

        release.Set();
        var results = await Task.WhenAll(sync1, sync2);

        Assert.All(results, r => Assert.True(r.Outcome is SyncOutcome.Synced or SyncOutcome.NoChanges));
        Assert.True(cloud.GetInfoCalls > callsWhileHeld);
    }

    [Fact]
    public async Task A_choice_is_not_applied_to_a_newer_remote_edit_made_during_the_upload()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        await RenameOnlyAsync(a, "X from A");
        await RenameOnlyAsync(b, "X from B");
        await b.Sync.SyncAsync(cloud);
        var pending = await a.Sync.SyncAsync(cloud);
        Assert.Equal(SyncOutcome.NeedsConflictResolution, pending.Outcome);
        var resolutions = Snap.Pick(pending.Conflicts!, ConflictChoice.Local);

        // while a uploads its choice, b edits X again and wins the race
        cloud.BeforeUpload = () => Task.Run(async () =>
        {
            await RenameOnlyAsync(b, "X from B again");
            await b.Sync.SyncAsync(cloud);
        }).GetAwaiter().GetResult();
        var result = await a.Sync.SyncAsync(cloud, resolutions);

        Assert.Equal(SyncOutcome.NeedsConflictResolution, result.Outcome);
        var conflict = Assert.IsType<OccasionConflict>(Assert.Single(result.Conflicts!));
        Assert.Equal("X from B again", conflict.Remote!.Title);
        Assert.Equal(new[] { "X from A" }, await a.TitlesAsync());
        using var c = new SyncDevice();
        Assert.True((await c.Sync.SyncAsync(cloud)).IsSuccess);
        Assert.Equal(new[] { "X from B again" }, await c.TitlesAsync());   // b's second edit is still in the cloud
    }

    [Fact]
    public async Task Pinning_another_occasion_does_not_conflict_with_an_edit_of_the_old_pin()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var x = await a.AddOccasionAsync("X");
        var y = await a.AddOccasionAsync("Y");
        await a.Occasions.SetPinnedAsync(x.Id);
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        await a.Occasions.SetPinnedAsync(y.Id);   // unpins X as a side effect
        Assert.Equal(SyncOutcome.Synced, (await a.Sync.SyncAsync(cloud)).Outcome);
        var xOnB = (await b.Occasions.GetAllAsync()).Single(o => o.Title == "X");
        xOnB.Notes = "from b";
        await b.Occasions.UpdateAsync(xOnB);
        var result = await b.Sync.SyncAsync(cloud);
        Assert.True((await a.Sync.SyncAsync(cloud)).IsSuccess);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Empty(result.Conflicts ?? []);
        foreach (var device in new[] { a, b })
        {
            var all = await device.Occasions.GetAllAsync();
            Assert.Equal("Y", Assert.Single(all, o => o.IsPinned).Title);
            Assert.Equal("from b", all.Single(o => o.Title == "X").Notes);
        }
    }

    [Fact]
    public async Task Pinning_a_lower_Id_after_a_sync_moved_the_pin_keeps_one_pin()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var x = await a.AddOccasionAsync("X");
        var y = await a.AddOccasionAsync("Y");
        var z = await a.AddOccasionAsync("Z");
        await a.Occasions.SetPinnedAsync(x.Id);
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        await a.Occasions.SetPinnedAsync(z.Id);
        await a.Sync.SyncAsync(cloud);
        Assert.Equal(SyncOutcome.Synced, (await b.Sync.SyncAsync(cloud)).Outcome);   // b receives the pin move X to Z

        var onB = await b.Occasions.GetAllAsync();
        Assert.Equal("Z", Assert.Single(onB, o => o.IsPinned).Title);
        await b.Occasions.SetPinnedAsync(onB.Single(o => o.Title == "Y").Id);

        Assert.Equal("Y", Assert.Single(await b.Occasions.GetAllAsync(), o => o.IsPinned).Title);
    }

    [Fact]
    public async Task Deleting_a_category_does_not_conflict_with_an_edit_of_its_occasion()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var category = await a.Categories.CreateAsync(new Category { Name = "Family" });
        var x = await a.AddOccasionAsync("X");
        x.CategoryId = category.Id;
        await a.Occasions.UpdateAsync(x);
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        await a.Categories.DeleteAsync(category.Id);   // clears X's category as a side effect
        Assert.Equal(SyncOutcome.Synced, (await a.Sync.SyncAsync(cloud)).Outcome);
        var xOnB = (await b.Occasions.GetAllAsync()).Single();
        Assert.NotNull(xOnB.CategoryId);
        xOnB.Notes = "from b";
        await b.Occasions.UpdateAsync(xOnB);
        var result = await b.Sync.SyncAsync(cloud);
        Assert.True((await a.Sync.SyncAsync(cloud)).IsSuccess);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Empty(result.Conflicts ?? []);
        foreach (var device in new[] { a, b })
        {
            var only = Assert.Single(await device.Occasions.GetAllAsync());
            Assert.Equal("from b", only.Notes);
            Assert.Null(only.CategoryId);
            Assert.Empty(await device.Categories.GetAllAsync());
        }
    }

    private static async Task RenameOnlyAsync(SyncDevice device, string title)
    {
        var occasion = (await device.Occasions.GetAllAsync()).Single();
        occasion.Title = title;
        await device.Occasions.UpdateAsync(occasion);
    }

    private static readonly DateOnly March2 = new(2026, 3, 2);

    [Fact]
    public async Task Two_devices_in_different_time_zones_share_one_event_without_conflict()
    {
        using var a = new SyncDevice(zone: Zones.Utc(-5));
        using var b = new SyncDevice(zone: Zones.Utc(9));
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionOnAsync("Birthday", March2);
        await b.AddOccasionOnAsync("Birthday", March2);

        Assert.Equal(SyncOutcome.Synced, (await a.Sync.SyncAsync(cloud)).Outcome);
        var result = await b.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Empty(result.Conflicts ?? []);
        Assert.Equal(March2, b.DayOf(Assert.Single(await b.Occasions.GetAllAsync())));
        await a.Sync.SyncAsync(cloud);
        Assert.Equal(March2, a.DayOf(Assert.Single(await a.Occasions.GetAllAsync())));
    }

    [Fact]
    public async Task An_event_created_in_one_zone_shows_the_same_day_in_another()
    {
        using var a = new SyncDevice(zone: Zones.Utc(9));
        using var b = new SyncDevice(zone: Zones.Utc(-5));
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionOnAsync("Birthday", March2);
        await a.Sync.SyncAsync(cloud);

        await b.Sync.SyncAsync(cloud);

        Assert.Equal(March2, b.DayOf(Assert.Single(await b.Occasions.GetAllAsync())));
    }

    [Fact]
    public async Task A_pure_zone_difference_does_not_reset_milestone_notifications()
    {
        using var a = new SyncDevice(zone: Zones.Utc(9));
        using var b = new SyncDevice(zone: Zones.Utc(-5));
        var cloud = new InMemoryCloudProvider();
        var ao = await a.AddOccasionOnAsync("Birthday", March2);
        await a.Occasions.AddMilestoneAsync(ao.Id, 1000, "1000 days");
        var bo = await b.AddOccasionOnAsync("Birthday", March2);
        var bm = await b.Occasions.AddMilestoneAsync(bo.Id, 1000, "1000 days");   // not reached: a recompute would clear it
        await b.Occasions.MarkMilestoneNotifiedAsync(bm.Id);
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        await a.Sync.SyncAsync(cloud);
        var onA = Assert.Single(await a.Occasions.GetAllAsync());
        onA.Notes = "same day, new note";
        await a.Occasions.UpdateAsync(onA);
        await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, (await b.Sync.SyncAsync(cloud)).Outcome);

        var onB = Assert.Single(await b.Occasions.GetAllAsync());
        Assert.Equal("same day, new note", onB.Notes);
        Assert.True(Assert.Single(onB.Milestones).Notified);
    }

    private static async Task<(SyncDevice A, SyncDevice B, InMemoryCloudProvider Cloud)> SyncedInPragueAsync()
    {
        var a = new SyncDevice(zone: Zones.Utc(1));
        var b = new SyncDevice(zone: Zones.Utc(1));
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionOnAsync("Birthday", March2);   // stored 1 March 23:00 UTC
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        return (a, b, cloud);
    }

    private static async Task<DateOnly?> StoredDayAsync(SyncDevice device) =>
        (await device.Db.Occasions.AsNoTracking().SingleAsync()).AnchorDay;

    [Fact]
    public async Task A_device_that_changed_time_zone_does_not_push_a_day_shift()
    {
        var (a, b, cloud) = await SyncedInPragueAsync();
        using var _ = a; using var __ = b;
        var revision = cloud.Revision;

        a.SwitchZone(Zones.Utc(-5));   // the same instant is 1 March in New York
        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.NoChanges, result.Outcome);
        Assert.Equal(revision, cloud.Revision);
        Assert.Equal(March2, Assert.Single(SyncSnapshotCodec.Decode(cloud.Data!, "pass").Occasions).AnchorDay);
        Assert.Equal(SyncOutcome.NoChanges, (await b.Sync.SyncAsync(cloud)).Outcome);
        Assert.Equal(March2, b.DayOf(Assert.Single(await b.Occasions.GetAllAsync())));
    }

    [Fact]
    public async Task Editing_another_field_after_a_time_zone_change_keeps_the_day()
    {
        var (a, b, cloud) = await SyncedInPragueAsync();
        using var _ = a; using var __ = b;
        a.SwitchZone(Zones.Utc(-5));
        var onA = Assert.Single(await a.Occasions.GetAllAsync());
        onA.Notes = "travelling";
        await a.Occasions.UpdateAsync(onA);   // same anchor instant

        Assert.Equal(SyncOutcome.Synced, (await a.Sync.SyncAsync(cloud)).Outcome);
        await b.Sync.SyncAsync(cloud);

        Assert.Equal(March2, await StoredDayAsync(a));
        var onB = Assert.Single(await b.Occasions.GetAllAsync());
        Assert.Equal("travelling", onB.Notes);
        Assert.Equal(March2, b.DayOf(onB));
    }

    [Fact]
    public async Task Changing_the_date_updates_the_stored_day()
    {
        var (a, b, cloud) = await SyncedInPragueAsync();
        using var _ = a; using var __ = b;
        a.SwitchZone(Zones.Utc(-5));
        var onA = Assert.Single(await a.Occasions.GetAllAsync());
        await a.Occasions.UpdateAsync(new Occasion   // a fresh object, as the edit form sends
        {
            Id = onA.Id, Title = onA.Title, Direction = onA.Direction,
            AnchorDate = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 3, 5), a.Zone),
        });

        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        Assert.Equal(new DateOnly(2026, 3, 5), await StoredDayAsync(a));
        Assert.Equal(new DateOnly(2026, 3, 5), b.DayOf(Assert.Single(await b.Occasions.GetAllAsync())));
    }
}
