using Markd.Core.Services;
using Markd.Core.Sync;
using Xunit;
using static Markd.Core.Tests.Snap;

namespace Markd.Core.Tests;

public class SyncMergerCascadeTests
{
    /// <summary>Merges once to get the conflicts, then again with the same choice for all of them.</summary>
    private static ExportModel MergeChoosing(ExportModel b, ExportModel l, ExportModel r, ConflictChoice choice) =>
        SyncMerger.Merge(b, l, r, Now, Pick(SyncMerger.Merge(b, l, r, Now).Conflicts, choice)).Merged;

    [Fact]
    public void Milestones_follow_a_deleted_occasion()
    {
        var b = Model(o: [Occ(1)], m: [Ms(1, 1)]);
        var l = Model(o: [Occ(1, deleted: true)], m: [Ms(1, 1)]);   // local deleted the occasion (milestone row left)
        var r = b.CloneModel();

        var merged = SyncMerger.Merge(b, l, r, Now).Merged;

        Assert.DoesNotContain(merged.Milestones, m => m.DeletedAt is null);
    }

    [Fact]
    public void A_milestone_added_on_one_device_and_the_title_edited_on_the_other_is_not_a_conflict()
    {
        var b = Model(o: [Occ(1, "a")]);
        var l = Model(o: [Occ(1, "a")], m: [Ms(1, 1, days: 7)]);
        var r = Model(o: [Occ(1, "renamed")]);

        var result = SyncMerger.Merge(b, l, r, Now);

        Assert.Empty(result.Conflicts);
        Assert.Equal("renamed", result.Merged.Occasions.Single().Title);
        Assert.Single(result.Merged.Milestones.Where(m => m.DeletedAt is null));
    }

    [Fact]
    public void Keeping_an_edited_occasion_against_a_remote_delete_brings_its_milestones_back()
    {
        var b = Model(o: [Occ(1, "a")], m: [Ms(1, 1), Ms(2, 1, days: 200)]);
        var l = Model(o: [Occ(1, "edited")], m: [Ms(1, 1), Ms(2, 1, days: 200)]);
        var r = Model(o: [Occ(1, "a", deleted: true)], m: [Ms(1, 1, deleted: true), Ms(2, 1, days: 200, deleted: true)]);

        var merged = MergeChoosing(b, l, r, ConflictChoice.Local);

        Assert.Equal("edited", merged.Occasions.Single().Title);
        Assert.Equal(2, merged.Milestones.Count(m => m.DeletedAt is null));
    }

    [Fact]
    public void Keeping_the_delete_keeps_the_milestones_deleted()
    {
        var b = Model(o: [Occ(1, "a")], m: [Ms(1, 1)]);
        var l = Model(o: [Occ(1, "edited")], m: [Ms(1, 1)]);
        var r = Model(o: [Occ(1, "a", deleted: true)], m: [Ms(1, 1, deleted: true)]);

        var merged = MergeChoosing(b, l, r, ConflictChoice.Remote);

        Assert.DoesNotContain(merged.Occasions, o => o.DeletedAt is null);
        Assert.DoesNotContain(merged.Milestones, m => m.DeletedAt is null);
    }

    [Fact]
    public void An_occasion_pointing_at_a_deleted_category_becomes_uncategorised()
    {
        var b = Model(c: [Cat(1)], o: [Occ(1)]);
        var l = Model(c: [Cat(1, deleted: true)], o: [Occ(1)]);
        var r = Model(c: [Cat(1)], o: [Occ(1, cat: 1)]);   // remote assigned it to the category local deleted

        var merged = SyncMerger.Merge(b, l, r, Now).Merged;

        Assert.Null(merged.Occasions.Single().CategorySyncId);
    }

    /// <summary>Local deleted category 1 (clearing X and Y), remote renamed it.</summary>
    private static (ExportModel B, ExportModel L, ExportModel R) DeletedHereRenamedThere()
    {
        var later = T0.AddDays(1);
        var b = Model(c: [Cat(1, "Family")], o: [Occ(1, "X", cat: 1), Occ(2, "Y", cat: 1)]);
        var l = Model(c: [Cat(1, "Family", deleted: true)], o: [Occ(1, "X", updated: later), Occ(2, "Y", updated: later)]);
        var r = Model(c: [Cat(1, "Kin", updated: later)], o: [Occ(1, "X", cat: 1), Occ(2, "Y", cat: 1)]);
        return (b, l, r);
    }

    [Fact]
    public void Keeping_a_category_against_its_delete_restores_its_occasions()
    {
        var (b, l, r) = DeletedHereRenamedThere();
        Assert.IsType<CategoryConflict>(Assert.Single(SyncMerger.Merge(b, l, r, Now).Conflicts));

        var merged = MergeChoosing(b, l, r, ConflictChoice.Remote);

        var category = Assert.Single(merged.Categories);
        Assert.Null(category.DeletedAt);
        Assert.Equal("Kin", category.Name);
        Assert.All(merged.Occasions, o => Assert.Equal(G(1), o.CategorySyncId));
    }

    [Fact]
    public void Keeping_the_delete_of_a_category_leaves_its_occasions_uncategorised()
    {
        var (b, l, r) = DeletedHereRenamedThere();

        var merged = MergeChoosing(b, l, r, ConflictChoice.Local);

        Assert.NotNull(Assert.Single(merged.Categories).DeletedAt);
        Assert.All(merged.Occasions, o => Assert.Null(o.CategorySyncId));
    }

