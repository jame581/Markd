using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Markd.Core.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Markd.Services.Sync.Cloud.CloudHttp;

namespace Markd.Services.Sync.Cloud;

/// <summary>
/// Sync through <c>markd-sync.markd</c> in the OneDrive app folder (Microsoft Graph <c>special/approot</c>). The
/// revision is <c>&lt;itemId&gt;:&lt;eTag&gt;</c>, the eTag kept exactly as Graph sends it. Paths are unique in OneDrive,
/// so a create with <c>conflictBehavior=fail</c> cannot race into two files. Updates and downloads address the item by
/// id, so an update never creates a file another device deleted. Content downloads answer 302 to a pre-authenticated
/// URL that is fetched without the bearer token: pass an HttpClient that does not follow redirects itself
/// (<c>AllowAutoRedirect = false</c>). Transport failures and HttpClient timeouts become
/// <see cref="SyncOfflineException"/>, a refused token (after one forced refresh) or a grant without app folder access
/// <see cref="SyncAuthException"/>, and the caller's cancellation passes through as OperationCanceledException.
/// </summary>
public sealed class OneDriveProvider(
    HttpClient http, IAccessTokenSource tokens, ILogger<OneDriveProvider>? logger = null) : ICloudSyncProvider
{
    private readonly ILogger _logger = logger ?? NullLogger<OneDriveProvider>.Instance;

    private const string ItemUrl = "https://graph.microsoft.com/v1.0/me/drive/special/approot:/markd-sync.markd:";
    private const string ItemsUrl = "https://graph.microsoft.com/v1.0/me/drive/items/";
    private const string MetadataUrl = ItemUrl + "?$select=id,eTag,lastModifiedDateTime";
    private const string CreateUrl = ItemUrl + "/content?@microsoft.graph.conflictBehavior=fail";

    /// <summary>Graph's limit for a simple (single request) upload as documented when this was written.</summary>
    private const int MaxSimpleUploadBytes = 4 * 1024 * 1024;

    public const string ProviderId = "onedrive";

    public string Id => ProviderId;

    public string? AccountName => tokens.Account?.Id;

    public string? AccountLabel => tokens.Account?.Email;

    public async Task<RemoteInfo?> GetInfoAsync(CancellationToken ct)
    {
        var item = await GetItemAsync(ct);
        return item is null ? null : new RemoteInfo(item.Value.Revision, item.Value.ModifiedUtc);
    }

    public async Task<RemoteFile?> DownloadAsync(CancellationToken ct)
    {
        // Metadata first: content newer than its label only costs a 412 later, while content older than its label
        // would let an upload overwrite a change this device never saw.
        var item = await GetItemAsync(ct);
        if (item is null)
            return null;

        var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, ItemsUrl + Uri.EscapeDataString(item.Value.Id) + "/content"), ct);
        if (response.Status == HttpStatusCode.NotFound)
            return null; // deleted by another device since the metadata call

        if (IsRedirect(response.Status))
        {
            var location = response.Location;
            if (location is not null && !location.IsAbsoluteUri)
                location = new Uri(new Uri(ItemsUrl), location);
            if (location is null || location.Scheme != Uri.UriSchemeHttps)
            {
                _logger.LogWarning("OneDrive redirected a download to a location that is not HTTPS");
                throw new InvalidOperationException("OneDrive redirected the download to an unusable location.");
            }

            // Pre-authenticated: the bearer token must not travel to the download host.
            response = await CloudHttp.SendAsync(http, new HttpRequestMessage(HttpMethod.Get, location), ct);
            if (response.Status == HttpStatusCode.NotFound)
                return null;
        }

        if (!IsSuccess(response.Status))
            throw Unexpected("Downloading the sync file", response);
        return new RemoteFile(response.Body, item.Value.Revision);
    }

    public async Task<string> UploadAsync(byte[] data, string? expectedRevision, CancellationToken ct)
    {
        if (data.Length > MaxSimpleUploadBytes)
            throw new InvalidOperationException("The sync file is too large for a OneDrive simple upload.");

        string url;
        string? etag = null;
        if (expectedRevision is null)
        {
            url = CreateUrl;
        }
        else
        {
            var separator = expectedRevision.IndexOf(':');
            if (separator <= 0 || separator == expectedRevision.Length - 1)
                throw new RevisionConflictException(); // not a revision of this provider
            url = ItemsUrl + Uri.EscapeDataString(expectedRevision[..separator]) + "/content";
            etag = expectedRevision[(separator + 1)..];
        }

        var response = await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Content = new ByteArrayContent(data) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } },
                };
                if (etag is not null)
                    request.Headers.TryAddWithoutValidation("If-Match", etag);
                return request;
            },
            ct);

        // Create: 409 means the file exists. Update: 412 means changed since it was read, 404 deleted by another
        // device, and 409 is treated the same way because a conflict is always the safe answer.
        if (response.Status == HttpStatusCode.Conflict
            || (etag is not null && response.Status is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound))
            throw new RevisionConflictException();
        if (!IsSuccess(response.Status))
            throw Unexpected(etag is null ? "Creating the sync file" : "Updating the sync file", response);

        var root = Parse(response.Body);
        var id = GetString(root, "id");
        var newEtag = GetString(root, "eTag") ?? response.ETag;
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(newEtag))
            throw new InvalidOperationException("OneDrive returned no item id or eTag.");
        return id + ":" + newEtag;
    }

    public async Task DeleteAsync(CancellationToken ct)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, ItemUrl), ct);
        if (!IsSuccess(response.Status) && response.Status != HttpStatusCode.NotFound)
            throw Unexpected("Deleting the sync file", response);
    }

    private async Task<(string Id, string Revision, DateTime ModifiedUtc)?> GetItemAsync(CancellationToken ct)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, MetadataUrl), ct);
        if (response.Status == HttpStatusCode.NotFound)
            return null;
        if (!IsSuccess(response.Status))
            throw Unexpected("Reading the sync file metadata", response);

        var root = Parse(response.Body);
        var id = GetString(root, "id");
        var etag = GetString(root, "eTag");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(etag)
            || !root.TryGetProperty("lastModifiedDateTime", out var modified) || !modified.TryGetDateTimeOffset(out var modifiedAt))
            throw new InvalidOperationException("OneDrive returned incomplete file metadata.");
        return (id, id + ":" + etag, modifiedAt.UtcDateTime);
    }

    /// <summary>
    /// Sends with a fresh bearer token per attempt, retrying once with a forced refresh after a 401. The request is
    /// built anew for the retry, so it carries the same If-Match and body.
    /// </summary>
    private async Task<CloudResponse> SendAsync(Func<HttpRequestMessage> build, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await tokens.GetAccessTokenAsync(forceRefresh: attempt > 0, ct);
            var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await CloudHttp.SendAsync(http, request, ct);

            if (response.Status == HttpStatusCode.Unauthorized)
            {
                if (attempt == 0)
                {
                    _logger.LogDebug("OneDrive refused the access token, retrying with a refreshed one");
                    continue;
                }

                throw new SyncAuthException("OneDrive refused the access token.");
            }

            // A grant without Files.ReadWrite.AppFolder (or a revoked one) answers 403 accessDenied.
            if (response.Status == HttpStatusCode.Forbidden && ErrorCode(response.Body) == "accessDenied")
                throw new SyncAuthException("The sign-in does not allow access to the app's OneDrive folder.");

            return response;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.Found or HttpStatusCode.MovedPermanently or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>Only the status and Graph's error code, never the message (it can echo request details).</summary>
    private static InvalidOperationException Unexpected(string action, CloudResponse response) =>
        new($"{action} failed with HTTP {(int)response.Status} ({ErrorCode(response.Body) ?? "no code"}).");

    private static string? ErrorCode(byte[] body) =>
        Parse(body).TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            ? GetString(error, "code")
            : null;
}
