using Markd.Core.Sync;

namespace Markd.Core.Tests;

/// <summary>A fake cloud with real revision semantics, shared by several simulated devices.</summary>
internal sealed class InMemoryCloudProvider : ICloudSyncProvider
{
    private byte[]? _data;
    private int _version;

    public string Id => "test";
    public bool Offline { get; set; }
    public bool AuthExpired { get; set; }
    public int Uploads { get; private set; }
    public int Downloads { get; private set; }

    /// <summary>Runs just before an upload is checked, to simulate another device writing first.</summary>
    public Action? BeforeUpload { get; set; }

    /// <summary>Runs once, when a download starts, to simulate the user editing while a sync is running.</summary>
    public Action? OnDownload { get; set; }

    public byte[]? Data => _data;
    public string? Revision => _data is null ? null : _version.ToString();

    private void Guard()
    {
        if (Offline) throw new SyncOfflineException();
        if (AuthExpired) throw new SyncAuthException();
    }

    public Task<RemoteInfo?> GetInfoAsync(CancellationToken ct)
    {
        Guard();
        return Task.FromResult<RemoteInfo?>(_data is null ? null : new RemoteInfo(Revision!, DateTime.UtcNow));
    }

    public Task<RemoteFile?> DownloadAsync(CancellationToken ct)
    {
        Guard();
        var hook = OnDownload;
        OnDownload = null;
        hook?.Invoke();
        Downloads++;
        return Task.FromResult<RemoteFile?>(_data is null ? null : new RemoteFile(_data.ToArray(), Revision!));
    }

    public Task<string> UploadAsync(byte[] data, string? expectedRevision, CancellationToken ct)
    {
        Guard();
        var hook = BeforeUpload;
        BeforeUpload = null;
        hook?.Invoke();
        if (expectedRevision != Revision)
            throw new RevisionConflictException();

        _data = data.ToArray();
        _version++;
        Uploads++;
        return Task.FromResult(Revision!);
    }

    public Task DeleteAsync(CancellationToken ct)
    {
        Guard();
        _data = null;
        return Task.CompletedTask;
    }

    /// <summary>Replace the remote content directly, as another device (or the user) would.</summary>
    public void Put(byte[] data) { _data = data.ToArray(); _version++; }
}
