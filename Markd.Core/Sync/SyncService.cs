using Markd.Core.Domain;
using Markd.Core.Services;

namespace Markd.Core.Sync
{
    public sealed class SyncService(
        ISnapshotService snapshots, ISyncStateStore store, ISyncSecretStore secrets, TimeProvider clock, SyncOptions options)
        : ISyncService
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
            // Another provider or another account on the same provider is another file: its revision strings say
            // nothing about ours, so start over as a first sync (no base, no revision, no epoch) and merge.
            if (!string.Equals(state.ProviderId, provider.Id, StringComparison.Ordinal)
                || !string.Equals(state.AccountName, provider.AccountName, StringComparison.Ordinal))
                state = new SyncState { ProviderId = provider.Id, AccountName = provider.AccountName };

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
                if (!remoteMissing)
                {
                    // Local data now holds everything the remote had at remoteRevision, so that remote is the common
                    // ancestor. Store it before uploading: if the upload loses a race (or the network drops), the next
                    // merge must not read the records this device just adopted as its own edits.
                    state.BaseSnapshotJson = ExportModelJson.Serialize(remote, indented: false);
                    state.RemoteRevision = remoteRevision;
                    state.Epoch = merged.SyncEpoch;
                    await store.SaveAsync(state);
                }

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

        public async Task<SyncResult> ReplaceRemoteAsync(ICloudSyncProvider provider, string newPassphrase, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(newPassphrase))
                return new SyncResult(SyncOutcome.NeedsPassphrase);

            await _gate.WaitAsync(ct);
            try
            {
                return await RunAsync(async () =>
                {
                    var now = clock.GetUtcNow().UtcDateTime;
                    var info = await provider.GetInfoAsync(ct);
                    var local = await snapshots.ReadForSyncAsync();
                    local.SyncEpoch = Guid.NewGuid();   // a new lineage: other devices must not merge against their old base
                    var bytes = await Task.Run(() => SyncSnapshotCodec.Encode(local, newPassphrase, options.KdfIterations), ct);
                    var revision = await provider.UploadAsync(bytes, info?.Revision, ct);

                    await secrets.SetPassphraseAsync(newPassphrase);
                    var state = await store.LoadAsync();
                    state.ProviderId = provider.Id;
                    state.AccountName = provider.AccountName;
                    state.RemoteRevision = revision;
                    state.LastSyncUtc = now;
                    state.LastResult = nameof(SyncOutcome.Synced);
                    state.Epoch = local.SyncEpoch;
                    state.BaseSnapshotJson = ExportModelJson.Serialize(local, indented: false);
                    await store.SaveAsync(state);
                    return new SyncResult(SyncOutcome.Synced);
                }, ct);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Forgets the link and the passphrase. Local data is never touched. When deleting the cloud file fails,
        /// nothing is forgotten and the failure is the outcome.
        /// </summary>
        public async Task<SyncResult> DisconnectAsync(ICloudSyncProvider? provider, bool deleteRemote, CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct);
            try
            {
                return await RunAsync(async () =>
                {
                    // A failed delete throws before anything is forgotten: the link, the state and the passphrase stay.
                    if (deleteRemote && provider is not null)
                        await provider.DeleteAsync(ct);

                    await store.ClearAsync();
                    await secrets.SetPassphraseAsync(null);
                    return new SyncResult(SyncOutcome.Synced);
                }, ct);
            }
            finally
            {
                _gate.Release();
            }
        }

        private static async Task<SyncResult> RunAsync(Func<Task<SyncResult>> body, CancellationToken ct)
        {
            try
            {
                return await body();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;   // the caller cancelled
            }
            catch (SyncAuthException)
            {
                return new SyncResult(SyncOutcome.AuthExpired);
            }
            catch (Exception ex) when (ex is SyncOfflineException or HttpRequestException or OperationCanceledException)
            {
                // OperationCanceledException here is a timeout (HttpClient reports one as TaskCanceledException).
                return new SyncResult(SyncOutcome.Offline);
            }
            catch (MarkdPackageException ex) when (ex.Error is PackageError.WrongPassword or PackageError.PasswordRequired)
            {
                return new SyncResult(SyncOutcome.WrongPassphrase);
            }
            catch (SnapshotTooNewException)
            {
                return new SyncResult(SyncOutcome.RemoteTooNew);
            }
            catch (SnapshotFormatException ex)
            {
                return new SyncResult(SyncOutcome.RemoteUnreadable, Detail: ex.Message);   // Detail is for logging only
            }
            catch (RevisionConflictException)
            {
                return new SyncResult(SyncOutcome.Failed, Detail: "The remote file changed while replacing it.");
            }
            catch (Exception ex)
            {
                return new SyncResult(SyncOutcome.Failed, Detail: ex.Message);
            }
        }
    }
}
