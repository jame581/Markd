using Markd.Core.Sync;

namespace Markd.Services.Sync;

/// <summary>
/// The sync service and state store on a DbContext of their own, separate from the one the UI uses.
/// Sync code gets its services only through this, never by injecting <see cref="ISyncService"/> (which would
/// resolve the UI context from the root provider).
/// </summary>
public interface ISyncSession
{
    ISyncService Sync { get; }

    ISyncStateStore State { get; }
}
