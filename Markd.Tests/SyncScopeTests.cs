using Markd.Core;
using Markd.Core.Sync;
using Markd.Services.Sync;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Markd.Tests;

public class SyncScopeTests
{
    private sealed class NoSecrets : ISyncSecretStore
    {
        public Task<string?> GetPassphraseAsync() => Task.FromResult<string?>(null);

        public Task SetPassphraseAsync(string? passphrase) => Task.CompletedTask;
    }

    private static ServiceProvider BuildRoot()
    {
        var services = new ServiceCollection();
        services.AddMarkdCore(Path.Combine(Path.GetTempPath(), "markd-syncscope-unused.db"));
        services.AddSingleton<ISyncSecretStore, NoSecrets>();
        services.AddSingleton<SyncScope>();
        services.AddSingleton<ISyncSession>(sp => sp.GetRequiredService<SyncScope>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Session_services_come_from_one_scope_of_their_own()
    {
        using var root = BuildRoot();
        var session = root.GetRequiredService<ISyncSession>();

        Assert.Same(session.State, session.State);
        Assert.Same(session.Sync, session.Sync);
        Assert.NotSame(root.GetRequiredService<ISyncStateStore>(), session.State);
    }

    [Fact]
    public void Dispose_disposes_the_scope_context()
    {
        var root = BuildRoot();
        var scope = root.GetRequiredService<SyncScope>();
        _ = scope.State;

        root.Dispose();

        // A store on a disposed scope can no longer be resolved.
        Assert.Throws<ObjectDisposedException>(() => scope.State);
    }
}
