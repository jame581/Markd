using Markd.Core.Localization;

namespace Markd.Services;

/// <summary>Writes an export file somewhere the user chooses, or hands it to the share sheet.</summary>
public interface IFileExportService
{
    /// <summary>Returns a short description of where the file went, or null if cancelled.</summary>
    Task<string?> SaveAsync(string fileName, byte[] data);

    Task ShareAsync(string fileName, byte[] data);
}

/// <summary>The share sheet. The file is staged in the cache; earlier staged exports are deleted first.</summary>
public static class ShareSheet
{
    // Essentials' Share copies files into "<root>/<EssentialsFolderHash>/<guid>/<fileName>" before handing
    // them to the target app. That root defaults to (or, once ANDROID's TemporaryLocation is set at startup,
    // still can be) somewhere other than FileSystem.CacheDirectory, so those copies must be cleaned up too.
    private const string EssentialsFolderHash = "2203693cc04e0be7f4f024d5f9499e13";

    /// <summary>Deletes exports staged for earlier shares. Also called at app start so a plain JSON copy does not linger. Never throws.</summary>
    public static void ClearStaged()
    {
        ClearStagedUnder(FileSystem.CacheDirectory);

#if ANDROID
        try
        {
            var externalCache = Platform.AppContext.ExternalCacheDir?.AbsolutePath;
            if (!string.IsNullOrEmpty(externalCache))
                ClearStagedUnder(externalCache);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
#endif
    }

    private static void ClearStagedUnder(string cacheDirectory)
    {
        DeleteMatching(cacheDirectory, "markd-export-*");

        var staged = Path.Combine(cacheDirectory, EssentialsFolderHash);
        foreach (var guidFolder in EnumerateDirectories(staged))
        {
            DeleteMatching(guidFolder, "markd-export-*");
            RemoveIfEmpty(guidFolder);
        }
    }

    private static void DeleteMatching(string directory, string pattern)
    {
        foreach (var file in EnumerateFiles(directory, pattern))
        {
            try { File.Delete(file); }
            catch (IOException) { } // still open in the receiving app; the next start retries
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void RemoveIfEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static IEnumerable<string> EnumerateFiles(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern).ToArray() : [];
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> EnumerateDirectories(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateDirectories(directory).ToArray() : [];
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    public static async Task ShareAsync(string fileName, byte[] data)
    {
        ClearStaged();

        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllBytesAsync(path, data);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = Strings.Export_ShareTitle, File = new ShareFile(path) });
    }
}
