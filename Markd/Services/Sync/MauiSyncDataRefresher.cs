using CommunityToolkit.Mvvm.Messaging;
using Markd.Core.Data;
using Markd.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Markd.Services.Sync;

/// <summary>
/// After a sync wrote through its own context, drops what the UI context has tracked (so the UI services read
/// fresh rows) and tells every view to reload.
/// </summary>
public sealed class MauiSyncDataRefresher(IServiceProvider services, IUiDispatcher dispatcher) : ISyncDataRefresher
{
    public Task RefreshAfterSyncAsync() => dispatcher.InvokeAsync(() =>
    {
        // Root-resolved: the same instance the UI services use.
        services.GetRequiredService<MarkdDbContext>().ChangeTracker.Clear();

        WeakReferenceMessenger.Default.Send(new CategoriesChangedMessage(this));
        WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(null, this));
        return Task.CompletedTask;
    });
}
