namespace Markd.Core.Domain
{
    public class Category
    {
        public int Id { get; set; } 

        public string Name { get; set; } = string.Empty;

        public string? Emoji { get; set; }
        
        public string? ColorHex { get; set; }

        public ICollection<Occasion> Occasions { get; set; } = new List<Occasion>();
    }
}
