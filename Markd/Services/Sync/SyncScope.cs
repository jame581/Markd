using Markd.Core.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace Markd.Services.Sync;

/// <summary>Holds one service scope, created at first use, so sync runs on its own MarkdDbContext.</summary>
public sealed class SyncScope(IServiceScopeFactory scopeFactory) : ISyncSession, IDisposable
{
    private readonly Lazy<IServiceScope> _scope = new(scopeFactory.CreateScope, LazyThreadSafetyMode.ExecutionAndPublication);

    public ISyncService Sync => _scope.Value.ServiceProvider.GetRequiredService<ISyncService>();

    public ISyncStateStore State => _scope.Value.ServiceProvider.GetRequiredService<ISyncStateStore>();

    public void Dispose()
    {
        if (_scope.IsValueCreated)
            _scope.Value.Dispose();
    }
}
