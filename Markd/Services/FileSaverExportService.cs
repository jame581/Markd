using CommunityToolkit.Maui.Storage;

namespace Markd.Services;

/// <summary>The system Save As dialog (Windows, iOS Files, macOS). Cloud folders appear there as ordinary locations.</summary>
public sealed class FileSaverExportService : IFileExportService
{
    public async Task<string?> SaveAsync(string fileName, byte[] data)
    {
        using var stream = new MemoryStream(data);
        var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);
        if (result.IsSuccessful)
            return result.FilePath;

        // Closing the dialog is reported as a failed result, not an error worth showing.
        if (result.Exception is null or OperationCanceledException
            || result.Exception.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            return null;

        throw result.Exception;
    }

    public Task ShareAsync(string fileName, byte[] data) => ShareSheet.ShareAsync(fileName, data);
}
