using Markd.Core.Services;
using Markd.Core.Sync;
using Xunit;
using static Markd.Core.Tests.Snap;

namespace Markd.Core.Tests;

public class SyncMergerTests
{
    private static string Titles(ExportModel m) =>
        string.Join(",", m.Occasions.Where(o => o.DeletedAt is null).OrderBy(o => o.SyncId).Select(o => o.Title));

    [Fact]
    public void Nothing_changed_keeps_the_record()
    {
        var b = Model(o: [Occ(1, "a")]);
        var r = SyncMerger.Merge(b, b.CloneModel(), b.CloneModel(), Now);
        Assert.Empty(r.Conflicts);
        Assert.Equal("a", Titles(r.Merged));
    }

    [Fact]
    public void A_change_on_one_side_wins()
    {
        var b = Model(o: [Occ(1, "a")]);
        var l = Model(o: [Occ(1, "local")]);
        var same = b.CloneModel();

        Assert.Equal("local", Titles(SyncMerger.Merge(b, l, same, Now).Merged));
        Assert.Equal("local", Titles(SyncMerger.Merge(b, same, l, Now).Merged));
    }

    [Fact]
    public void The_same_change_on_both_sides_is_not_a_conflict()
    {
        var b = Model(o: [Occ(1, "a")]);
        var l = Model(o: [Occ(1, "x", updated: T0.AddDays(1))]);
        var r = Model(o: [Occ(1, "x", updated: T0.AddDays(2))]);
        var result = SyncMerger.Merge(b, l, r, Now);
        Assert.Empty(result.Conflicts);
        Assert.Equal("x", Titles(result.Merged));
    }

    [Fact]
    public void Different_changes_on_both_sides_conflict_and_can_be_resolved_either_way()
    {
        var b = Model(o: [Occ(1, "a")]);
        var l = Model(o: [Occ(1, "mine", updated: T0.AddDays(1))]);
        var r = Model(o: [Occ(1, "theirs", updated: T0.AddDays(2))]);

        var first = SyncMerger.Merge(b, l, r, Now);
        var conflict = Assert.Single(first.Conflicts);
        Assert.Equal(SyncRecordKind.Occasion, conflict.Kind);
        Assert.Equal(G(1), conflict.SyncId);
        Assert.Equal("mine", ((OccasionConflict)conflict).Local!.Title);
        Assert.Equal(ConflictChoice.Remote, conflict.Newest);

        var mine = SyncMerger.Merge(b, l, r, Now, Pick(first.Conflicts, ConflictChoice.Local));
        var theirs = SyncMerger.Merge(b, l, r, Now, Pick(first.Conflicts, ConflictChoice.Remote));
        Assert.Empty(mine.Conflicts);
        Assert.Equal("mine", Titles(mine.Merged));
        Assert.Equal("theirs", Titles(theirs.Merged));
    }

    [Fact]
    public void A_resolution_for_the_same_versions_applies()
    {
        var b = Model(o: [Occ(1, "a")]);
        var l = Model(o: [Occ(1, "mine", updated: T0.AddDays(1))]);
        var r = Model(o: [Occ(1, "theirs", updated: T0.AddDays(2))]);
        var first = SyncMerger.Merge(b, l, r, Now);

        var result = SyncMerger.Merge(b, l.CloneModel(), r.CloneModel(), Now, Pick(first.Conflicts, ConflictChoice.Remote));

        Assert.Empty(result.Conflicts);
        Assert.Equal("theirs", Titles(result.Merged));
    }

    [Fact]
    public void A_resolution_for_a_remote_that_changed_again_is_asked_again()
    {
        var b = Model(o: [Occ(1, "a")]);
        var l = Model(o: [Occ(1, "mine", updated: T0.AddDays(1))]);
        var remoteV1 = Model(o: [Occ(1, "theirs", updated: T0.AddDays(2))]);
        var remoteV2 = Model(o: [Occ(1, "theirs again", updated: T0.AddDays(3))]);
        var first = SyncMerger.Merge(b, l, remoteV1, Now);

        var result = SyncMerger.Merge(b, l, remoteV2, Now, Pick(first.Conflicts, ConflictChoice.Local));

        var conflict = Assert.IsType<OccasionConflict>(Assert.Single(result.Conflicts));
        Assert.Equal(G(1), conflict.SyncId);
        Assert.Equal("theirs again", conflict.Remote!.Title);
        Assert.Equal("mine", Titles(result.Merged));
        Assert.NotEqual(Assert.Single(first.Conflicts).Signature, conflict.Signature);
    }

