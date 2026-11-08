using Markd.Core.Data;
using Markd.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Markd.Core.Sync
{
    public class SyncStateStore(MarkdDbContext db) : ISyncStateStore
    {
        public async Task<SyncState> LoadAsync()
        {
            var row = await db.SyncStates.AsNoTracking().FirstOrDefaultAsync();
            return row is null ? new SyncState() : Copy(row);
        }

        public async Task SaveAsync(SyncState state)
        {
            var row = await db.SyncStates.FirstOrDefaultAsync();
            if (row is null)
            {
                row = new SyncState();
                db.SyncStates.Add(row);
            }

            row.ProviderId = state.ProviderId;
            row.AccountName = state.AccountName;
            row.RemoteRevision = state.RemoteRevision;
            row.LastSyncUtc = state.LastSyncUtc;
            row.LastResult = state.LastResult;
            row.IsConnected = state.IsConnected;
            row.LastAttemptUtc = state.LastAttemptUtc;
            row.LastAttemptResult = state.LastAttemptResult;
            row.Epoch = state.Epoch;
            row.BaseSnapshotJson = state.BaseSnapshotJson;
            try
            {
                await db.SaveChangesAsync();
            }
            catch
            {
                // The app keeps one context for its session: a row left tracked as added or modified would be
                // written by the next unrelated save.
                db.Entry(row).State = EntityState.Detached;
                throw;
            }
        }

        public Task<bool> HasSyncStateAsync() =>
            db.SyncStates.AsNoTracking().AnyAsync(s => s.ProviderId != null);

        public async Task ClearAsync()
        {
            var row = await db.SyncStates.FirstOrDefaultAsync();
            if (row is null)
                return;

            db.SyncStates.Remove(row);
            await db.SaveChangesAsync();
        }

        private static SyncState Copy(SyncState s) => new()
        {
            Id = s.Id, ProviderId = s.ProviderId, AccountName = s.AccountName, RemoteRevision = s.RemoteRevision,
            LastSyncUtc = s.LastSyncUtc, LastResult = s.LastResult, BaseSnapshotJson = s.BaseSnapshotJson, Epoch = s.Epoch,
            IsConnected = s.IsConnected, LastAttemptUtc = s.LastAttemptUtc, LastAttemptResult = s.LastAttemptResult
        };
    }
}
