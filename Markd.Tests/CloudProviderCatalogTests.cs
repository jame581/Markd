using Markd.Core.Sync;
using Markd.Services.Sync;
using Xunit;

namespace Markd.Tests;

public class CloudProviderCatalogTests
{
    private static CloudProviderRegistration Registration(
        string id, bool available, Func<ICloudSyncProvider>? create = null, ICloudAuthService? auth = null) =>
        new(id, id + " name", available, create ?? (() => new SyncCoordinatorTests.FakeProvider()), auth);

    [Fact]
    public void Unavailable_IsNotListed_ButHasADisplayName()
    {
        var created = 0;
        var auth = new SyncCoordinatorTests.FakeAuth();
        var catalog = new CloudProviderCatalog(
        [
            Registration("google-drive", available: false, () => { created++; return new SyncCoordinatorTests.FakeProvider(); }, auth),
            Registration("local-folder", available: true),
        ]);

        Assert.Equal(["local-folder"], catalog.Available.Select(o => o.Id));
        Assert.Equal("google-drive name", catalog.DisplayNameOf("google-drive"));
        Assert.Null(catalog.Get("google-drive"));
        Assert.Null(catalog.AuthFor("google-drive"));
        Assert.Equal(0, created);
    }

    [Fact]
    public void UnknownId_HasNothing()
    {
        var catalog = new CloudProviderCatalog([Registration("local-folder", available: true)]);

        Assert.Null(catalog.DisplayNameOf("dropbox"));
        Assert.Null(catalog.Get("dropbox"));
        Assert.Null(catalog.AuthFor("dropbox"));
    }

    [Fact]
    public void Get_ReturnsTheSameProviderInstance()
    {
        var created = 0;
        var catalog = new CloudProviderCatalog(
        [
            Registration("google-drive", available: true, () => { created++; return new SyncCoordinatorTests.FakeProvider(); }),
        ]);

        var first = catalog.Get("google-drive");
        var second = catalog.Get("google-drive");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, created);
    }

    [Fact]
    public void AuthFor_LocalFolder_IsNull()
    {
        var auth = new SyncCoordinatorTests.FakeAuth();
        var catalog = new CloudProviderCatalog(
        [
            Registration("local-folder", available: true),
            Registration("google-drive", available: true, auth: auth),
        ]);

        Assert.Null(catalog.AuthFor("local-folder"));
        Assert.Same(auth, catalog.AuthFor("google-drive"));
    }
}