    [Fact]
    public void A_resolution_after_a_local_edit_is_asked_again()
    {
        var b = Model(o: [Occ(1, "a")]);
        var localV1 = Model(o: [Occ(1, "mine", updated: T0.AddDays(1))]);
        var localV2 = Model(o: [Occ(1, "mine, edited while asked", updated: T0.AddDays(3))]);
        var r = Model(o: [Occ(1, "theirs", updated: T0.AddDays(2))]);
        var first = SyncMerger.Merge(b, localV1, r, Now);

        var result = SyncMerger.Merge(b, localV2, r, Now, Pick(first.Conflicts, ConflictChoice.Remote));

        var conflict = Assert.IsType<OccasionConflict>(Assert.Single(result.Conflicts));
        Assert.Equal("mine, edited while asked", conflict.Local!.Title);
        Assert.Equal("mine, edited while asked", Titles(result.Merged));
        Assert.NotEqual(Assert.Single(first.Conflicts).Signature, conflict.Signature);
    }

    [Fact]
    public void A_delete_beats_an_unchanged_record_in_both_directions()
    {
        var b = Model(o: [Occ(1, "a")]);
        var deleted = Model(o: [Occ(1, "a", deleted: true)]);

        var r1 = SyncMerger.Merge(b, deleted, b.CloneModel(), Now);
        var r2 = SyncMerger.Merge(b, b.CloneModel(), deleted, Now);

        Assert.Equal("", Titles(r1.Merged));
        Assert.Equal("", Titles(r2.Merged));
        Assert.NotNull(Assert.Single(r1.Merged.Occasions).DeletedAt); // tombstone kept so it can propagate
    }

    [Fact]
    public void An_occasion_conflict_names_the_category_on_each_side()
    {
        var b = Model(c: [Cat(10, "Work"), Cat(11, "Home")], o: [Occ(1, "a", cat: 10)]);
        var l = Model(c: [Cat(10, "Work"), Cat(11, "Home")], o: [Occ(1, "mine", cat: 10, updated: T0.AddDays(1))]);
        var r = Model(c: [Cat(10, "Work"), Cat(11, "Home")], o: [Occ(1, "theirs", cat: 11, updated: T0.AddDays(2))]);

        var conflict = Assert.IsType<OccasionConflict>(Assert.Single(SyncMerger.Merge(b, l, r, Now).Conflicts));

        Assert.Equal("Work", conflict.LocalCategoryName);
        Assert.Equal("Home", conflict.RemoteCategoryName);
    }

    [Fact]
    public void A_milestone_conflict_names_its_occasion()
    {
        var b = Model(o: [Occ(1, "Wedding")], m: [Ms(5, 1, label: "a")]);
        var l = Model(o: [Occ(1, "Wedding")], m: [Ms(5, 1, label: "mine", updated: T0.AddDays(1))]);
        var r = Model(o: [Occ(1, "Wedding")], m: [Ms(5, 1, label: "theirs", updated: T0.AddDays(2))]);

        var conflict = Assert.IsType<MilestoneConflict>(Assert.Single(SyncMerger.Merge(b, l, r, Now).Conflicts));

        Assert.Equal("Wedding", conflict.OccasionTitle);
        Assert.Equal("mine", conflict.Local!.Label);
    }

    [Fact]
    public void Edit_versus_delete_is_a_conflict_with_the_deleted_side_shown_as_null()
    {
        var b = Model(o: [Occ(1, "a")]);
        var edited = Model(o: [Occ(1, "edited")]);
        var deleted = Model(o: [Occ(1, "a", deleted: true)]);

        var result = SyncMerger.Merge(b, edited, deleted, Now);

        var conflict = Assert.IsType<OccasionConflict>(Assert.Single(result.Conflicts));
        Assert.NotNull(conflict.Local);
        Assert.Null(conflict.Remote);
        var keepEdit = SyncMerger.Merge(b, edited, deleted, Now, Pick(result.Conflicts, ConflictChoice.Local));
        var keepDelete = SyncMerger.Merge(b, edited, deleted, Now, Pick(result.Conflicts, ConflictChoice.Remote));
        Assert.Equal("edited", Titles(keepEdit.Merged));
        Assert.Equal("", Titles(keepDelete.Merged));
    }

