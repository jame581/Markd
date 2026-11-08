using Markd.Core.Sync;
using Markd.Pages;
using Markd.ViewModels;

namespace Markd.Services.Sync;

/// <summary>
/// Phones: the conflict list as a full modal page. Only returns the answers; null ("Later") when the page is closed,
/// backed out of, or gone without an answer.
/// </summary>
public sealed class PhoneConflictResolutionService : IConflictResolutionService
{
    public Task<IReadOnlyList<ConflictResolution>?> ResolveAsync(IReadOnlyList<SyncConflict> conflicts) =>
        PhoneModalPrompt.ShowAsync(() => new ConflictListPage(new ConflictListViewModel(conflicts)), page => page.Result,
            (IReadOnlyList<ConflictResolution>?)null);
}
