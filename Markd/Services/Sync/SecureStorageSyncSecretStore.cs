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

    public Task SetPassphraseAsync(string? passphrase)
    {
        if (passphrase is null)
        {
            SecureStorage.Default.Remove(Key);
            return Task.CompletedTask;
        }

        return SecureStorage.Default.SetAsync(Key, passphrase);
    }
}
