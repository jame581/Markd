using Markd.Core.Domain;
using Markd.Core.Services;

namespace Markd.Core.Sync
{
    public sealed class SyncService(
        ISnapshotService snapshots, ISyncStateStore store, ISyncSecretStore secrets, TimeProvider clock, SyncOptions options,
        SyncGate gate)
        : ISyncService
    {
        public async Task<SyncResult> SyncAsync(
            ICloudSyncProvider provider, IReadOnlyCollection<ConflictResolution>? resolutions = null, CancellationToken ct = default)
        {
            await gate.WaitAsync(ct);
            try
            {
                var syncResult = await RunAsync(() => SyncCoreAsync(provider, resolutions, ct), ct);
                await RecordAttemptAsync(syncResult);
                return syncResult;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>The sync loop with the stored passphrase. The caller holds the gate and maps exceptions (RunAsync).</summary>
        private async Task<SyncResult> SyncCoreAsync(
            ICloudSyncProvider provider, IReadOnlyCollection<ConflictResolution>? resolutions, CancellationToken ct)
        {
            var passphrase = await secrets.GetPassphraseAsync();
            if (string.IsNullOrEmpty(passphrase))
                return new SyncResult(SyncOutcome.NeedsPassphrase);

            var retry = RetryReason.RemoteChanged;
            for (var attempt = 0; attempt < Math.Max(1, options.MaxUploadAttempts); attempt++)
            {
                (var result, retry) = await TryOnceAsync(provider, passphrase, resolutions, ct);
                if (result is not null)
                    return result;
            }

            return new SyncResult(SyncOutcome.Failed, Detail: retry == RetryReason.LocalChanged
                ? "Local data kept changing during the sync."
                : "The remote file kept changing.");
        }

        public async Task<SyncResult> ChangePassphraseAsync(ICloudSyncProvider provider, string newPassphrase, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(newPassphrase))
                return new SyncResult(SyncOutcome.NeedsPassphrase);

            await gate.WaitAsync(ct);
            try
            {
                var changeResult = await RunAsync(async () =>
                {
                    for (var attempt = 0; attempt < Math.Max(1, options.MaxUploadAttempts); attempt++)
                    {
                        // Bring both sides level first: after a successful sync the base is exactly the cloud content
                        // at RemoteRevision, so re-encrypting it changes the key and nothing else.
                        var synced = await SyncCoreAsync(provider, resolutions: null, ct);
                        if (synced.Outcome is not (SyncOutcome.Synced or SyncOutcome.NoChanges))
                            return synced;

                        var state = await store.LoadAsync();
                        var content = ParseBase(state.BaseSnapshotJson);
                        if (content is null || state.RemoteRevision is null)
                            return new SyncResult(SyncOutcome.Failed, Detail: "No synced content to re-encrypt.");

                        content.SyncEpoch = state.Epoch;   // same lineage: other devices keep their base
                        var bytes = await Task.Run(() => SyncSnapshotCodec.Encode(content, newPassphrase, options.KdfIterations), ct);
                        string revision;
                        try
                        {
                            // Expecting the synced revision means an edit uploaded meanwhile by another device is never
                            // overwritten: the upload fails and the next round merges that edit first.
                            revision = await provider.UploadAsync(bytes, state.RemoteRevision, ct);
                        }
                        catch (RevisionConflictException)
                        {
                            continue;
                        }

                        await secrets.SetPassphraseAsync(newPassphrase);
                        state.RemoteRevision = revision;
                        state.LastSyncUtc = clock.GetUtcNow().UtcDateTime;
                        state.LastResult = nameof(SyncOutcome.Synced);
                        state.IsConnected = true;
                        await store.SaveAsync(state);
                        return new SyncResult(SyncOutcome.Synced);
                    }

                    return new SyncResult(SyncOutcome.Failed, Detail: "The remote file kept changing.");
                }, ct);
                await RecordAttemptAsync(changeResult);
                return changeResult;
            }
            finally
            {
                gate.Release();
            }
        }

        private enum RetryReason { RemoteChanged, LocalChanged }

        /// <summary>
        /// Returns a null result when the attempt must be repeated, with the reason: the upload lost a race, or the
        /// user edited while the sync was running.
        /// </summary>
        private async Task<(SyncResult? Result, RetryReason Retry)> TryOnceAsync(
            ICloudSyncProvider provider, string passphrase, IReadOnlyCollection<ConflictResolution>? resolutions, CancellationToken ct)
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
            // After a disconnect the passphrase was typed again: decode the file to prove it before anything is
            // uploaded under the same epoch (an unchanged revision would otherwise skip the download). The base stays.
            var mustDecode = !state.IsConnected;
            if (info is not null && baseModel is not null && !localChanged && !remoteChanged && !mustDecode)
            {
                state.LastSyncUtc = now;
                state.LastResult = nameof(SyncOutcome.NoChanges);
                state.IsConnected = true;
                await store.SaveAsync(state);
                return Done(new SyncResult(SyncOutcome.NoChanges));
            }

            var remoteMissing = info is null;
            ExportModel remote;
            string? remoteRevision = info?.Revision;
            if (info is null)
            {
                remote = SyncMerger.Empty();
            }
            else if (remoteChanged || baseModel is null || mustDecode)
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
                return Done(new SyncResult(SyncOutcome.NeedsConflictResolution, merge.Conflicts));

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
                    return (null, RetryReason.LocalChanged);   // the user edited while we were talking to the cloud: merge again from fresh data
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
                    return (null, RetryReason.RemoteChanged);
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
            state.IsConnected = true;
            state.BaseSnapshotJson = ExportModelJson.Serialize(merged, indented: false);
            await store.SaveAsync(state);
            return Done(new SyncResult(SyncOutcome.Synced));
        }

        /// <summary>
        /// Writes only the two attempt fields onto whatever state row exists (a new empty one otherwise): it never links
        /// or connects. A failure here is swallowed so it cannot replace the real outcome.
        /// </summary>
        private async Task RecordAttemptAsync(SyncResult result)
        {
            try
            {
                var state = await store.LoadAsync();
                state.LastAttemptUtc = clock.GetUtcNow().UtcDateTime;
                state.LastAttemptResult = result.Outcome.ToString();
                await store.SaveAsync(state);
            }
            catch (Exception)
            {
                // best effort, see above
            }
        }

        private static (SyncResult? Result, RetryReason Retry) Done(SyncResult result) => (result, default);

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

            await gate.WaitAsync(ct);
            try
            {
                var replaceResult = await RunAsync(async () =>
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
                    state.IsConnected = true;
                    state.Epoch = local.SyncEpoch;
                    state.BaseSnapshotJson = ExportModelJson.Serialize(local, indented: false);
                    await store.SaveAsync(state);
                    return new SyncResult(SyncOutcome.Synced);
                }, ct);
                await RecordAttemptAsync(replaceResult);
                return replaceResult;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Forgets the passphrase and keeps the base (marked disconnected), or with deleteRemote deletes the cloud file
        /// and forgets the whole state. Local data is never touched. When deleting the cloud file fails, nothing is
        /// forgotten and the failure is the outcome.
        /// </summary>
        public async Task<SyncResult> DisconnectAsync(ICloudSyncProvider? provider, bool deleteRemote, CancellationToken ct = default)
        {
            await gate.WaitAsync(ct);
            try
            {
                return await RunAsync(async () =>
                {
                    // A failed delete throws before anything is forgotten: the link, the state and the passphrase stay.
                    if (deleteRemote && provider is not null)
                        await provider.DeleteAsync(ct);

                    if (deleteRemote)
                    {
                        await store.ClearAsync();
                    }
                    else
                    {
                        // Keep the base: a later reconnect to the same file merges against it instead of asking
                        // about every record both sides changed meanwhile.
                        var state = await store.LoadAsync();
                        state.IsConnected = false;
                        await store.SaveAsync(state);
                    }

                    await secrets.SetPassphraseAsync(null);
                    return new SyncResult(SyncOutcome.Disconnected);
                }, ct);
            }
            finally
            {
                gate.Release();
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
