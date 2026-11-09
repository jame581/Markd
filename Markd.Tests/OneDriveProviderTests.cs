using System.Net;
using System.Text;
using Markd.Core.Sync;
using Markd.Services.Sync;
using Markd.Services.Sync.Cloud;
using Xunit;

namespace Markd.Tests;

public class OneDriveProviderTests
{
    private const string Base = "https://graph.microsoft.com/v1.0/me/drive/special/approot:/markd-sync.markd:";
    private const string Items = "https://graph.microsoft.com/v1.0/me/drive/items/";
    private const string ETag1 = "\"{1D3C4B2A-0000-4000-8000-00000000000A},1\"";
    private const string ETag2 = "\"{1D3C4B2A-0000-4000-8000-00000000000A},2\"";
    private const string Modified = "2026-11-08T12:30:00Z";

    private sealed class FakeTokens : IAccessTokenSource
    {
        public CloudAccount? Account { get; set; } = new("ms-oid-1", "me@outlook.com");

        public List<bool> Calls { get; } = [];

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
        {
            Calls.Add(forceRefresh);
            return Task.FromResult("token-" + Calls.Count);
        }
    }

    private static (OneDriveProvider Provider, FakeHttpHandler Http, FakeTokens Tokens) Create()
    {
        var http = new FakeHttpHandler();
        var tokens = new FakeTokens();
        return (new OneDriveProvider(new HttpClient(http), tokens), http, tokens);
    }

    private static string Item(string id, string etag, string modified = Modified) =>
        $$"""{"id":"{{id}}","eTag":"{{etag.Replace("\"", "\\\"")}}","lastModifiedDateTime":"{{modified}}"}""";

    private static string Error(string code) => $$$"""{"error":{"code":"{{{code}}}","message":"secret detail"}}""";

