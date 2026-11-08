using Markd.Core.Domain;

namespace Markd.Core.Sync
{
    public interface ISyncStateStore
    {
        Task<SyncState> LoadAsync();

        Task SaveAsync(SyncState state);

        Task ClearAsync();

        /// <summary>True when a row with a provider exists: the device is linked, or disconnected with a kept base.</summary>
        Task<bool> HasSyncStateAsync();
    }
}
