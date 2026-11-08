namespace Markd.Services.Sync;

/// <summary>What the sync settings UI needs from the sync coordinator.</summary>
public interface ISyncController
{
    SyncStatus Status { get; }

    event EventHandler? StatusChanged;

    Task SyncNowAsync(SyncTrigger trigger);

    Task OpenConflictsAsync();

    Task ConnectAsync(string providerId);

    Task EnterPassphraseAsync();

    Task ChangePassphraseAsync();

    Task ReplaceCloudAsync();

    Task SignInAgainAsync();

    Task DisconnectAsync();
}
