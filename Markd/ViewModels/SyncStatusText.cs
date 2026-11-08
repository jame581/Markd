using Markd.Core.Localization;
using Markd.Services.Sync;

namespace Markd.ViewModels;

/// <summary>The one-line sync status shown in settings.</summary>
public static class SyncStatusText
{
    public static string Describe(SyncStatus status, DateTime nowUtc)
    {
        var culture = LocalizationManager.Instance.Culture;
        return status.Kind switch
        {
            SyncStatusKind.Off => Strings.Sync_StatusOff,
            SyncStatusKind.Syncing => Strings.Sync_StatusSyncing,
            SyncStatusKind.Idle => Relative(status.LastSyncUtc, nowUtc, culture),
            SyncStatusKind.Conflicts => Plural.Format("Sync_StatusConflicts", status.ConflictCount, culture),
            SyncStatusKind.NeedsPassphrase => Strings.Sync_StatusNeedsPassphrase,
            SyncStatusKind.SignInAgain => Strings.Sync_StatusSignInAgain,
            SyncStatusKind.Offline => Strings.Sync_StatusOffline,
            SyncStatusKind.UpdateApp => Strings.Sync_StatusUpdateApp,
            SyncStatusKind.Unreadable => Strings.Sync_StatusUnreadable,
            _ => Strings.Sync_StatusFailed,
        };
    }

    private static string Relative(DateTime? lastSyncUtc, DateTime nowUtc, System.Globalization.CultureInfo culture)
    {
        var age = lastSyncUtc is { } last ? nowUtc - last : TimeSpan.Zero;
        if (age < TimeSpan.FromMinutes(1))
            return Strings.Sync_StatusJustNow;
        if (age < TimeSpan.FromHours(1))
            return Plural.Format("Sync_StatusMinutesAgo", (int)age.TotalMinutes, culture);
        if (age < TimeSpan.FromDays(1))
            return Plural.Format("Sync_StatusHoursAgo", (int)age.TotalHours, culture);
        return Plural.Format("Sync_StatusDaysAgo", (int)age.TotalDays, culture);
    }
}
