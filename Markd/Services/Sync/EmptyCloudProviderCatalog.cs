using Markd.Core.Sync;

namespace Markd.Services.Sync;

/// <summary>Release catalog: no providers until real ones ship.</summary>
public sealed class EmptyCloudProviderCatalog : ICloudProviderCatalog
{
    public IReadOnlyList<CloudProviderOption> Available { get; } = [];

    public ICloudSyncProvider? Get(string providerId) => null;
}
