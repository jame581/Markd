#if DEBUG
using Markd.Core.Sync;

namespace Markd.Services.Sync;

/// <summary>
/// Debug-only catalog offering a local folder as the "cloud", so two app instances can sync on one PC.
/// The folder comes from MARKD_SYNC_FOLDER, or defaults to Documents/MarkdSyncDev.
/// </summary>
public sealed class DebugCloudProviderCatalog : ICloudProviderCatalog
{
    private const string LocalFolderId = "local-folder";

    // Developer-only label: never ships, so it is not localized.
    public IReadOnlyList<CloudProviderOption> Available { get; } = [new(LocalFolderId, "Local folder (developer)")];

    public ICloudSyncProvider? Get(string providerId)
    {
        if (providerId != LocalFolderId) return null;

        var folder = Environment.GetEnvironmentVariable("MARKD_SYNC_FOLDER");
        if (string.IsNullOrWhiteSpace(folder))
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MarkdSyncDev");

        Directory.CreateDirectory(folder);
        return new LocalFolderCloudProvider(folder);
    }
}
#endif
