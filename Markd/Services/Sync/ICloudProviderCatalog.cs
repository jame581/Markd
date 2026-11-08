using Markd.Core.Sync;

namespace Markd.Services.Sync;

public sealed record CloudProviderOption(string Id, string DisplayName);

/// <summary>The cloud providers this build can sync with.</summary>
public interface ICloudProviderCatalog
{
    /// <summary>Empty in release builds until real providers exist.</summary>
    IReadOnlyList<CloudProviderOption> Available { get; }

    /// <summary>Null when this build has no provider with that id.</summary>
    ICloudSyncProvider? Get(string providerId);

    /// <summary>
    /// The provider's name even when it is not available in this build or on this device (a link made elsewhere
    /// still shows it). Null for ids this app has never known.
    /// </summary>
    string? DisplayNameOf(string providerId);
}
