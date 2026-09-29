namespace Markd.Core.Domain
{
    public class Category : ISyncEntity
    {
        public int Id { get; set; } 

        public Guid SyncId { get; set; } = Guid.NewGuid();

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? DeletedAt { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Emoji { get; set; }
        
        public string? ColorHex { get; set; }

        public ICollection<Occasion> Occasions { get; set; } = new List<Occasion>();
    }
}
