namespace Markd.Core.Domain
{
    public class Occasion
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Emoji { get; set; }

        public string? ColorHex { get; set; }

        /// <summary>
        /// The date this occasion is measured from (Since) or toward (Until).
        /// Stored as UTC.
        /// </summary>
        public DateTime AnchorDate { get; set; }

        public OccasionDirection Direction { get; set; } = OccasionDirection.Since;

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? CategoryId { get; set; }

        public Category? Category { get; set; }

        public ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();
    }
}
