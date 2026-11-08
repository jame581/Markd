namespace Markd.Services.Sync;

public enum SyncStatusKind { Off, Idle, Syncing, Conflicts, NeedsPassphrase, SignInAgain, Offline, UpdateApp, Unreadable, Failed }

public sealed record SyncStatus(SyncStatusKind Kind, DateTime? LastSyncUtc, int ConflictCount, string? ProviderName, string? AccountName);