    [Fact]
    public void Without_a_base_a_local_delete_against_a_remote_live_record_is_a_conflict()
    {
        // A shared SyncId with no base means both sides once held the record (reconnect, provider switch, new epoch).
        var deleted = Model(o: [Occ(1, "a", deleted: true)]);
        var live = Model(o: [Occ(1, "a")]);

        var result = SyncMerger.Merge(null, deleted, live, Now);

        var conflict = Assert.IsType<OccasionConflict>(Assert.Single(result.Conflicts));
        Assert.Equal(G(1), conflict.SyncId);
        Assert.Null(conflict.Local);
        Assert.NotNull(conflict.Remote);
        var keepDelete = SyncMerger.Merge(null, deleted, live, Now, Pick(result.Conflicts, ConflictChoice.Local));
        var keepRecord = SyncMerger.Merge(null, deleted, live, Now, Pick(result.Conflicts, ConflictChoice.Remote));
        Assert.Empty(keepDelete.Conflicts);
        Assert.Equal("", Titles(keepDelete.Merged));
        Assert.Equal("a", Titles(keepRecord.Merged));
    }

    [Fact]
    public void Without_a_base_a_local_live_record_against_a_remote_delete_is_a_conflict()
    {
        var live = Model(o: [Occ(1, "a")]);
        var deleted = Model(o: [Occ(1, "a", deleted: true)]);

        var result = SyncMerger.Merge(null, live, deleted, Now);

        var conflict = Assert.IsType<OccasionConflict>(Assert.Single(result.Conflicts));
        Assert.NotNull(conflict.Local);
        Assert.Null(conflict.Remote);
        var keepRecord = SyncMerger.Merge(null, live, deleted, Now, Pick(result.Conflicts, ConflictChoice.Local));
        var keepDelete = SyncMerger.Merge(null, live, deleted, Now, Pick(result.Conflicts, ConflictChoice.Remote));
        Assert.Empty(keepRecord.Conflicts);
        Assert.Equal("a", Titles(keepRecord.Merged));
        Assert.Equal("", Titles(keepDelete.Merged));
    }

    [Fact]
    public void Without_a_base_a_delete_against_a_missing_record_is_a_plain_delete()
    {
        var deleted = Model(o: [Occ(1, "a", deleted: true)]);

        var result = SyncMerger.Merge(null, deleted, Model(), Now);

        Assert.Empty(result.Conflicts);
        Assert.NotNull(Assert.Single(result.Merged.Occasions).DeletedAt);
    }

    [Fact]
    public void Without_a_base_one_decision_on_a_deleted_occasion_settles_its_milestones()
    {
        var deleted = Model(o: [Occ(1, "a", deleted: true)], m: [Ms(2, 1, deleted: true)]);
        var live = Model(o: [Occ(1, "a")], m: [Ms(2, 1)]);

        var pending = SyncMerger.Merge(null, deleted, live, Now);
        var keepRecord = SyncMerger.Merge(null, deleted, live, Now, Pick(pending.Conflicts, ConflictChoice.Remote));
        var keepDelete = SyncMerger.Merge(null, deleted, live, Now, Pick(pending.Conflicts, ConflictChoice.Local));

        Assert.Equal(G(1), Assert.Single(pending.Conflicts).SyncId);
        Assert.Empty(keepRecord.Conflicts);
        Assert.Null(Assert.Single(keepRecord.Merged.Milestones).DeletedAt);
        Assert.Empty(keepDelete.Conflicts);
        Assert.NotNull(Assert.Single(keepDelete.Merged.Milestones).DeletedAt);

        var mirrored = SyncMerger.Merge(null, live, deleted, Now);
        var mirroredKeep = SyncMerger.Merge(null, live, deleted, Now, Pick(mirrored.Conflicts, ConflictChoice.Local));
        Assert.Equal(G(1), Assert.Single(mirrored.Conflicts).SyncId);
        Assert.Empty(mirroredKeep.Conflicts);
        Assert.Null(Assert.Single(mirroredKeep.Merged.Milestones).DeletedAt);
    }

