namespace Markd.Core.Domain
{
    public class Occasion : ISyncEntity
    {
        public int Id { get; set; }

        public Guid SyncId { get; set; } = Guid.NewGuid();

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? DeletedAt { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Emoji { get; set; }

        public string? ColorHex { get; set; }

        public bool IsPinned { get; set; }

        /// <summary>
        /// The date this occasion is measured from (Since) or toward (Until).
        /// Stored as UTC.
        /// </summary>
        public DateTime AnchorDate { get; set; }

        /// <summary>
        /// The calendar day the anchor stands for, fixed when the date is chosen (or arrives by sync). Sync sends this
        /// day, so a device that changes time zone does not see its own events move. Null only before the startup
        /// backfill has run on rows from 1.0.
        /// </summary>
        public DateOnly? AnchorDay { get; set; }

        public OccasionDirection Direction { get; set; } = OccasionDirection.Since;

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? CategoryId { get; set; }

        public Category? Category { get; set; }

        public ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();
    }
}
