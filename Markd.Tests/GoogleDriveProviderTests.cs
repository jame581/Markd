using System.Net;
using System.Text;
using Markd.Core.Sync;
using Markd.Services.Sync;
using Markd.Services.Sync.Cloud;
using Xunit;

namespace Markd.Tests;

public class GoogleDriveProviderTests
{
    private const string ListPrefix = "https://www.googleapis.com/drive/v2/files?";
    private const string OldDate = "2026-11-01T08:00:00.000Z";
    private const string NewDate = "2026-11-02T08:00:00.000Z";

    private sealed class FakeTokens : IAccessTokenSource
    {
        public CloudAccount? Account { get; set; } = new("sub-1", "me@gmail.com");

        public List<bool> Calls { get; } = [];

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
        {
            Calls.Add(forceRefresh);
            return Task.FromResult("token-" + Calls.Count);
        }
    }

    private static (GoogleDriveProvider Provider, FakeHttpHandler Http, FakeTokens Tokens) Create()
    {
        var http = new FakeHttpHandler();
        var tokens = new FakeTokens();
        return (new GoogleDriveProvider(new HttpClient(http), tokens), http, tokens);
    }

    private static string Item(string id, string etag, string created = OldDate, string modified = NewDate) =>
        $$"""{"id":"{{id}}","etag":"{{etag.Replace("\"", "\\\"")}}","createdDate":"{{created}}","modifiedDate":"{{modified}}"}""";

    private static string List(params string[] items) => $$"""{"items":[{{string.Join(",", items)}}]}""";

    private static string Query(string url, string name)
    {
        foreach (var pair in new Uri(url).Query.TrimStart('?').Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == name)
                return Uri.UnescapeDataString(parts[1]);
        }

