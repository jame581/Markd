using Markd.Core.Sync;

namespace Markd.Services.Sync;

/// <summary>
/// One provider this app knows. IsAvailable says whether this build and device can use it (client id configured,
/// Play services present); an unavailable one keeps its name for links made elsewhere. Auth is null when the provider
/// needs no sign-in.
/// </summary>
public sealed record CloudProviderRegistration(
    string Id, string DisplayName, bool IsAvailable, Func<ICloudSyncProvider> CreateProvider, ICloudAuthService? Auth);

/// <summary>
/// The catalog built from the registrations made at start-up. Each available provider is created on first use and
/// kept for the app session.
/// </summary>
public sealed class CloudProviderCatalog : ICloudProviderCatalog
{
    private readonly Dictionary<string, CloudProviderRegistration> _registrations;
    private readonly Dictionary<string, Lazy<ICloudSyncProvider>> _providers;

    public CloudProviderCatalog(IEnumerable<CloudProviderRegistration> registrations)
    {
        _registrations = registrations.ToDictionary(r => r.Id);
        _providers = _registrations.Values
            .Where(r => r.IsAvailable)
            .ToDictionary(r => r.Id, r => new Lazy<ICloudSyncProvider>(r.CreateProvider));
        Available = _registrations.Values
            .Where(r => r.IsAvailable)
            .Select(r => new CloudProviderOption(r.Id, r.DisplayName))
            .ToList();
    }

    public IReadOnlyList<CloudProviderOption> Available { get; }

    public ICloudSyncProvider? Get(string providerId) =>
        _providers.TryGetValue(providerId, out var provider) ? provider.Value : null;

    public string? DisplayNameOf(string providerId) =>
        _registrations.TryGetValue(providerId, out var registration) ? registration.DisplayName : null;

    public ICloudAuthService? AuthFor(string providerId) =>
        _registrations.TryGetValue(providerId, out var registration) && registration.IsAvailable ? registration.Auth : null;
}
