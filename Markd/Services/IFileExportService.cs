namespace Markd.Services;

/// <summary>Writes an export file somewhere the user can find it. Returns a short description of where, or null if cancelled.</summary>
public interface IFileExportService
{
    Task<string?> SaveAsync(string fileName, byte[] data);
}

/// <summary>Fallback: write to the cache and hand the file to the share sheet.</summary>
public sealed class ShareFileExportService : IFileExportService
{
    public async Task<string?> SaveAsync(string fileName, byte[] data)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllBytesAsync(path, data);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = "Markd export", File = new ShareFile(path) });
        return fileName;
    }
}