    [Fact]
    public void New_records_on_either_side_are_added()
    {
        var b = Model(o: [Occ(1, "a")]);
        var l = Model(o: [Occ(1, "a"), Occ(2, "local-new")]);
        var r = Model(o: [Occ(1, "a"), Occ(3, "remote-new")]);
        Assert.Equal("a,local-new,remote-new", Titles(SyncMerger.Merge(b, l, r, Now).Merged));
    }

    [Fact]
    public void A_record_missing_from_remote_but_in_base_counts_as_deleted()
    {
        var b = Model(o: [Occ(1, "a")]);
        var result = SyncMerger.Merge(b, b.CloneModel(), Model(), Now);
        Assert.Equal("", Titles(result.Merged));
    }

    [Fact]
    public void Inputs_are_not_modified()
    {
        var b = Model(o: [Occ(1, "a")]);
        var l = Model(o: [Occ(1, "mine")]);
        var r = Model(o: [Occ(1, "a", deleted: true)]);
        var pending = SyncMerger.Merge(b, l, r, Now);
        var result = SyncMerger.Merge(b, l, r, Now, Pick(pending.Conflicts, ConflictChoice.Local));
        Assert.Equal("mine", l.Occasions[0].Title);
        Assert.Null(l.Occasions[0].DeletedAt);
        Assert.Equal("a", b.Occasions[0].Title);
        Assert.Null(b.Occasions[0].DeletedAt);
        Assert.Equal("a", r.Occasions[0].Title);
        Assert.NotNull(r.Occasions[0].DeletedAt);
        var merged = Assert.Single(result.Merged.Occasions);
        Assert.NotSame(l.Occasions[0], merged);
        Assert.NotSame(r.Occasions[0], merged);
        Assert.NotSame(b.Occasions[0], merged);
    }

    [Fact]
    public void Without_conflicts_the_result_does_not_depend_on_which_side_is_local()
    {
        var b = Model(c: [Cat(1, "c")], o: [Occ(1, "a", cat: 1), Occ(2, "b")], m: [Ms(1, 1)]);
        var l = Model(c: [Cat(1, "c")], o: [Occ(1, "a2", cat: 1), Occ(2, "b", deleted: true), Occ(4, "n")], m: [Ms(1, 1), Ms(3, 4)]);
        var r = Model(c: [Cat(1, "c2")], o: [Occ(1, "a", cat: 1), Occ(2, "b"), Occ(5, "m")], m: [Ms(1, 1, days: 200)]);

        var ab = SyncMerger.Merge(b, l, r, Now);
        var ba = SyncMerger.Merge(b, r, l, Now);

        Assert.Empty(ab.Conflicts);
        Assert.Equal(SyncContent.Fingerprint(ab.Merged), SyncContent.Fingerprint(ba.Merged));
    }

    [Fact]
    public void Fingerprint_ignores_timestamps_notified_and_tombstones()
    {
        var a = Model(o: [Occ(1, "a", updated: T0), Occ(2, "gone", deleted: true)]);
        var b = Model(o: [Occ(1, "a", updated: T0.AddDays(5))]);
        Assert.Equal(SyncContent.Fingerprint(a), SyncContent.Fingerprint(b));
        var c = Model(o: [Occ(1, "different")]);
        Assert.NotEqual(SyncContent.Fingerprint(a), SyncContent.Fingerprint(c));
    }

    [Fact]
    public void Tombstones_are_kept_for_a_year()
    {
        Assert.Equal(TimeSpan.FromDays(365), SyncMerger.TombstoneRetention);

        var recent = Occ(1, "recent", deleted: true); recent.DeletedAt = Now.AddDays(-200);
        var old = Occ(2, "old", deleted: true); old.DeletedAt = Now.AddDays(-366);
        var local = Model(o: [recent, old]);

        var merged = SyncMerger.Merge(null, local, SyncMerger.Empty(), Now).Merged;

        Assert.Equal([G(1)], merged.Occasions.Select(o => o.SyncId));
    }
}
