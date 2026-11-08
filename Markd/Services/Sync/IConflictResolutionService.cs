using Markd.Core.Sync;

namespace Markd.Services.Sync;

/// <summary>Shows sync conflicts and collects the user's choices (implemented per layout).</summary>
public interface IConflictResolutionService
{
    /// <summary>Null means "Later". Otherwise returns one resolution per conflict.</summary>
    Task<IReadOnlyList<ConflictResolution>?> ResolveAsync(IReadOnlyList<SyncConflict> conflicts);
}
