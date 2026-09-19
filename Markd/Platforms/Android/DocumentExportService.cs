using Android.App;
using Android.Content;
using Android.Provider;
using Markd.Core.Localization;
using Markd.Services;

namespace Markd.Platforms.Android;

/// <summary>
/// "Save to…" through the Storage Access Framework: the system picker lists local folders and every installed
/// document provider (Google Drive, OneDrive, Dropbox), and Markd writes to whatever the user chooses.
/// </summary>
public sealed class DocumentExportService : IFileExportService
{
    public async Task<string?> SaveAsync(string fileName, byte[] data)
    {
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "application/json" : "application/octet-stream");
        intent.PutExtra(Intent.ExtraTitle, fileName);

        var (code, result) = await ActivityResults.StartAsync(intent);
        if (code != Result.Ok || result?.Data is not { } uri)
            return null;

        var resolver = Platform.AppContext.ContentResolver!;
        try
        {
            await using (var stream = OpenForWrite(resolver, uri))
                await stream.WriteAsync(data);
        }
        catch
        {
            // Do not leave an empty document behind at the chosen location.
            try { DocumentsContract.DeleteDocument(resolver, uri); } catch (Exception) { }
            throw;
        }

        return DisplayName(resolver, uri) ?? fileName;
    }

    /// <summary>"wt" truncates; some cloud providers only accept "w", which is equivalent for the freshly created document.</summary>
    private static Stream OpenForWrite(ContentResolver resolver, global::Android.Net.Uri uri)
    {
        try
        {
            return resolver.OpenOutputStream(uri, "wt") ?? throw new IOException(Strings.Export_CouldNotOpenFile);
        }
        catch (Exception ex) when (ex is Java.IO.FileNotFoundException or Java.Lang.IllegalArgumentException)
        {
            return resolver.OpenOutputStream(uri, "w") ?? throw new IOException(Strings.Export_CouldNotOpenFile);
        }
    }

    public Task ShareAsync(string fileName, byte[] data) => ShareSheet.ShareAsync(fileName, data);

    private static string? DisplayName(ContentResolver resolver, global::Android.Net.Uri uri)
    {
        using var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
        return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
    }
}
