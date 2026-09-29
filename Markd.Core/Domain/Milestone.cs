namespace Markd.Core.Domain
{
    public class Milestone : ISyncEntity
    {
        public int Id { get; set; }

        public Guid SyncId { get; set; } = Guid.NewGuid();

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? DeletedAt { get; set; }

        public int OccasionId { get; set; }
        public Occasion? Occasion { get; set; }

        /// <summary>
        /// Day threshold that triggers this milestone.
        /// For Since: days elapsed. For Until: days remaining.
        /// </summary>
        public int ThresholdDays { get; set; }

        public string Label { get; set; } = string.Empty;

        public bool Notified { get; set; } = false;
    }
}