        return "<missing>";
    }

    private static string Error(int code, string reason) =>
        $$$"""{"error":{"code":{{{code}}},"errors":[{"domain":"global","reason":"{{{reason}}}"}]}}""";

    [Fact]
    public void Identity_ComesFromTheTokenSource()
    {
        var (provider, _, _) = Create();

        Assert.Equal("google-drive", provider.Id);
        Assert.Equal("sub-1", provider.AccountName);
        Assert.Equal("me@gmail.com", provider.AccountLabel);
    }

    [Fact]
    public async Task GetInfo_NoFile_ReturnsNull()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List());

        Assert.Null(await provider.GetInfoAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetInfo_UsesAppDataFolderQuery()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List());

        await provider.GetInfoAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.StartsWith(ListPrefix, request.Url);
        Assert.Equal("appDataFolder", Query(request.Url, "spaces"));
        Assert.Equal("title='markd-sync.markd' and trashed=false", Query(request.Url, "q"));
        Assert.Equal("items(id,etag,createdDate,modifiedDate)", Query(request.Url, "fields"));
        Assert.Equal("Bearer token-1", request.Authorization);
    }

    [Fact]
    public async Task Revision_IsFileIdColonEtag()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List(Item("abc", "\"MTc5NDE3OTk5ODI4MA\"", modified: "2026-11-08T12:30:00.000Z")));

        var info = await provider.GetInfoAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(info);
        Assert.Equal("abc:\"MTc5NDE3OTk5ODI4MA\"", info.Revision);
        Assert.Equal(new DateTime(2026, 11, 8, 12, 30, 0, DateTimeKind.Utc), info.ModifiedUtc);
        Assert.Equal(DateTimeKind.Utc, info.ModifiedUtc.Kind);
    }

    [Fact]
    public async Task TwoFiles_EveryCallUsesTheOldest()
    {
        var (provider, http, _) = Create();
        var ct = TestContext.Current.CancellationToken;
        var twoFiles = List(Item("newer", "\"N\"", created: NewDate), Item("older", "\"O\"", created: OldDate));

        http.Respond(HttpStatusCode.OK, twoFiles);
        var info = await provider.GetInfoAsync(ct);
        Assert.Equal("older:\"O\"", info!.Revision);

        http.Respond(HttpStatusCode.OK, twoFiles).RespondBytes([1, 2, 3]);
        var file = await provider.DownloadAsync(ct);
        Assert.Equal("older:\"O\"", file!.Revision);
        Assert.StartsWith("https://www.googleapis.com/drive/v3/files/older?", http.Requests[^1].Url);

        http.Respond(HttpStatusCode.OK, twoFiles).Respond(HttpStatusCode.OK, Item("older", "\"O2\""));
        await provider.UploadAsync([4], "older:\"O\"", ct);
        Assert.Equal("https://www.googleapis.com/upload/drive/v2/files/older?uploadType=media", http.Requests[^1].Url);
    }

    [Fact]
    public async Task SameCreatedDate_TieBrokenById()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List(Item("b", "\"B\""), Item("a", "\"A\"")));

        var info = await provider.GetInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("a:\"A\"", info!.Revision);
    }

    [Fact]
    public async Task Download_ReadsRevisionBeforeContent()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List(Item("f1", "\"E1\""))).RespondBytes([9, 8, 7]);

        var file = await provider.DownloadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(file);
        Assert.Equal([9, 8, 7], file.Data);
        Assert.Equal("f1:\"E1\"", file.Revision);
        Assert.Equal(2, http.Requests.Count);
        Assert.StartsWith(ListPrefix, http.Requests[0].Url);
        Assert.Equal("https://www.googleapis.com/drive/v3/files/f1?alt=media", http.Requests[1].Url);
        Assert.Equal("Bearer token-2", http.Requests[1].Authorization);
    }

    [Fact]
    public async Task Download_NoFile_ReturnsNull()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List());

        Assert.Null(await provider.DownloadAsync(TestContext.Current.CancellationToken));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Download_FileGoneAfterListing_ReturnsNull()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List(Item("f1", "\"E1\""))).Respond(HttpStatusCode.NotFound, Error(404, "notFound"));

        Assert.Null(await provider.DownloadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_WhenAFileExists_ThrowsRevisionConflict()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List(Item("f1", "\"E1\"")));

        await Assert.ThrowsAsync<RevisionConflictException>(
            () => provider.UploadAsync([1], null, TestContext.Current.CancellationToken));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Create_UploadsIntoTheAppDataFolder()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List())
            .Respond(HttpStatusCode.OK, """{"id":"mine"}""")
            .Respond(HttpStatusCode.OK, List(Item("mine", "\"M1\"")));

        var revision = await provider.UploadAsync(Encoding.UTF8.GetBytes("PAYLOAD"), null, TestContext.Current.CancellationToken);

        Assert.Equal("mine:\"M1\"", revision);
        var create = http.Requests[1];
        Assert.Equal(HttpMethod.Post, create.Method);
        Assert.StartsWith("https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart", create.Url);
        Assert.StartsWith("multipart/related", create.ContentType);
        var body = Encoding.UTF8.GetString(create.Body);
        Assert.Contains("""{"name":"markd-sync.markd","parents":["appDataFolder"]}""", body);
        Assert.Contains("PAYLOAD", body);
    }

    [Fact]
    public async Task Create_LosesRace_DeletesOwnCopyAndThrows()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List())
            .Respond(HttpStatusCode.OK, """{"id":"mine"}""")
            .Respond(HttpStatusCode.OK, List(Item("mine", "\"M1\"", created: NewDate), Item("theirs", "\"T1\"", created: OldDate)))
            .Respond(HttpStatusCode.NoContent);

        await Assert.ThrowsAsync<RevisionConflictException>(
            () => provider.UploadAsync([1], null, TestContext.Current.CancellationToken));

        var delete = Assert.Single(http.Requests, r => r.Method == HttpMethod.Delete);
        Assert.Equal("https://www.googleapis.com/drive/v2/files/mine", delete.Url);
        Assert.Equal(0, http.Remaining);
    }

    [Fact]
    public async Task Create_OwnFileMissingFromTheList_ReadsItsMetadata()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List())
            .Respond(HttpStatusCode.OK, """{"id":"mine"}""")
            .Respond(HttpStatusCode.OK, List())
            .Respond(HttpStatusCode.OK, Item("mine", "\"M1\""));

        var revision = await provider.UploadAsync([1], null, TestContext.Current.CancellationToken);

        Assert.Equal("mine:\"M1\"", revision);
        Assert.StartsWith("https://www.googleapis.com/drive/v2/files/mine?", http.Requests[3].Url);
    }

    [Fact]
    public async Task Update_SendsIfMatch_AndMaps412ToRevisionConflict()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List(Item("f1", "\"E2\"")))
            .Respond(HttpStatusCode.PreconditionFailed, Error(412, "conditionNotMet"));

        await Assert.ThrowsAsync<RevisionConflictException>(
            () => provider.UploadAsync([5, 6], "f1:\"E1\"", TestContext.Current.CancellationToken));

        var put = http.Requests[1];
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("https://www.googleapis.com/upload/drive/v2/files/f1?uploadType=media", put.Url);
        Assert.Equal("\"E1\"", put.IfMatch);
        Assert.Equal([5, 6], put.Body);
    }

    [Fact]
    public async Task Update_404_IsRevisionConflict()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List(Item("f1", "\"E1\"")))
            .Respond(HttpStatusCode.NotFound, Error(404, "notFound"));

        await Assert.ThrowsAsync<RevisionConflictException>(
            () => provider.UploadAsync([1], "f1:\"E1\"", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_WhenTheFileIsGone_IsRevisionConflictWithoutWriting()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List(Item("other", "\"X\"")));

        await Assert.ThrowsAsync<RevisionConflictException>(
            () => provider.UploadAsync([1], "f1:\"E1\"", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(http.Requests, r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Upload_ReturnsTheSameRevisionAsTheNextGetInfo()
    {
        var (provider, http, _) = Create();
        var ct = TestContext.Current.CancellationToken;
        http.Respond(HttpStatusCode.OK, List(Item("f1", "\"E1\"")))
            .Respond(HttpStatusCode.OK, Item("f1", "\"E2\""), etagHeader: "\"E2\"")
            .Respond(HttpStatusCode.OK, List(Item("f1", "\"E2\"")));

        var revision = await provider.UploadAsync([1], "f1:\"E1\"", ct);
        var info = await provider.GetInfoAsync(ct);

        Assert.Equal("f1:\"E2\"", revision);
        Assert.Equal(info!.Revision, revision);
    }

    [Fact]
    public async Task Unauthorized_RetriesOnceWithForcedRefresh()
    {
        var (provider, http, tokens) = Create();
        http.Respond(HttpStatusCode.Unauthorized).Respond(HttpStatusCode.OK, List());

        Assert.Null(await provider.GetInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal([false, true], tokens.Calls);
        Assert.Equal("Bearer token-1", http.Requests[0].Authorization);
        Assert.Equal("Bearer token-2", http.Requests[1].Authorization);
    }

    [Fact]
    public async Task Unauthorized_MapsToSyncAuthException()
    {
        var (provider, http, tokens) = Create();
        http.Respond(HttpStatusCode.Unauthorized).Respond(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<SyncAuthException>(() => provider.GetInfoAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, tokens.Calls.Count);
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task InsufficientPermissions_MapsToSyncAuthException()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.Forbidden, Error(403, "insufficientPermissions"));

        await Assert.ThrowsAsync<SyncAuthException>(() => provider.GetInfoAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OtherForbidden_IsNotAnAuthFailure()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.Forbidden, Error(403, "userRateLimitExceeded"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetInfoAsync(TestContext.Current.CancellationToken));
        Assert.Contains("403", ex.Message);
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
    public async Task Delete_RemovesAllCopies()
    {
        var (provider, http, _) = Create();
        http.Respond(HttpStatusCode.OK, List(Item("a", "\"A\""), Item("b", "\"B\"", created: NewDate)))
            .Respond(HttpStatusCode.NoContent)
            .Respond(HttpStatusCode.NotFound, Error(404, "notFound"));

        await provider.DeleteAsync(TestContext.Current.CancellationToken);

        var deletes = http.Requests.Where(r => r.Method == HttpMethod.Delete).Select(r => r.Url).Order().ToList();
        Assert.Equal(["https://www.googleapis.com/drive/v2/files/a", "https://www.googleapis.com/drive/v2/files/b"], deletes);
    }
}
