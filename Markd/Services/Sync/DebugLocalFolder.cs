#if DEBUG
using Markd.Core.Sync;

namespace Markd.Services.Sync;

/// <summary>
/// Debug-only provider offering a local folder as the "cloud", so two app instances can sync on one PC.
/// The folder comes from MARKD_SYNC_FOLDER, or defaults to Documents/MarkdSyncDev.
/// </summary>
public static class DebugLocalFolder
{
    // Developer-only label: never ships, so it is not localized.
    public static CloudProviderRegistration Registration { get; } =
        new("local-folder", "Local folder (developer)", IsAvailable: true, CreateProvider, Auth: null);

    private static ICloudSyncProvider CreateProvider()
    {
        var folder = Environment.GetEnvironmentVariable("MARKD_SYNC_FOLDER");
        if (string.IsNullOrWhiteSpace(folder))
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MarkdSyncDev");

        Directory.CreateDirectory(folder);
        return new LocalFolderCloudProvider(folder);
    }
}
#endif
