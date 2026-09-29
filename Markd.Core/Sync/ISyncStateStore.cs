using Markd.Core.Domain;

namespace Markd.Core.Sync
{
    public interface ISyncStateStore
    {
        Task<SyncState> LoadAsync();

        Task SaveAsync(SyncState state);

        Task ClearAsync();
    }
}
