using Markd.Core.Sync;
using Xunit;

namespace Markd.Core.Tests;

public sealed class LocalFolderCloudProviderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "markd-localfolder-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
            else if (File.Exists(_folder)) File.Delete(_folder);
        }
        catch (IOException) { }
    }

    [Fact]
    public async Task Missing_folder_and_file_report_no_remote_and_upload_creates_the_folder()
    {
        var provider = new LocalFolderCloudProvider(_folder);

        Assert.Null(await provider.GetInfoAsync(default));
        Assert.Null(await provider.DownloadAsync(default));

        var revision = await provider.UploadAsync([1, 2, 3], null, default);

        var file = await provider.DownloadAsync(default);
        Assert.Equal(new byte[] { 1, 2, 3 }, file!.Data);
        Assert.Equal(revision, file.Revision);
        Assert.Equal(revision, (await provider.GetInfoAsync(default))!.Revision);
        Assert.Equal("local-folder", provider.Id);
        Assert.Equal(Path.GetFullPath(_folder), provider.AccountName);
    }

    [Fact]
    public async Task Create_only_upload_rejects_an_existing_file()
    {
        var provider = new LocalFolderCloudProvider(_folder);
        await provider.UploadAsync([1], null, default);

        await Assert.ThrowsAsync<RevisionConflictException>(() => provider.UploadAsync([2], null, default));
    }

    [Fact]
    public async Task Conditional_upload_rejects_a_stale_revision_and_a_missing_file()
    {
        var provider = new LocalFolderCloudProvider(_folder);
        var first = await provider.UploadAsync([1], null, default);
        await provider.UploadAsync([2], first, default);

        await Assert.ThrowsAsync<RevisionConflictException>(() => provider.UploadAsync([3], first, default));
        Assert.Equal(new byte[] { 2 }, (await provider.DownloadAsync(default))!.Data);

        await provider.DeleteAsync(default);
        await Assert.ThrowsAsync<RevisionConflictException>(() => provider.UploadAsync([4], first, default));
    }

    [Fact]
    public async Task Revision_changes_on_every_upload()
    {
        var provider = new LocalFolderCloudProvider(_folder);
        var r1 = await provider.UploadAsync([1], null, default);
        var r2 = await provider.UploadAsync([2], r1, default);
        var r3 = await provider.UploadAsync([3], r2, default);

        Assert.Equal(3, new[] { r1, r2, r3 }.Distinct().Count());
    }

    [Fact]
    public async Task Revision_includes_the_file_path_so_folders_never_collide()
    {
        var other = _folder + "-other";
        try
        {
            var r1 = await new LocalFolderCloudProvider(_folder).UploadAsync([1], null, default);
            var r2 = await new LocalFolderCloudProvider(other).UploadAsync([1], null, default);
            Assert.NotEqual(r1, r2);
        }
        finally { Directory.Delete(other, recursive: true); }
    }

    [Fact]
    public async Task Two_provider_instances_on_one_folder_see_each_others_writes()
    {
        var a = new LocalFolderCloudProvider(_folder);
        var b = new LocalFolderCloudProvider(_folder);

        var r1 = await a.UploadAsync([1], null, default);
        Assert.Equal(r1, (await b.GetInfoAsync(default))!.Revision);

        var r2 = await b.UploadAsync([2], r1, default);
        Assert.Equal(new byte[] { 2 }, (await a.DownloadAsync(default))!.Data);
        await Assert.ThrowsAsync<RevisionConflictException>(() => a.UploadAsync([3], r1, default));
        Assert.Equal(r2, (await a.GetInfoAsync(default))!.Revision);
    }

    [Fact]
    public async Task Delete_removes_the_file_and_is_idempotent()
    {
        var provider = new LocalFolderCloudProvider(_folder);
        await provider.UploadAsync([1], null, default);

        await provider.DeleteAsync(default);
        await provider.DeleteAsync(default);

        Assert.Null(await provider.GetInfoAsync(default));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task The_lock_is_released_after_a_conflict()
    {
        var provider = new LocalFolderCloudProvider(_folder);
        await provider.UploadAsync([1], null, default);
        await Assert.ThrowsAsync<RevisionConflictException>(() => provider.UploadAsync([2], null, default));

        Assert.Empty(Directory.GetFiles(_folder).Where(f => f.EndsWith(".lock")));
        await provider.UploadAsync([3], (await provider.GetInfoAsync(default))!.Revision, default);
    }

    [Fact]
    public async Task A_held_lock_surfaces_as_offline_after_a_bounded_wait()
    {
        var provider = new LocalFolderCloudProvider(_folder);
        await provider.UploadAsync([1], null, default);
        using var held = new FileStream(Path.Combine(_folder, "markd-sync.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<SyncOfflineException>(() => provider.GetInfoAsync(default));
    }

    [Fact]
    public async Task A_folder_path_that_is_a_file_surfaces_as_offline()
    {
        File.WriteAllText(_folder, "x");
        var provider = new LocalFolderCloudProvider(_folder);

        await Assert.ThrowsAsync<SyncOfflineException>(() => provider.UploadAsync([1], null, default));
    }

    [Fact]
    public async Task Two_devices_converge_through_a_local_folder()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var folder = new LocalFolderCloudProvider(_folder);
        await a.AddOccasionAsync("Wedding");
        await a.Sync.SyncAsync(folder);

        await b.Sync.SyncAsync(folder);
        await b.AddOccasionAsync("Birthday");
        await b.Sync.SyncAsync(folder);
        await a.Sync.SyncAsync(folder);

        Assert.Equal(new[] { "Birthday", "Wedding" }, await a.TitlesAsync());
        Assert.Equal(new[] { "Birthday", "Wedding" }, await b.TitlesAsync());
    }
}
