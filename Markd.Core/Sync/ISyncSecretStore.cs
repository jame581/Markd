namespace Markd.Core.Sync
{
    /// <summary>The sync passphrase, kept per device in the platform's secure storage (implemented in the app).</summary>
    public interface ISyncSecretStore
    {
        Task<string?> GetPassphraseAsync();

        /// <summary>Null clears it.</summary>
        Task SetPassphraseAsync(string? passphrase);
    }
}
