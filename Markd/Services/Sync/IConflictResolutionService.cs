using Markd.Core.Sync;

namespace Markd.Services.Sync;

/// <summary>
/// Shows sync conflicts and collects the user's choices (implemented per layout). Implementations only return answers:
/// they never call <see cref="SyncCoordinator"/> (it is waiting for them), and the task must always complete, so a
/// dismiss, back or navigation away returns null.
/// </summary>
public interface IConflictResolutionService
{
    /// <summary>Null means "Later". Otherwise returns one resolution per conflict.</summary>
    Task<IReadOnlyList<ConflictResolution>?> ResolveAsync(IReadOnlyList<SyncConflict> conflicts);
}
