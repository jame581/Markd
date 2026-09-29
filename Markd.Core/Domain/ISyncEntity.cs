namespace Markd.Core.Domain
{
    /// <summary>Identity and change tracking shared by every record that syncs between devices.</summary>
    public interface ISyncEntity
    {
        /// <summary>Stable across devices. The int Id stays the local key.</summary>
        Guid SyncId { get; set; }

        /// <summary>UTC. Stamped by <c>MarkdDbContext</c> on every real change.</summary>
        DateTime UpdatedAt { get; set; }

        /// <summary>UTC tombstone. Null while the record is live.</summary>
        DateTime? DeletedAt { get; set; }
    }
}
