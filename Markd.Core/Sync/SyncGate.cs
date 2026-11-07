namespace Markd.Core.Sync
{
    /// <summary>
    /// The one lock every sync operation takes. It is registered as a singleton so scoped <see cref="SyncService"/>
    /// instances (one per DI scope) can never run two syncs at once.
    /// </summary>
    public sealed class SyncGate
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);

        public void Release() => _semaphore.Release();
    }
}
