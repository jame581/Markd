using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Markd.Core.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Markd.Services.Sync.Cloud;

/// <summary>
/// Sync through <c>markd-sync.markd</c> in the hidden Drive app data folder. Metadata and the conditional update use
/// Drive v2, because v3 ignores If-Match and has no etag: the revision is <c>&lt;fileId&gt;:&lt;v2 etag&gt;</c>, the etag
/// kept exactly as Drive sends it (quotes included). Drive allows two files with one name, so when a create race leaves
/// two, every call uses the oldest (created first, ties by id) and the device that created the newer one deletes it.
/// Transport failures and HttpClient timeouts become <see cref="SyncOfflineException"/>, a refused token (after one
/// forced refresh) or a grant without Drive app data access <see cref="SyncAuthException"/>, and the caller's
/// cancellation passes through as OperationCanceledException.
/// </summary>
public sealed class GoogleDriveProvider(
    HttpClient http, IAccessTokenSource tokens, ILogger<GoogleDriveProvider>? logger = null) : ICloudSyncProvider
{
    private readonly ILogger _logger = logger ?? NullLogger<GoogleDriveProvider>.Instance;

    private const string FileName = "markd-sync.markd";
    private const string FilesV2 = "https://www.googleapis.com/drive/v2/files";
    private const string UploadV2 = "https://www.googleapis.com/upload/drive/v2/files";
    private const string ContentV3 = "https://www.googleapis.com/drive/v3/files";
    private const string CreateV3 = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id";
    private const string MetadataFields = "id,etag,createdDate,modifiedDate";

    // Oldest first, so the canonical file is on the first page even if a create race ever left many copies.
    private static readonly string ListUrl = FilesV2
        + "?spaces=appDataFolder"
        + "&q=" + Uri.EscapeDataString($"title='{FileName}' and trashed=false")
        + "&orderBy=createdDate&maxResults=1000"
        + "&fields=" + Uri.EscapeDataString($"items({MetadataFields})");

    private sealed record DriveFile(string Id, string Etag, DateTime CreatedUtc, DateTime ModifiedUtc)
    {
        public string Revision => Id + ":" + Etag;
    }

    public string Id => "google-drive";

    public string? AccountName => tokens.Account?.Id;

    public string? AccountLabel => tokens.Account?.Email;

    public async Task<RemoteInfo?> GetInfoAsync(CancellationToken ct)
    {
        var file = Oldest(await ListAsync(ct));
        return file is null ? null : new RemoteInfo(file.Revision, file.ModifiedUtc);
    }

    public async Task<RemoteFile?> DownloadAsync(CancellationToken ct)
    {
        // Metadata first: content downloads carry no ETag, and content newer than its label only costs a 412 later,
        // while content older than its label would let an upload overwrite a change this device never saw.
        var file = Oldest(await ListAsync(ct));
        if (file is null)
            return null;

        return await SendAsync<RemoteFile?>(
            () => new HttpRequestMessage(HttpMethod.Get, $"{ContentV3}/{Uri.EscapeDataString(file.Id)}?alt=media"),
            (status, body, _) => status switch
            {
                HttpStatusCode.NotFound => null, // deleted by another device since the listing
                _ when IsSuccess(status) => new RemoteFile(body, file.Revision),
                _ => throw Unexpected("Downloading the sync file", status, body),
            },
            ct);
    }

    public Task<string> UploadAsync(byte[] data, string? expectedRevision, CancellationToken ct) =>
        expectedRevision is null ? CreateAsync(data, ct) : UpdateAsync(data, expectedRevision, ct);

    public async Task DeleteAsync(CancellationToken ct)
    {
        foreach (var file in await ListAsync(ct))
            await DeleteFileAsync(file.Id, ct);
    }

    private async Task<string> CreateAsync(byte[] data, CancellationToken ct)
    {
        if ((await ListAsync(ct)).Count > 0)
            throw new RevisionConflictException();

        var ownId = await SendAsync<string>(
            () =>
            {
                var content = new MultipartContent("related");
                content.Add(new StringContent(
                    $$"""{"name":"{{FileName}}","parents":["appDataFolder"]}""", Encoding.UTF8, "application/json"));
                content.Add(new ByteArrayContent(data) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } });
                return new HttpRequestMessage(HttpMethod.Post, CreateV3) { Content = content };
            },
            (status, body, _) => IsSuccess(status)
                ? GetString(Parse(body), "id") ?? throw new InvalidOperationException("Google Drive returned no file id.")
                : throw Unexpected("Creating the sync file", status, body),
            ct);

        // Another device may have created its file at the same time. List again: the oldest file wins everywhere.
        // The listing can lag behind the create; then read the own file directly. Gone already: another device
        // deleted it, which is a conflict like any other change.
        var listed = await ListAsync(ct);
        var own = listed.FirstOrDefault(f => f.Id == ownId)
            ?? (await ListAsync(ct, ownId)).FirstOrDefault()
            ?? throw new RevisionConflictException();
        if (Oldest([.. listed.Where(f => f.Id != ownId), own])!.Id != ownId)
        {
            await DeleteLostCopyAsync(ownId, ct);
            throw new RevisionConflictException();
        }

        return own.Revision;
    }

    private async Task<string> UpdateAsync(byte[] data, string expectedRevision, CancellationToken ct)
    {
        var separator = expectedRevision.IndexOf(':');
        if (separator <= 0 || separator == expectedRevision.Length - 1)
            throw new RevisionConflictException(); // not a revision of this provider
        var fileId = expectedRevision[..separator];
        var etag = expectedRevision[(separator + 1)..];

        // Never write to a copy other devices do not read.
        if (Oldest(await ListAsync(ct))?.Id != fileId)
            throw new RevisionConflictException();

        return await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, $"{UploadV2}/{Uri.EscapeDataString(fileId)}?uploadType=media")
                {
                    Content = new ByteArrayContent(data) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } },
                };
                request.Headers.TryAddWithoutValidation("If-Match", etag);
                return request;
            },
            (status, body, etagHeader) =>
            {
                // 412: changed since it was read. 404: deleted by another device since it was read.
                if (status is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
                    throw new RevisionConflictException();
                if (!IsSuccess(status))
                    throw Unexpected("Updating the sync file", status, body);
                var newEtag = GetString(Parse(body), "etag") ?? etagHeader
                    ?? throw new InvalidOperationException("Google Drive returned no etag.");
                return fileId + ":" + newEtag;
            },
            ct);
    }

    /// <summary>The losing copy of a create race. Failing to delete it is harmless: every call uses the oldest file.</summary>
    private async Task DeleteLostCopyAsync(string fileId, CancellationToken ct)
    {
        try
        {
            await DeleteFileAsync(fileId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The type only: messages can carry the file id.
            _logger.LogDebug("Deleting the losing copy of a Google Drive create race failed: {Error}", ex.GetType().Name);
        }
    }

    private Task<bool> DeleteFileAsync(string fileId, CancellationToken ct) =>
        SendAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, $"{FilesV2}/{Uri.EscapeDataString(fileId)}"),
            (status, body, _) => IsSuccess(status) || status == HttpStatusCode.NotFound
                ? true
                : throw Unexpected("Deleting the sync file", status, body),
            ct);

    /// <summary>Every sync file in the app folder, or with <paramref name="fileId"/> just that file (empty when gone).</summary>
    private Task<List<DriveFile>> ListAsync(CancellationToken ct, string? fileId = null) =>
        SendAsync<List<DriveFile>>(
            () => new HttpRequestMessage(HttpMethod.Get, fileId is null
                ? ListUrl
                : $"{FilesV2}/{Uri.EscapeDataString(fileId)}?fields={Uri.EscapeDataString(MetadataFields)}"),
            (status, body, _) =>
            {
                if (fileId is not null && status == HttpStatusCode.NotFound)
                    return [];
                if (!IsSuccess(status))
                    throw Unexpected("Listing the sync file", status, body);
                var root = Parse(body);
                if (fileId is not null)
                    return [ReadFile(root)];
                return root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                    ? items.EnumerateArray().Select(ReadFile).ToList()
                    : [];
            },
            ct);

    private static DriveFile? Oldest(IEnumerable<DriveFile> files) =>
        files.OrderBy(f => f.CreatedUtc).ThenBy(f => f.Id, StringComparer.Ordinal).FirstOrDefault();

    private static DriveFile ReadFile(JsonElement item)
    {
        var id = GetString(item, "id");
        var etag = GetString(item, "etag");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(etag)
            || !item.TryGetProperty("createdDate", out var created) || !created.TryGetDateTimeOffset(out var createdAt)
            || !item.TryGetProperty("modifiedDate", out var modified) || !modified.TryGetDateTimeOffset(out var modifiedAt))
            throw new InvalidOperationException("Google Drive returned incomplete file metadata.");
        return new DriveFile(id, etag, createdAt.UtcDateTime, modifiedAt.UtcDateTime);
    }

    /// <summary>
    /// Sends with a fresh bearer token per attempt, retrying once with a forced refresh after a 401, and hands the
    /// status, body and ETag header to <paramref name="handle"/>. The request is built anew for the retry.
    /// </summary>
    private async Task<T> SendAsync<T>(
        Func<HttpRequestMessage> build, Func<HttpStatusCode, byte[], string?, T> handle, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await tokens.GetAccessTokenAsync(forceRefresh: attempt > 0, ct);
            HttpStatusCode status;
            byte[] body;
            string? etagHeader;
            try
            {
                using var request = build();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await http.SendAsync(request, ct);
                status = response.StatusCode;
                body = await response.Content.ReadAsByteArrayAsync(ct);
                etagHeader = response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // HttpClient's own timeout, not the caller.
                throw new SyncOfflineException(null, ex);
            }
            catch (HttpRequestException ex)
            {
                throw new SyncOfflineException(null, ex);
            }

            if (status == HttpStatusCode.Unauthorized)
            {
                if (attempt == 0)
                    continue;
                throw new SyncAuthException("Google Drive refused the access token.");
            }

            if (status == HttpStatusCode.Forbidden && LacksDriveScope(body))
                throw new SyncAuthException("The sign-in does not allow access to the app's Google Drive data.");

            return handle(status, body, etagHeader);
        }
    }

    /// <summary>A grant without drive.appdata (the user unticked it on the consent screen) answers 403.</summary>
    private static bool LacksDriveScope(byte[] body)
    {
        if (!Parse(body).TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            return false;
        return HasReason(error, "errors", "insufficientPermissions") || HasReason(error, "details", "ACCESS_TOKEN_SCOPE_INSUFFICIENT");
    }

    private static bool HasReason(JsonElement error, string listName, string reason) =>
        error.TryGetProperty(listName, out var list) && list.ValueKind == JsonValueKind.Array
        && list.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Object && GetString(e, "reason") == reason);

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and <= 299;

    /// <summary>Only the status and Google's error reason, never the body (it can echo request details).</summary>
    private static InvalidOperationException Unexpected(string action, HttpStatusCode status, byte[] body)
    {
        var root = Parse(body);
        string? reason = null;
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            reason = errors.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Object ? GetString(e, "reason") : null)
                .FirstOrDefault(r => r is not null);
        return new InvalidOperationException($"{action} failed with HTTP {(int)status} ({reason ?? "no reason"}).");
    }

    /// <summary>The JSON object in the body, or an empty object when there is none or it is not JSON.</summary>
    private static JsonElement Parse(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body.Length == 0 ? "{}"u8.ToArray() : body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return document.RootElement.Clone();
        }
        catch (JsonException)
        {
        }

        using var empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
