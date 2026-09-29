namespace Markd.Core.Services
{
    public interface ISnapshotService
    {
        /// <summary>Tombstones included, Notified always false.</summary>
        Task<ExportModel> ReadForSyncAsync();

        Task ApplyAsync(ExportModel target, SnapshotApplyOptions options);

        /// <summary>Hard-deletes tombstones older than the cutoff. Returns rows removed.</summary>
        Task<int> PurgeTombstonesAsync(DateTime olderThanUtc);
    }

    public sealed record SnapshotApplyOptions(
        bool TakeNotifiedFromTarget,
        bool ApplySettings = false,
        IReadOnlyDictionary<Guid, Guid>? SyncIdRemaps = null,
        DateTime? NowUtc = null,
        string? ExpectedLocalFingerprint = null);

    public sealed class SnapshotChangedException() : InvalidOperationException("Local data changed while the snapshot was being applied.");
}