    private static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Redirect(string location) =>
        (_, _) => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } };

    [Fact]
    public void Identity_ComesFromTheTokenSource()
    {
        var (provider, _, _) = Create();

        Assert.Equal("onedrive", provider.Id);
        Assert.Equal(OneDriveProvider.ProviderId, provider.Id);
        Assert.Equal("ms-oid-1", provider.AccountName);
        Assert.Equal("me@outlook.com", provider.AccountLabel);
    }

    [Fact]
    public async Task GetInfo_404_ReturnsNull()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.NotFound, Error("itemNotFound"));

        Assert.Null(await provider.GetInfoAsync(TestContext.Current.CancellationToken));

        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(Base + "?$select=id,eTag,lastModifiedDateTime", request.Url);
        Assert.Equal("Bearer token-1", request.Authorization);
    }

    [Fact]
    public async Task Revision_IsItemIdColonETag()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, Item("01ITEM", ETag1));

        var info = await provider.GetInfoAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(info);
        Assert.Equal("01ITEM:" + ETag1, info.Revision);
        Assert.Equal(new DateTime(2026, 11, 8, 12, 30, 0, DateTimeKind.Utc), info.ModifiedUtc);
        Assert.Equal(DateTimeKind.Utc, info.ModifiedUtc.Kind);
    }

    [Fact]
    public async Task Download_ReadsRevisionBeforeContent()
    {
        var (provider, http, _) = Create();
        const string preAuthenticated = "https://public.dm.files.1drv.com/y4mDownload?token=abc";
        http.Respond(HttpStatusCode.OK, Item("01ITEM", ETag1))
            .Respond(Redirect(preAuthenticated))
            .RespondBytes([9, 8, 7]);

        var file = await provider.DownloadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(file);
        Assert.Equal([9, 8, 7], file.Data);
        Assert.Equal("01ITEM:" + ETag1, file.Revision);
        Assert.Equal(3, http.Requests.Count);
        Assert.StartsWith(Base + "?", http.Requests[0].Url);
        Assert.Equal(Items + "01ITEM/content", http.Requests[1].Url);
        Assert.Equal("Bearer token-2", http.Requests[1].Authorization);
        Assert.Equal(preAuthenticated, http.Requests[2].Url);
        Assert.Null(http.Requests[2].Authorization);
    }

    [Fact]
    public async Task Download_DirectContent_IsAccepted()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, Item("01ITEM", ETag1)).RespondBytes([1, 2]);

        var file = await provider.DownloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], file!.Data);
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task Download_NoFile_ReturnsNull()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.NotFound, Error("itemNotFound"));

        Assert.Null(await provider.DownloadAsync(TestContext.Current.CancellationToken));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Download_FileGoneAfterMetadata_ReturnsNull()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, Item("01ITEM", ETag1)).Respond(HttpStatusCode.NotFound, Error("itemNotFound"));

        Assert.Null(await provider.DownloadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Download_RedirectToPlainHttp_IsRefused()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, Item("01ITEM", ETag1)).Respond(Redirect("http://example.com/file"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.DownloadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task Create_UsesConflictBehaviorFail_And409IsRevisionConflict()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.Conflict, Error("nameAlreadyExists"));

        await Assert.ThrowsAsync<RevisionConflictException>(
            () => provider.UploadAsync(Encoding.UTF8.GetBytes("PAYLOAD"), null, TestContext.Current.CancellationToken));

        var put = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal(Base + "/content?@microsoft.graph.conflictBehavior=fail", put.Url);
        Assert.Null(put.IfMatch);
        Assert.Equal("PAYLOAD", Encoding.UTF8.GetString(put.Body));
        Assert.Equal("application/octet-stream", put.ContentType);
    }

    [Fact]
    public async Task Create_ReturnsTheRevisionFromTheResponse()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.Created, Item("01NEW", ETag1));

        var revision = await provider.UploadAsync([1], null, TestContext.Current.CancellationToken);

        Assert.Equal("01NEW:" + ETag1, revision);
    }

    [Fact]
    public async Task Update_SendsIfMatch_And412IsRevisionConflict()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.PreconditionFailed, Error("resourceModified"));

        await Assert.ThrowsAsync<RevisionConflictException>(
            () => provider.UploadAsync([5, 6], "01ITEM:" + ETag1, TestContext.Current.CancellationToken));

        var put = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal(Items + "01ITEM/content", put.Url);
        Assert.Equal(ETag1, put.IfMatch);
        Assert.Equal([5, 6], put.Body);
    }

    [Fact]
    public async Task Update_409_IsRevisionConflict()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.Conflict, Error("resourceModified"));

        await Assert.ThrowsAsync<RevisionConflictException>(
            () => provider.UploadAsync([1], "01ITEM:" + ETag1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_BodyWithoutETag_UsesTheETagHeader()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, """{"id":"01ITEM"}""", etagHeader: ETag2);

        var revision = await provider.UploadAsync([1], "01ITEM:" + ETag1, TestContext.Current.CancellationToken);

        Assert.Equal("01ITEM:" + ETag2, revision);
    }

    [Fact]
    public async Task Update_404_IsRevisionConflict()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.NotFound, Error("itemNotFound"));

        await Assert.ThrowsAsync<RevisionConflictException>(
            () => provider.UploadAsync([1], "01ITEM:" + ETag1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_ForeignRevision_IsRevisionConflictWithoutWriting()
    {
        var (provider, http, _) = Create();

        await Assert.ThrowsAsync<RevisionConflictException>(
            () => provider.UploadAsync([1], "no-separator", TestContext.Current.CancellationToken));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Upload_ReturnsTheSameRevisionAsTheNextGetInfo()
    {
        var (provider, http, _) = Create();
        var ct = TestContext.Current.CancellationToken;
        http.Respond(HttpStatusCode.OK, Item("01ITEM", ETag2), etagHeader: ETag2)
            .Respond(HttpStatusCode.OK, Item("01ITEM", ETag2));

        var revision = await provider.UploadAsync([1], "01ITEM:" + ETag1, ct);
        var info = await provider.GetInfoAsync(ct);

        Assert.Equal("01ITEM:" + ETag2, revision);
        Assert.Equal(info!.Revision, revision);
    }

    [Fact]
    public async Task Upload_Over4MB_IsRefusedWithoutARequest()
    {
        var (provider, http, _) = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.UploadAsync(new byte[4 * 1024 * 1024 + 1], null, TestContext.Current.CancellationToken));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Unauthorized_RetriesOnceWithForcedRefresh()
    {
        var (provider, http, tokens) = Create();
        http.Respond(HttpStatusCode.Unauthorized, Error("InvalidAuthenticationToken"))
            .Respond(HttpStatusCode.NotFound, Error("itemNotFound"));

        Assert.Null(await provider.GetInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal([false, true], tokens.Calls);
        Assert.Equal("Bearer token-1", http.Requests[0].Authorization);
        Assert.Equal("Bearer token-2", http.Requests[1].Authorization);
    }

    [Fact]
    public async Task Update_Unauthorized_RetriesWithTheSameIfMatchAndBody()
    {
        var (provider, http, tokens) = Create();
        http.Respond(HttpStatusCode.Unauthorized).Respond(HttpStatusCode.OK, Item("01ITEM", ETag2));

        var revision = await provider.UploadAsync([5, 6, 7], "01ITEM:" + ETag1, TestContext.Current.CancellationToken);

        Assert.Equal("01ITEM:" + ETag2, revision);
        Assert.Equal([false, true], tokens.Calls);
        Assert.Equal(2, http.Requests.Count);
        Assert.Equal("Bearer token-2", http.Requests[1].Authorization);
        Assert.All(http.Requests, put =>
        {
            Assert.Equal(Items + "01ITEM/content", put.Url);
            Assert.Equal(ETag1, put.IfMatch);
            Assert.Equal([5, 6, 7], put.Body);
        });
    }

    [Fact]
    public async Task Unauthorized_Twice_MapsToSyncAuthException()
    {
        var (provider, http, tokens) = Create();
        http.Respond(HttpStatusCode.Unauthorized).Respond(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<SyncAuthException>(() => provider.GetInfoAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, tokens.Calls.Count);
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task AccessDenied_MapsToSyncAuthException()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.Forbidden, Error("accessDenied"));

        await Assert.ThrowsAsync<SyncAuthException>(() => provider.GetInfoAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OtherFailure_ReportsStatusAndCodeOnly()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.Forbidden, Error("quotaLimitReached"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetInfoAsync(TestContext.Current.CancellationToken));
        Assert.Contains("403", ex.Message);
        Assert.Contains("quotaLimitReached", ex.Message);
        Assert.DoesNotContain("secret detail", ex.Message);
    }

    [Fact]
    public async Task NetworkFailure_MapsToOffline()
    {
        var (provider, http, _) = Create();
        http.Throw(new HttpRequestException("no route"));

        await Assert.ThrowsAsync<SyncOfflineException>(() => provider.GetInfoAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HttpClientTimeout_MapsToOffline()
    {
        var (provider, http, _) = Create();
        http.Throw(new TaskCanceledException("timeout"));

        await Assert.ThrowsAsync<SyncOfflineException>(() => provider.GetInfoAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PreAuthenticatedDownloadFailure_MapsToOffline()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, Item("01ITEM", ETag1))
            .Respond(Redirect("https://public.dm.files.1drv.com/x"))
            .Throw(new HttpRequestException("reset"));

        await Assert.ThrowsAsync<SyncOfflineException>(() => provider.DownloadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CallerCancellation_IsNotOffline()
    {
        var (provider, http, _) = Create();
        using var cts = new CancellationTokenSource();
        http.Respond((_, _) =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetInfoAsync(cts.Token));
        Assert.IsNotType<SyncOfflineException>(ex);
    }

    [Fact]
    public async Task Delete_404_IsNotAnError()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.NotFound, Error("itemNotFound"));

        await provider.DeleteAsync(TestContext.Current.CancellationToken);

        var delete = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Delete, delete.Method);
        Assert.Equal(Base, delete.Url);
    }
}
