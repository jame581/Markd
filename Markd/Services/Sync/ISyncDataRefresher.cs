namespace Markd.Services.Sync;

/// <summary>
/// Makes the UI see what a sync wrote through its own context: clears the UI DbContext tracker and sends
/// CategoriesChangedMessage and OccasionsChangedMessage with the refresher as Source.
/// </summary>
public interface ISyncDataRefresher
{
    Task RefreshAfterSyncAsync();
}
