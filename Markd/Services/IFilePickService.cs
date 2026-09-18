using Markd.Core.Localization;

namespace Markd.Services;

public sealed record PickedFile(string FileName, byte[] Data);

public interface IFilePickService
{
    /// <summary>Null when cancelled.</summary>
    Task<PickedFile?> PickImportFileAsync();
}

public sealed class FilePickService : IFilePickService
{
    public const long MaxImportBytes = 50 * 1024 * 1024;

    // Android has no MIME type for .markd, so octet-stream (and */* for providers that report nothing) must stay pickable.
    private static readonly FilePickerFileType ImportTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = ["application/json", "application/octet-stream", "*/*"],
        [DevicePlatform.WinUI] = [".markd", ".json"],
        [DevicePlatform.iOS] = ["public.json", "public.data"],
        [DevicePlatform.MacCatalyst] = ["public.json", "public.data"],
    });

    public async Task<PickedFile?> PickImportFileAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Strings.Import_PickerTitle, FileTypes = ImportTypes });
        if (file is null)
            return null;

        await using var stream = await file.OpenReadAsync();
        if (stream.CanSeek && stream.Length > MaxImportBytes)
            throw new InvalidOperationException(Strings.Import_FileTooLarge);

        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            if (memory.Length + read > MaxImportBytes)
                throw new InvalidOperationException(Strings.Import_FileTooLarge);
            memory.Write(buffer, 0, read);
        }

        return new PickedFile(file.FileName, memory.ToArray());
    }
}
