using System.Net;
using System.Security.Cryptography;
using System.Text;
using Markd.Core.Sync;
using Markd.Services.Sync;
using Markd.Services.Sync.Cloud;
using Xunit;

namespace Markd.Tests;

public class GoogleOAuthClientTests
{
    private const string Redirect = "http://127.0.0.1:50123/";
    private const string AllScopes = "https://www.googleapis.com/auth/drive.appdata openid https://www.googleapis.com/auth/userinfo.email";

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return await respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (GoogleOAuthClient Client, FakeHandler Handler) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, string? secret = "secret")
    {
        var handler = new FakeHandler(respond);
        return (new GoogleOAuthClient(new HttpClient(handler), "client-id", secret), handler);
    }

    private static (GoogleOAuthClient Client, FakeHandler Handler) Create(HttpStatusCode status, string json) =>
        Create((_, _) => Task.FromResult(Json(status, json)));

    private static string Query(string url, string name)
    {
        var query = new Uri(url).Query.TrimStart('?');
        foreach (var pair in query.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == name)
                return Uri.UnescapeDataString(parts[1]);
        }

        return "<missing>";
    }

    [Fact]
    public void AuthorizationUrl_HasPkceScopesAndOffline()
    {
        var (client, _) = Create(HttpStatusCode.OK, "{}");

        var url = client.BuildAuthorizationUrl(Redirect, "the-challenge", "the-state", "a@x.com", forceConsent: true);

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("code_challenge=the-challenge", url);
        Assert.Contains("access_type=offline", url);
        Assert.Contains("scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fdrive.appdata%20openid%20email", url);
        Assert.Contains("prompt=consent", url);
        Assert.Equal("a@x.com", Query(url, "login_hint"));
        Assert.Equal("client-id", Query(url, "client_id"));
        Assert.Equal(Redirect, Query(url, "redirect_uri"));
        Assert.Equal("code", Query(url, "response_type"));
        Assert.Equal("the-state", Query(url, "state"));

        var plain = client.BuildAuthorizationUrl(Redirect, "c", "s", null, forceConsent: false);
        Assert.DoesNotContain("prompt=", plain);
        Assert.DoesNotContain("login_hint=", plain);
    }

    [Fact]
    public void Pkce_VerifierLengthAndChallenge()
    {
        var (verifier, challenge) = GoogleOAuthClient.CreatePkce();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9._~-]+$", verifier);
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, challenge);
        Assert.NotEqual(verifier, GoogleOAuthClient.CreatePkce().Verifier);
    }

    [Fact]
    public async Task Exchange_ReturnsTokens_AndPostsTheCodeAndVerifier()
    {
        var (client, handler) = Create(HttpStatusCode.OK,
            $$"""{"access_token":"at","refresh_token":"rt","expires_in":3599,"scope":"{{AllScopes}}","token_type":"Bearer"}""");

        var tokens = await client.ExchangeCodeAsync("the-code", "the-verifier", Redirect, CancellationToken.None);

        Assert.Equal("at", tokens.AccessToken);
        Assert.Equal("rt", tokens.RefreshToken);
        Assert.InRange(tokens.ExpiresUtc, DateTime.UtcNow.AddSeconds(3500), DateTime.UtcNow.AddSeconds(3600));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://oauth2.googleapis.com/token", request.RequestUri!.ToString());
        var body = handler.Bodies[0];
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code=the-code", body);
        Assert.Contains("code_verifier=the-verifier", body);
        Assert.Contains("client_id=client-id", body);
        Assert.Contains("client_secret=secret", body);
    }

    [Fact]
    public async Task Exchange_WithoutRefreshToken_ThrowsSyncAuthException()
    {
        var (client, _) = Create(HttpStatusCode.OK,
            $$"""{"access_token":"at","expires_in":3599,"scope":"{{AllScopes}}"}""");

        await Assert.ThrowsAsync<SyncAuthException>(() =>
            client.ExchangeCodeAsync("code", "verifier", Redirect, CancellationToken.None));
    }

    [Fact]
    public async Task Exchange_WithoutDriveScope_ThrowsMissingPermission()
    {
        var (client, _) = Create(HttpStatusCode.OK,
            """{"access_token":"at","refresh_token":"rt","expires_in":3599,"scope":"openid https://www.googleapis.com/auth/userinfo.email"}""");

        await Assert.ThrowsAsync<SignInMissingPermissionException>(() =>
            client.ExchangeCodeAsync("code", "verifier", Redirect, CancellationToken.None));
    }

    [Fact]
    public async Task Exchange_WithoutScopeField_IsAccepted()
    {
        var (client, _) = Create(HttpStatusCode.OK, """{"access_token":"at","refresh_token":"rt","expires_in":3599}""");

        var tokens = await client.ExchangeCodeAsync("code", "verifier", Redirect, CancellationToken.None);

        Assert.Equal("rt", tokens.RefreshToken);
    }

    [Fact]
    public async Task Exchange_InvalidGrant_ThrowsSyncAuthException()
    {
        var (client, _) = Create(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Bad Request"}""");

        await Assert.ThrowsAsync<SyncAuthException>(() =>
            client.ExchangeCodeAsync("code", "verifier", Redirect, CancellationToken.None));
    }

    [Fact]
    public async Task GetAccount_Unauthorized_ThrowsSyncAuthException()
    {
        var (client, _) = Create(HttpStatusCode.Unauthorized, """{"error":"invalid_token"}""");

        await Assert.ThrowsAsync<SyncAuthException>(() => client.GetAccountAsync("at", CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_ReturnsAccessToken_AndPostsTheRefreshToken()
    {
        var (client, handler) = Create(HttpStatusCode.OK,
            $$"""{"access_token":"at2","expires_in":3599,"scope":"{{AllScopes}}"}""");

        var tokens = await client.RefreshAsync("rt", CancellationToken.None);

        Assert.Equal("at2", tokens.AccessToken);
        Assert.Null(tokens.RefreshToken);
        Assert.Contains("grant_type=refresh_token", handler.Bodies[0]);
        Assert.Contains("refresh_token=rt", handler.Bodies[0]);
    }

    [Fact]
    public async Task Refresh_InvalidGrant_ThrowsSyncAuthException()
    {
        var (client, _) = Create(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");

        await Assert.ThrowsAsync<SyncAuthException>(() => client.RefreshAsync("rt", CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_NetworkFailure_ThrowsSyncOfflineException()
    {
        var (failing, _) = Create((_, _) => throw new HttpRequestException("no route"));
        await Assert.ThrowsAsync<SyncOfflineException>(() => failing.RefreshAsync("rt", CancellationToken.None));

        var (timingOut, _) = Create((_, _) => throw new TaskCanceledException("timeout", new TimeoutException()));
        await Assert.ThrowsAsync<SyncOfflineException>(() => timingOut.RefreshAsync("rt", CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_ServerError_ThrowsPlainException()
    {
        var (client, _) = Create(HttpStatusCode.InternalServerError, """{"error":"internal_failure"}""");

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => client.RefreshAsync("rt", CancellationToken.None));

        Assert.IsNotType<SyncAuthException>(ex);
        Assert.IsNotType<SyncOfflineException>(ex);
        Assert.IsNotAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task GetAccount_ReadsSubAndEmail()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """{"sub":"1234567890","email":"a@x.com","email_verified":true}""");

        var account = await client.GetAccountAsync("at", CancellationToken.None);

        Assert.Equal(new CloudAccount("1234567890", "a@x.com"), account);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://openidconnect.googleapis.com/v1/userinfo", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("at", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task CallerCancellation_IsNotOffline()
    {
        using var cts = new CancellationTokenSource();
        var (client, _) = Create(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return Json(HttpStatusCode.OK, "{}");
        });

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RefreshAsync("rt", cts.Token));

        Assert.IsNotType<SyncOfflineException>(ex);
    }
}
