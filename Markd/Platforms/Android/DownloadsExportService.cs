using Android.Content;
using Android.OS;
using Android.Provider;
using Markd.Core.Localization;
using Markd.Services;

namespace Markd.Platforms.Android;

/// <summary>"Writes one file to Downloads": MediaStore on Android 10+, the share sheet on older versions.</summary>
public sealed class DownloadsExportService : IFileExportService
{
    public async Task<string?> SaveAsync(string fileName, byte[] data)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return await new ShareFileExportService().SaveAsync(fileName, data);

        var resolver = Platform.AppContext.ContentResolver!;
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "application/json");
        values.Put(MediaStore.IMediaColumns.RelativePath, global::Android.OS.Environment.DirectoryDownloads);
        values.Put(MediaStore.IMediaColumns.IsPending, 1);

        var uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri, values)
            ?? throw new IOException(Strings.Export_CouldNotCreateFile);

        await using (var stream = resolver.OpenOutputStream(uri) ?? throw new IOException(Strings.Export_CouldNotOpenFile))
            await stream.WriteAsync(data);

        values.Clear();
        values.Put(MediaStore.IMediaColumns.IsPending, 0);
        resolver.Update(uri, values, null, null);
        return string.Format(LocalizationManager.Instance.Culture, Strings.Export_LocationPrefix, fileName);
    }

    public Task ShareAsync(string fileName, byte[] data) => ShareSheet.ShareAsync(fileName, data);
}
