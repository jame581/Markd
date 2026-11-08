namespace Markd.Services.Sync;

/// <summary>A signed-in cloud account: <see cref="Id"/> is stable (the link's account id), the email is display only.</summary>
public sealed record CloudAccount(string Id, string? Email);

/// <summary>What a provider needs from its sign-in: the account and silent access tokens, never any UI.</summary>
public interface IAccessTokenSource
{
    /// <summary>The signed-in account, or null. Valid once LoadAsync completed.</summary>
    CloudAccount? Account { get; }

    /// <summary>
    /// Silent only. forceRefresh skips any cached access token (providers retry once with it after a 401).
    /// Throws SyncAuthException when the user must sign in, SyncOfflineException without network, and lets
    /// OperationCanceledException from the caller's ct through unchanged.
    /// </summary>
    Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct);
}

/// <summary>One provider's sign-in on this device. Interactive sign-in only ever starts from a user action.</summary>
public interface ICloudAuthService : IAccessTokenSource
{
    /// <summary>Reads the stored account (DPAPI file, Preferences, MSAL cache). Idempotent, never throws.</summary>
    Task LoadAsync();

    /// <summary>True when the interactive flow opens a browser outside Markd (the waiting panel is shown).</summary>
    bool SignInLeavesApp { get; }

    /// <summary>Null when the user cancelled (or ct fired). Throws SyncOfflineException / SyncAuthException on failure.</summary>
    Task<CloudAccount?> SignInAsync(string? loginHint, CancellationToken ct);

    /// <summary>Forgets this device's tokens and account. Never revokes. Never throws.</summary>
    Task SignOutAsync();
}
