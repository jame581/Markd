using Markd.Core.Sync;
using Xunit;
using static Markd.Core.Tests.Snap;

namespace Markd.Core.Tests;

public class FirstSyncAlignerTests
{
    [Fact]
    public void The_same_event_typed_on_both_devices_becomes_one_record()
    {
        var l = Model(c: [Cat(10, "Family")], o: [Occ(11, "Wedding", cat: 10)], m: [Ms(12, 11, days: 100, label: "100 days")]);
        var r = Model(c: [Cat(20, "Family")], o: [Occ(21, "Wedding", cat: 20)], m: [Ms(22, 21, days: 100, label: "100 days")]);

        var result = SyncMerger.Merge(null, l, r, Now);

        Assert.Empty(result.Conflicts);
        Assert.Single(result.Merged.Categories.Where(c => c.DeletedAt is null));
        Assert.Single(result.Merged.Occasions.Where(o => o.DeletedAt is null));
        Assert.Single(result.Merged.Milestones.Where(m => m.DeletedAt is null));
        Assert.Equal(G(20), result.SyncIdRemaps[G(10)]);
        Assert.Equal(G(21), result.SyncIdRemaps[G(11)]);
        Assert.Equal(G(22), result.SyncIdRemaps[G(12)]);
    }

    [Fact]
    public void Case_and_spacing_differences_still_match_but_surface_as_conflicts_to_decide()
    {
        var l = Model(c: [Cat(10, "Family")], o: [Occ(11, "Wedding", cat: 10)], m: [Ms(12, 11, days: 100, label: "100 days")]);
        var r = Model(c: [Cat(20, "family")], o: [Occ(21, "wedding ", cat: 20)], m: [Ms(22, 21, days: 100, label: "100 DAYS")]);

        var result = SyncMerger.Merge(null, l, r, Now);

        Assert.Equal(3, result.Conflicts.Count);   // one per matched record, never a duplicate
        Assert.Single(result.Merged.Categories.Where(c => c.DeletedAt is null));
        Assert.Single(result.Merged.Occasions.Where(o => o.DeletedAt is null));
        Assert.Single(result.Merged.Milestones.Where(m => m.DeletedAt is null));
    }

    [Theory]
    [InlineData("🎉", null, "🎂", null)]
    [InlineData(null, "#FF0000", null, "#00FF00")]
    [InlineData("🎉", null, null, null)]
    public void Categories_with_the_same_name_but_another_emoji_or_colour_stay_separate(
        string? localEmoji, string? localColor, string? remoteEmoji, string? remoteColor)
    {
        var lc = Cat(10, "Family"); lc.Emoji = localEmoji; lc.ColorHex = localColor;
        var rc = Cat(20, "Family"); rc.Emoji = remoteEmoji; rc.ColorHex = remoteColor;

        var result = SyncMerger.Merge(null, Model(c: [lc]), Model(c: [rc]), Now);

        Assert.Empty(result.Conflicts);
        Assert.Empty(result.SyncIdRemaps);
        Assert.Equal(new[] { G(10), G(20) }, result.Merged.Categories.Where(c => c.DeletedAt is null).Select(c => c.SyncId).Order());
    }

    [Fact]
    public void Categories_with_the_same_emoji_and_colour_match_despite_name_case()
    {
        var lc = Cat(10, "Family"); lc.Emoji = "🎉"; lc.ColorHex = "#FF0000";
        var rc = Cat(20, " family"); rc.Emoji = "🎉"; rc.ColorHex = "#FF0000";

        var result = SyncMerger.Merge(null, Model(c: [lc]), Model(c: [rc]), Now);

        Assert.Equal(G(20), result.SyncIdRemaps[G(10)]);
        Assert.Single(result.Merged.Categories.Where(c => c.DeletedAt is null));
    }

    [Fact]
    public void Different_events_are_all_kept()
    {
        var l = Model(o: [Occ(1, "A")]);
        var r = Model(o: [Occ(2, "B")]);
        var result = SyncMerger.Merge(null, l, r, Now);
        Assert.Equal(2, result.Merged.Occasions.Count(o => o.DeletedAt is null));
        Assert.Empty(result.SyncIdRemaps);
    }

    [Fact]
    public void Same_title_but_different_date_is_not_the_same_event()
    {
        var l = Model(o: [Occ(1, "Birthday", anchor: T0)]);
        var r = Model(o: [Occ(2, "Birthday", anchor: T0.AddDays(1))]);
        Assert.Equal(2, SyncMerger.Merge(null, l, r, Now).Merged.Occasions.Count);
    }

    [Fact]
    public void Matched_records_that_differ_in_notes_are_a_conflict_the_user_can_decide()
    {
        var l = Model(o: [Occ(1, "Trip", notes: "mine")]);
        var r = Model(o: [Occ(2, "Trip", notes: "theirs")]);

        var result = SyncMerger.Merge(null, l, r, Now);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(G(2), conflict.SyncId);
    }

    [Fact]
    public void Duplicates_on_one_side_pair_up_one_to_one()
    {
        var l = Model(o: [Occ(1, "Same"), Occ(2, "Same")]);
        var r = Model(o: [Occ(3, "Same")]);
        var result = SyncMerger.Merge(null, l, r, Now);
        Assert.Equal(2, result.Merged.Occasions.Count(o => o.DeletedAt is null));
    }

    [Fact]
    public void With_a_base_nothing_is_aligned()
    {
        var b = Model();
        var l = Model(o: [Occ(1, "Wedding")]);
        var r = Model(o: [Occ(2, "Wedding")]);
        var result = SyncMerger.Merge(b, l, r, Now);
        Assert.Empty(result.SyncIdRemaps);
        Assert.Equal(2, result.Merged.Occasions.Count);
    }

    [Fact]
    public void Inputs_are_not_modified()
    {
        var l = Model(o: [Occ(1, "Wedding")]);
        var r = Model(o: [Occ(2, "Wedding")]);
        SyncMerger.Merge(null, l, r, Now);
        Assert.Equal(G(1), l.Occasions[0].SyncId);
    }
}
