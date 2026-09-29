using System.Security.Cryptography;
using Markd.Core.Domain;
using Markd.Core.Services;

namespace Markd.Core.Sync
{
    public sealed class SyncService(
        ISnapshotService snapshots, ISyncStateStore store, ISyncSecretStore secrets, TimeProvider clock, SyncOptions options)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public async Task<SyncResult> SyncAsync(
            ICloudSyncProvider provider, IReadOnlyDictionary<Guid, ConflictChoice>? resolutions = null, CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct);
            try
            {
                return await RunAsync(async () =>
                {
                    var passphrase = await secrets.GetPassphraseAsync();
                    if (string.IsNullOrEmpty(passphrase))
                        return new SyncResult(SyncOutcome.NeedsPassphrase);

                    for (var attempt = 0; attempt < Math.Max(1, options.MaxUploadAttempts); attempt++)
                    {
                        var result = await TryOnceAsync(provider, passphrase, resolutions, ct);
                        if (result is not null)
                            return result;
                    }

                    return new SyncResult(SyncOutcome.Failed, Detail: "The remote file kept changing.");
                }, ct);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Returns null when the upload lost a race and the attempt must be repeated.</summary>
        private async Task<SyncResult?> TryOnceAsync(
            ICloudSyncProvider provider, string passphrase, IReadOnlyDictionary<Guid, ConflictChoice>? resolutions, CancellationToken ct)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var state = await store.LoadAsync();
            if (state.ProviderId != provider.Id)
                state = new SyncState { ProviderId = provider.Id };

            var info = await provider.GetInfoAsync(ct);
            var local = await snapshots.ReadForSyncAsync();

            // A missing cloud file must never look like "everything was deleted": drop the base so the
            // merge re-uploads local data instead of wiping it.
            var baseModel = info is null ? null : ParseBase(state.BaseSnapshotJson);

            var localChanged = baseModel is null || SyncContent.Fingerprint(local) != SyncContent.Fingerprint(baseModel);
            var remoteChanged = info is not null && info.Revision != state.RemoteRevision;
            if (info is not null && baseModel is not null && !localChanged && !remoteChanged)
            {
                state.LastSyncUtc = now;
                state.LastResult = nameof(SyncOutcome.NoChanges);
                await store.SaveAsync(state);
                return new SyncResult(SyncOutcome.NoChanges);
            }

            var remoteMissing = info is null;
            ExportModel remote;
            string? remoteRevision = info?.Revision;
            if (info is null)
            {
                remote = SyncMerger.Empty();
            }
            else if (remoteChanged || baseModel is null)
            {
                var file = await provider.DownloadAsync(ct);
                if (file is null)
                {
                    remote = SyncMerger.Empty();
                    remoteRevision = null;
                    remoteMissing = true;
                    baseModel = null;
                }
                else
                {
                    remote = await Task.Run(() => SyncSnapshotCodec.Decode(file.Data, passphrase), ct);
                    remoteRevision = file.Revision;

                    // A different epoch means the cloud data was replaced (forgotten passphrase, reset): our base
                    // describes another lineage, so it must not turn the replacement into "everything was deleted".
                    if (baseModel is not null && remote.SyncEpoch != state.Epoch)
                        baseModel = null;
                }
            }
            else
            {
                remote = baseModel;
            }

            var merge = SyncMerger.Merge(baseModel, local, remote, now, resolutions);
            if (merge.Conflicts.Count > 0)
                return new SyncResult(SyncOutcome.NeedsConflictResolution, merge.Conflicts);

            var merged = merge.Merged;
            // A missing cloud file starts a new lineage: reusing the old epoch would let other devices keep their
            // base and read everything this device never saw as "deleted remotely".
            merged.SyncEpoch = remoteMissing ? Guid.NewGuid() : (remote.SyncEpoch ?? state.Epoch ?? Guid.NewGuid());
            var mergedPrint = SyncContent.Fingerprint(merged);

            if (mergedPrint != SyncContent.Fingerprint(local) || merge.SyncIdRemaps.Count > 0)
            {
                try
                {
                    await snapshots.ApplyAsync(merged, new SnapshotApplyOptions(
                        TakeNotifiedFromTarget: false, SyncIdRemaps: merge.SyncIdRemaps, NowUtc: now,
                        ExpectedLocalFingerprint: SyncContent.Fingerprint(local)));
                }
                catch (SnapshotChangedException)
                {
                    return null;   // the user edited while we were talking to the cloud: merge again from fresh data
                }
            }

            string newRevision;
            if (remoteRevision is null || mergedPrint != SyncContent.Fingerprint(remote))
            {
                var bytes = await Task.Run(() => SyncSnapshotCodec.Encode(merged, passphrase, options.KdfIterations), ct);
                try
                {
                    newRevision = await provider.UploadAsync(bytes, remoteRevision, ct);
                }
                catch (RevisionConflictException)
                {
                    return null;
                }
            }
            else
            {
                newRevision = remoteRevision;
            }

            await snapshots.PurgeTombstonesAsync(now - SyncMerger.TombstoneRetention);

            state.RemoteRevision = newRevision;
            state.Epoch = merged.SyncEpoch;
            state.LastSyncUtc = now;
            state.LastResult = nameof(SyncOutcome.Synced);
            state.BaseSnapshotJson = ExportModelJson.Serialize(merged, indented: false);
            await store.SaveAsync(state);
            return new SyncResult(SyncOutcome.Synced);
        }

        private static ExportModel? ParseBase(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try { return ExportModelJson.Deserialize(json); }
            catch (InvalidOperationException) { return null; }
        }

        /// <summary>Failure mapping. Filled in by Task 13.</summary>
        private static async Task<SyncResult> RunAsync(Func<Task<SyncResult>> body, CancellationToken ct) => await body();
    }
}
