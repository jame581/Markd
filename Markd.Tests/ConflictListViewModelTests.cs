using Markd.Core.Services;
using Markd.Core.Sync;
using Markd.ViewModels;
using Xunit;

namespace Markd.Tests;

[Collection(nameof(GlobalCultureCollection))]
public class ConflictListViewModelTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static OccasionDto Occ(string title) => new() { Title = title, Emoji = "X", AnchorDay = new DateOnly(2020, 5, 17), Direction = "Since" };

    private static OccasionConflict OccasionConflictOf(string local, string remote, int localAgeDays = 3, int remoteAgeDays = 1) =>
        new(Guid.NewGuid(), Occ(local), Occ(remote), "Home", "Work", Now.AddDays(-localAgeDays), Now.AddDays(-remoteAgeDays))
        { Signature = "sig-" + local };

    private static ConflictListViewModel Create(params SyncConflict[] conflicts) => new(conflicts, () => Now);

    [Fact]
    public void Header_UsesThePluralFamilyInEnglish()
    {
        using var _ = new CultureScope("en-GB");

        Assert.Equal("1 event changed on both devices", Create(OccasionConflictOf("a", "b")).HeaderText);
        Assert.Equal("2 events changed on both devices",
            Create(OccasionConflictOf("a", "b"), new CategoryConflict(Guid.NewGuid(), new CategoryDto { Name = "c" }, null, Now, Now)).HeaderText);
    }

    [Fact]
    public void Header_UsesTheCzechFamily()
    {
        using var _ = new CultureScope("cs-CZ");

        var four = Enumerable.Range(0, 4).Select(i => (SyncConflict)OccasionConflictOf("a" + i, "b")).ToArray();
        var five = Enumerable.Range(0, 5).Select(i => (SyncConflict)OccasionConflictOf("a" + i, "b")).ToArray();

        Assert.Equal("4 události se změnily na obou zařízeních", Create(four).HeaderText);
        Assert.Equal("5 událostí se změnilo na obou zařízeních", Create(five).HeaderText);
    }

    [Fact]
    public void Rows_ShowTitlesAndKinds()
    {
        using var _ = new CultureScope("en-GB");
        var milestone = new MilestoneConflict(Guid.NewGuid(), new MilestoneDto { Label = "1000 days", ThresholdDays = 1000 }, null, "Wedding", Now, Now);
        var category = new CategoryConflict(Guid.NewGuid(), null, new CategoryDto { Name = "Family" }, Now, Now);

        var vm = Create(OccasionConflictOf("Trip", "Trip 2"), milestone, category);

        Assert.Equal("Trip", vm.Rows[0].Title);
        Assert.Equal("Occasion", vm.Rows[0].KindText);
        Assert.Equal("1000 days · Wedding", vm.Rows[1].Title);
        Assert.Equal("Milestone", vm.Rows[1].KindText);
        Assert.Equal("Family", vm.Rows[2].Title);
        Assert.Equal("Category", vm.Rows[2].KindText);
    }

    [Fact]
    public void Chips_MarkADeletedSide()
    {
        using var _ = new CultureScope("en-GB");
        var conflict = new OccasionConflict(Guid.NewGuid(), null, Occ("Remote title"), null, null, Now.AddDays(-2), Now);

        var row = Create(conflict).Rows[0];

        Assert.Equal("This device: deleted", row.LocalChipText);
        Assert.Equal("Other device", row.RemoteChipText);
        Assert.Equal("Remote title", row.Title);

        var other = new OccasionConflict(Guid.NewGuid(), Occ("Local title"), null, null, null, Now, Now.AddDays(-2));
        var row2 = Create(other).Rows[0];
        Assert.Equal("This device", row2.LocalChipText);
        Assert.Equal("Other device: deleted", row2.RemoteChipText);
        Assert.Equal("Local title", row2.Title);
    }

    [Fact]
    public void Newest_IsMarkedOnTheNewerSideOnly()
    {
        var remoteNewer = Create(OccasionConflictOf("a", "b", localAgeDays: 5, remoteAgeDays: 1)).Rows[0];
        Assert.False(remoteNewer.IsLocalNewer);
        Assert.True(remoteNewer.IsRemoteNewer);

        var localNewer = Create(OccasionConflictOf("a", "b", localAgeDays: 1, remoteAgeDays: 5)).Rows[0];
        Assert.True(localNewer.IsLocalNewer);
        Assert.False(localNewer.IsRemoteNewer);
    }

    [Fact]
    public void Shortcuts_SetEveryRow()
    {
        var vm = Create(OccasionConflictOf("a", "b", 5, 1), OccasionConflictOf("c", "d", 1, 5));

        vm.ChooseAllLocalCommand.Execute(null);
        Assert.All(vm.Rows, r => Assert.Equal(ConflictChoice.Local, r.Choice));

        vm.ChooseAllRemoteCommand.Execute(null);
        Assert.All(vm.Rows, r => Assert.Equal(ConflictChoice.Remote, r.Choice));

        vm.ChooseAllNewestCommand.Execute(null);
        Assert.Equal(ConflictChoice.Remote, vm.Rows[0].Choice);
        Assert.Equal(ConflictChoice.Local, vm.Rows[1].Choice);
    }

    [Fact]
    public void Apply_IsDisabledUntilEveryRowHasAChoice()
    {
        var vm = Create(OccasionConflictOf("a", "b"), OccasionConflictOf("c", "d"));
        Assert.False(vm.ApplyCommand.CanExecute(null));

        vm.Rows[0].ChooseLocalCommand.Execute(null);
        Assert.False(vm.ApplyCommand.CanExecute(null));

        vm.Rows[1].ChooseRemoteCommand.Execute(null);
        Assert.True(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Apply_ProducesOneResolutionPerRowWithTheSignature()
    {
        var first = OccasionConflictOf("a", "b");
        var second = OccasionConflictOf("c", "d");
        var vm = Create(first, second);
        vm.Rows[0].ChooseLocalCommand.Execute(null);
        vm.Rows[1].ChooseRemoteCommand.Execute(null);

        vm.ApplyCommand.Execute(null);
        var result = await vm.Result;

        Assert.NotNull(result);
        Assert.Equal([first.Resolve(ConflictChoice.Local), second.Resolve(ConflictChoice.Remote)], result);
        Assert.Equal("sig-a", result![0].Signature);
    }

    [Fact]
    public async Task Later_YieldsNull()
    {
        var vm = Create(OccasionConflictOf("a", "b"));

        vm.LaterCommand.Execute(null);

        Assert.Null(await vm.Result);
    }

    [Fact]
    public void Compare_ListsBothColumnsWithChangedAge()
    {
        using var _ = new CultureScope("en-GB");
        var row = Create(OccasionConflictOf("Trip", "Trip 2", 3, 1)).Rows[0];
        ConflictCompareViewModel? requested = null;
        row.CompareRequested += (_, vm) => requested = vm;

        row.CompareCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal("Trip", requested!.Local.Single(f => f.Label == "Title").Value);
        Assert.Equal("Trip 2", requested.Remote.Single(f => f.Label == "Title").Value);
        Assert.Equal("Home", requested.Local.Single(f => f.Label == "Category").Value);
        Assert.Equal("3 days ago", requested.Local.Single(f => f.Label == "Changed").Value);
        Assert.Equal("1 day ago", requested.Remote.Single(f => f.Label == "Changed").Value);
        Assert.Equal("Since", requested.Local.Single(f => f.Label == "Direction").Value);
    }

    [Fact]
    public void Compare_DeletedSideHasNoFields()
    {
        var conflict = new OccasionConflict(Guid.NewGuid(), null, Occ("x"), null, null, Now, Now);
        var row = Create(conflict).Rows[0];
        ConflictCompareViewModel? requested = null;
        row.CompareRequested += (_, vm) => requested = vm;

        row.CompareCommand.Execute(null);

        Assert.Empty(requested!.Local);
        Assert.NotEmpty(requested.Remote);
    }
}
