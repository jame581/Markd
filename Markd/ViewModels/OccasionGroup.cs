using Markd.Core.Domain;

namespace Markd.ViewModels;

public class OccasionGroup(string categoryName, IEnumerable<OccasionSummary> items) : List<OccasionSummary>(items)
{
    public string CategoryName { get; } = categoryName;
}
