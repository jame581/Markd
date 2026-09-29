namespace Markd.Core.Domain
{
    /// <summary>Singleton row (Id = 1) describing this device's link to a cloud provider.</summary>
    public class SyncState
    {
        public int Id { get; set; } = 1;
        public string? ProviderId { get; set; }
        public string? AccountName { get; set; }
        public string? RemoteRevision { get; set; }
        public DateTime? LastSyncUtc { get; set; }
        public string? LastResult { get; set; }

        /// <summary>Identifies one lineage of the cloud file. A new epoch means the cloud data was replaced.</summary>
        public Guid? Epoch { get; set; }

        /// <summary>The snapshot (JSON) this device last synced. The base of the next three-way merge.</summary>
        public string? BaseSnapshotJson { get; set; }
    }
}
