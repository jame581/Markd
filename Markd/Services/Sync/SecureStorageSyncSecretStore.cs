using Markd.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Markd.Services.Sync;

/// <summary>Keeps the sync passphrase in the platform's secure storage (phone layouts).</summary>
public sealed class SecureStorageSyncSecretStore(ILogger<SecureStorageSyncSecretStore> logger) : ISyncSecretStore
{
    private const string Key = "markd.sync.passphrase";

    public async Task<string?> GetPassphraseAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(Key);
        }
        catch (Exception ex)
        {
            // Some devices fail to read secure storage (for example after a backup restore). Treat it as no
            // passphrase, so sync asks for it again instead of crashing.
            logger.LogWarning(ex, "Reading the sync passphrase from secure storage failed");
            return null;
        }
    }

    public async Task SetPassphraseAsync(string? passphrase)
    {
        try
        {
            if (passphrase is null)
                SecureStorage.Default.Remove(Key);
            else
                await SecureStorage.Default.SetAsync(Key, passphrase);
        }
        catch (Exception ex)
        {
            // Never crash a sync or a cancel over the secure store: a passphrase that was not kept is asked for again.
            logger.LogWarning(ex, "Writing the sync passphrase to secure storage failed");
        }
    }
}
