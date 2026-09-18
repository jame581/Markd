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
    /// <summary>Deletes exports staged for earlier shares. Also called at app start so a plain JSON copy does not linger.</summary>
    public static void ClearStaged()
    {
        foreach (var old in Directory.EnumerateFiles(FileSystem.CacheDirectory, "markd-export-*"))
        {
            try { File.Delete(old); }
            catch (IOException) { } // still open in the receiving app; the next start retries
        }
    }

    public static async Task ShareAsync(string fileName, byte[] data)
    {
        ClearStaged();

        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllBytesAsync(path, data);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = Strings.Export_ShareTitle, File = new ShareFile(path) });
    }
}

/// <summary>Fallback until Task 3: "save" also goes to the share sheet.</summary>
public sealed class ShareFileExportService : IFileExportService
{
    public async Task<string?> SaveAsync(string fileName, byte[] data)
    {
        await ShareSheet.ShareAsync(fileName, data);
        return fileName;
    }

    public Task ShareAsync(string fileName, byte[] data) => ShareSheet.ShareAsync(fileName, data);
}
