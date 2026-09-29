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
        Assert.Equal("mine", ((OccasionDto)conflict.Local!).Title);
        Assert.Equal(ConflictChoice.Remote, conflict.Newest);

        var mine = SyncMerger.Merge(b, l, r, Now, new Dictionary<Guid, ConflictChoice> { [G(1)] = ConflictChoice.Local });
        var theirs = SyncMerger.Merge(b, l, r, Now, new Dictionary<Guid, ConflictChoice> { [G(1)] = ConflictChoice.Remote });
        Assert.Empty(mine.Conflicts);
        Assert.Equal("mine", Titles(mine.Merged));
        Assert.Equal("theirs", Titles(theirs.Merged));
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
    public void Edit_versus_delete_is_a_conflict_with_the_deleted_side_shown_as_null()
    {
        var b = Model(o: [Occ(1, "a")]);
        var edited = Model(o: [Occ(1, "edited")]);
        var deleted = Model(o: [Occ(1, "a", deleted: true)]);

        var result = SyncMerger.Merge(b, edited, deleted, Now);

        var conflict = Assert.Single(result.Conflicts);
        Assert.NotNull(conflict.Local);
        Assert.Null(conflict.Remote);
        var keepEdit = SyncMerger.Merge(b, edited, deleted, Now, new Dictionary<Guid, ConflictChoice> { [G(1)] = ConflictChoice.Local });
        var keepDelete = SyncMerger.Merge(b, edited, deleted, Now, new Dictionary<Guid, ConflictChoice> { [G(1)] = ConflictChoice.Remote });
        Assert.Equal("edited", Titles(keepEdit.Merged));
        Assert.Equal("", Titles(keepDelete.Merged));
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
        var result = SyncMerger.Merge(b, l, r, Now, new Dictionary<Guid, ConflictChoice> { [G(1)] = ConflictChoice.Local });
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
}
