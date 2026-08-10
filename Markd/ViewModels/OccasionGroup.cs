using Markd.Core.Domain;

namespace Markd.ViewModels;

public class OccasionGroup(string categoryName, IEnumerable<Occasion> items) : List<Occasion>(items)
{
    public string CategoryName { get; } = categoryName;
}
