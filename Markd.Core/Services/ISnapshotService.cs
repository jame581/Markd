namespace Markd.Core.Services
{
    public interface ISnapshotService
    {
        /// <summary>The device's time zone, which turns stored anchors into calendar days and back.</summary>
        TimeZoneInfo Zone { get; }

        /// <summary>Tombstones included, Notified always false.</summary>
        Task<ExportModel> ReadForSyncAsync();

        Task ApplyAsync(ExportModel target, SnapshotApplyOptions options);

        /// <summary>Hard-deletes tombstones older than the cutoff. Returns rows removed.</summary>
        Task<int> PurgeTombstonesAsync(DateTime olderThanUtc);
    }

    /// <param name="Additive">
    /// Restore on a device with sync state: records live here are left untouched, tombstones in the target are ignored,
    /// local tombstones come back with the target's content, unknown records are added and nothing is deleted.
    /// Milestones are applied only under occasions that were added or revived. Live pins are never moved.
    /// </param>
    /// <param name="PrepareAdditions">
    /// Additive only: called inside the apply transaction with the local snapshot (tombstones included) and returns
    /// what to apply instead of the target, so matching against local data cannot be defeated by a sync applied
    /// between a separate read and the apply.
    /// </param>
    public sealed record SnapshotApplyOptions(
        bool TakeNotifiedFromTarget,
        bool ApplySettings = false,
        IReadOnlyDictionary<Guid, Guid>? SyncIdRemaps = null,
        DateTime? NowUtc = null,
        string? ExpectedLocalFingerprint = null,
        bool Additive = false,
        Func<ExportModel, ExportModel>? PrepareAdditions = null);

    public sealed class SnapshotChangedException() : InvalidOperationException("Local data changed while the snapshot was being applied.");
}