    [Fact]
    public void Keeping_a_category_does_not_undo_a_move_to_another_category()
    {
        var later = T0.AddDays(1);
        var b = Model(c: [Cat(1, "Family"), Cat(2, "Work")], o: [Occ(1, "X", cat: 1), Occ(2, "Y", cat: 1)]);
        var l = Model(c: [Cat(1, "Family", deleted: true), Cat(2, "Work")],
            o: [Occ(1, "X", cat: 2, updated: later), Occ(2, "Y", updated: later)]);   // X moved to Work before the delete
        var r = Model(c: [Cat(1, "Kin", updated: later), Cat(2, "Work")], o: [Occ(1, "X", cat: 1), Occ(2, "Y", cat: 1)]);

        var merged = MergeChoosing(b, l, r, ConflictChoice.Remote);

        Assert.Equal(G(2), merged.Occasions.Single(o => o.SyncId == G(1)).CategorySyncId);
        Assert.Equal(G(1), merged.Occasions.Single(o => o.SyncId == G(2)).CategorySyncId);
    }

    [Fact]
    public void Keeping_a_category_does_not_bring_back_an_occasion_the_keeping_side_took_out_of_it()
    {
        var later = T0.AddDays(1);
        var b = Model(c: [Cat(1, "Family")], o: [Occ(1, "X", cat: 1), Occ(2, "Y", cat: 1)]);
        var l = Model(c: [Cat(1, "Family", deleted: true)], o: [Occ(1, "X", updated: later), Occ(2, "Y", updated: later)]);
        var r = Model(c: [Cat(1, "Kin", updated: later)], o: [Occ(1, "X", cat: 1), Occ(2, "Y", updated: later)]);

        var merged = MergeChoosing(b, l, r, ConflictChoice.Remote);

        Assert.Equal(G(1), merged.Occasions.Single(o => o.SyncId == G(1)).CategorySyncId);
        Assert.Null(merged.Occasions.Single(o => o.SyncId == G(2)).CategorySyncId);
    }

    [Fact]
    public void Identical_categories_created_on_both_devices_collapse_and_occasions_are_repointed()
    {
        var b = Model();
        var l = Model(c: [Cat(5, "Family")], o: [Occ(1, cat: 5)]);
        var r = Model(c: [Cat(2, "family")], o: [Occ(2, cat: 2)]);

        var merged = SyncMerger.Merge(b, l, r, Now).Merged;

        Assert.Single(merged.Categories.Where(c => c.DeletedAt is null));
        var keeper = merged.Categories.Single(c => c.DeletedAt is null).SyncId;
        Assert.Equal(G(2), keeper);
        Assert.All(merged.Occasions, o => Assert.Equal(keeper, o.CategorySyncId));
    }

    [Fact]
    public void Categories_that_differ_in_emoji_or_colour_stay_separate()
    {
        var a = Cat(1, "Family"); a.Emoji = "A";
        var c = Cat(2, "Family"); c.Emoji = "B";
        var merged = SyncMerger.Merge(Model(), Model(c: [a]), Model(c: [c]), Now).Merged;
        Assert.Equal(2, merged.Categories.Count(x => x.DeletedAt is null));
    }

    [Fact]
    public void Two_pins_after_merge_leave_only_the_newest()
    {
        var b = Model(o: [Occ(1), Occ(2)]);
        var l = Model(o: [Occ(1, pinned: true, updated: T0.AddDays(1)), Occ(2)]);
        var r = Model(o: [Occ(1), Occ(2, pinned: true, updated: T0.AddDays(3))]);

        var merged = SyncMerger.Merge(b, l, r, Now).Merged;

        var pinned = Assert.Single(merged.Occasions.Where(o => o.IsPinned));
        Assert.Equal(G(2), pinned.SyncId);
    }

    [Fact]
    public void Old_tombstones_are_dropped_and_recent_ones_kept()
    {
        var old = Occ(1, deleted: true); old.DeletedAt = Now.AddDays(-400);
        var recent = Occ(2, deleted: true); recent.DeletedAt = Now.AddDays(-5);
        var b = Model();
        var l = Model(o: [old, recent]);

        var merged = SyncMerger.Merge(b, l, Model(), Now).Merged;

        Assert.Equal(new[] { G(2) }, merged.Occasions.Select(o => o.SyncId));
    }

    [Fact]
    public void A_milestone_edited_remotely_under_an_occasion_deleted_locally_is_not_a_separate_conflict()
    {
        var b = Model(o: [Occ(1)], m: [Ms(1, 1, days: 10)]);
        var l = Model(o: [Occ(1, deleted: true)], m: [Ms(1, 1, days: 10, deleted: true)]);
        var r = Model(o: [Occ(1)], m: [Ms(1, 1, days: 99)]);

        var result = SyncMerger.Merge(b, l, r, Now);

        Assert.Empty(result.Conflicts);
        Assert.DoesNotContain(result.Merged.Milestones, m => m.DeletedAt is null);
    }

    [Fact]
    public void Duplicate_categories_the_user_already_had_are_left_alone()
    {
        var b = Model(c: [Cat(1, "Same"), Cat(2, "Same")]);
        var merged = SyncMerger.Merge(b, b.CloneModel(), b.CloneModel(), Now).Merged;
        Assert.Equal(2, merged.Categories.Count(c => c.DeletedAt is null));
    }

    [Fact]
    public void Merge_does_not_change_its_inputs_when_cascading()
    {
        var b = Model(c: [Cat(1)], o: [Occ(1)]);
        var l = Model(c: [Cat(1, deleted: true)], o: [Occ(1)]);
        var r = Model(c: [Cat(1)], o: [Occ(1, cat: 1)]);
        SyncMerger.Merge(b, l, r, Now);
        Assert.Equal(G(1), r.Occasions[0].CategorySyncId);
    }
}
