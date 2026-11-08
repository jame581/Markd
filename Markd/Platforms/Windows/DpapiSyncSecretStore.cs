using System.Security.Cryptography;
using System.Text;
using Markd.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Markd.Platforms.Windows
{
    /// <summary>
    /// Keeps the sync passphrase in a file encrypted with DPAPI for the current Windows user. Used instead of
    /// SecureStorage, which is unverified on the unpackaged build (no package identity).
    /// </summary>
    public sealed class DpapiSyncSecretStore(string dataDirectory, ILogger<DpapiSyncSecretStore> logger) : ISyncSecretStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Markd.Sync.Passphrase");

        private readonly string _path = Path.Combine(dataDirectory, "sync-secret.bin");

        public async Task<string?> GetPassphraseAsync()
        {
            try
            {
                if (!File.Exists(_path)) return null;
                var protectedBytes = await File.ReadAllBytesAsync(_path);
                var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                // A damaged file or one from another Windows account: treat it as no passphrase, so sync asks again.
                logger.LogWarning(ex, "Reading the sync passphrase file failed");
                return null;
            }
        }

        public async Task SetPassphraseAsync(string? passphrase)
        {
            if (passphrase is null)
            {
                File.Delete(_path);
                return;
            }

            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(passphrase), Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await File.WriteAllBytesAsync(temp, protectedBytes);
            File.Move(temp, _path, overwrite: true);
        }
    }
}
