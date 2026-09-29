namespace Markd.Core.Sync
{
    public sealed record RemoteInfo(string Revision, DateTime ModifiedUtc);

    public sealed record RemoteFile(byte[] Data, string Revision);

    /// <summary>One cloud location holding one file. Implementations live in the app project (they need sign-in).</summary>
    public interface ICloudSyncProvider
    {
        string Id { get; }

        /// <summary>Cheap metadata call. Null when no file exists yet.</summary>
        Task<RemoteInfo?> GetInfoAsync(CancellationToken ct);

        Task<RemoteFile?> DownloadAsync(CancellationToken ct);

        /// <summary>
        /// expectedRevision null means the file must not exist yet. Throws <see cref="RevisionConflictException"/>
        /// when the remote changed since that revision. Returns the new revision.
        /// </summary>
        Task<string> UploadAsync(byte[] data, string? expectedRevision, CancellationToken ct);

        Task DeleteAsync(CancellationToken ct);
    }

    public sealed class RevisionConflictException : Exception
    {
        public RevisionConflictException() : base("The remote file changed since it was read.") { }
    }

    /// <summary>Sign-in expired or was revoked. Providers translate their auth errors to this.</summary>
    public sealed class SyncAuthException : Exception
    {
        public SyncAuthException(string? message = null, Exception? inner = null) : base(message ?? "Sign-in is required.", inner) { }
    }

    /// <summary>No network, DNS failure or timeout. Providers translate transport errors to this.</summary>
    public sealed class SyncOfflineException : Exception
    {
        public SyncOfflineException(string? message = null, Exception? inner = null) : base(message ?? "The network is unavailable.", inner) { }
    }
}
