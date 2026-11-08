using System.Security.Cryptography;

namespace Markd.Core.Sync
{
    /// <summary>
    /// A "cloud" that is a folder on disk, for development and tests. Every operation holds an exclusive lock file
    /// so two app instances on one PC cannot interleave; writes go through a temp file and an atomic replace.
    /// </summary>
    public sealed class LocalFolderCloudProvider(string folderPath) : ICloudSyncProvider
    {
        private const string FileName = "markd-sync.markd";
        private const string LockName = "markd-sync.lock";
        private const int LockAttempts = 20;
        private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);

        private readonly string _folder = Path.GetFullPath(folderPath);

        public string Id => "local-folder";

        public string? AccountName => _folder;

        private string FilePath => Path.Combine(_folder, FileName);

        public Task<RemoteInfo?> GetInfoAsync(CancellationToken ct) =>
            WithLockAsync<RemoteInfo?>(createFolder: false, ct, () =>
            {
                if (!File.Exists(FilePath)) return null;
                var bytes = File.ReadAllBytes(FilePath);
                return new RemoteInfo(RevisionOf(bytes), File.GetLastWriteTimeUtc(FilePath));
            });

        public Task<RemoteFile?> DownloadAsync(CancellationToken ct) =>
            WithLockAsync<RemoteFile?>(createFolder: false, ct, () =>
            {
                if (!File.Exists(FilePath)) return null;
                var bytes = File.ReadAllBytes(FilePath);
                return new RemoteFile(bytes, RevisionOf(bytes));
            });

        public Task<string> UploadAsync(byte[] data, string? expectedRevision, CancellationToken ct) =>
            WithLockAsync(createFolder: true, ct, () =>
            {
                var current = File.Exists(FilePath) ? RevisionOf(File.ReadAllBytes(FilePath)) : null;
                if (current != expectedRevision)
                    throw new RevisionConflictException();

                var temp = Path.Combine(_folder, FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.WriteAllBytes(temp, data);
                    File.Move(temp, FilePath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                return RevisionOf(data);
            });

        public Task DeleteAsync(CancellationToken ct) =>
            WithLockAsync(createFolder: false, ct, () =>
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                return true;
            });

        private string RevisionOf(byte[] bytes) =>
            FilePath + ":" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        private async Task<T> WithLockAsync<T>(bool createFolder, CancellationToken ct, Func<T> action)
        {
            try
            {
                if (createFolder) Directory.CreateDirectory(_folder);
                else if (!Directory.Exists(_folder)) return action();

                using var gate = await AcquireLockAsync(ct);
                return action();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SyncOfflineException(ex.Message, ex);
            }
        }

        private async Task<FileStream> AcquireLockAsync(CancellationToken ct)
        {
            var path = Path.Combine(_folder, LockName);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                }
                catch (IOException) when (attempt < LockAttempts)
                {
                    await Task.Delay(LockRetryDelay, ct);
                }
            }
        }
    }
}
