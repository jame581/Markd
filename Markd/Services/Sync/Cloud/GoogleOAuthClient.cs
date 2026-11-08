using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Markd.Core.Sync;

namespace Markd.Services.Sync.Cloud;

/// <summary>Tokens from Google's token endpoint. A refresh usually returns no new refresh token.</summary>
public sealed record GoogleTokens(string AccessToken, string? RefreshToken, DateTime ExpiresUtc);

/// <summary>
/// Google OAuth for an installed app (authorization code with PKCE), without any UI: builds the consent URL, exchanges
/// the code, refreshes and reads the account. Transport failures become <see cref="SyncOfflineException"/>, a refused
/// grant <see cref="SyncAuthException"/>, and the caller's cancellation passes through as OperationCanceledException.
/// </summary>
public sealed class GoogleOAuthClient(HttpClient http, string clientId, string? clientSecret)
{
    public const string DriveAppDataScope = "https://www.googleapis.com/auth/drive.appdata";
    private const string Scopes = DriveAppDataScope + " openid email";
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

    /// <summary>
    /// The consent URL. forceConsent adds prompt=consent, which Google needs to issue a refresh token again when the
    /// user already granted access earlier.
    /// </summary>
    public string BuildAuthorizationUrl(string redirectUri, string codeChallenge, string state, string? loginHint, bool forceConsent)
    {
        var parameters = new List<(string, string)>
        {
            ("client_id", clientId),
            ("redirect_uri", redirectUri),
            ("response_type", "code"),
            ("scope", Scopes),
            ("code_challenge", codeChallenge),
            ("code_challenge_method", "S256"),
            ("state", state),
            ("access_type", "offline"),
        };
        if (forceConsent)
            parameters.Add(("prompt", "consent"));
        if (!string.IsNullOrWhiteSpace(loginHint))
            parameters.Add(("login_hint", loginHint));

        return AuthorizationEndpoint + "?" + string.Join("&",
            parameters.Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));
    }

    /// <summary>A PKCE pair: a 43-character base64url verifier and its S256 challenge.</summary>
    public static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    /// <summary>
    /// Exchanges the authorization code. No refresh token is a <see cref="SyncAuthException"/>; a grant without the
    /// Drive app data scope is a <see cref="SignInMissingPermissionException"/>.
    /// </summary>
    public async Task<GoogleTokens> ExchangeCodeAsync(string code, string verifier, string redirectUri, CancellationToken ct)
    {
        var (tokens, scope) = await RequestTokensAsync(
        [
            new("grant_type", "authorization_code"),
            new("code", code),
            new("code_verifier", verifier),
            new("redirect_uri", redirectUri),
        ], ct);

        // Without a scope field the grant equals the request (RFC 6749 5.1).
        if (scope is not null && !scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(DriveAppDataScope))
            throw new SignInMissingPermissionException();
        if (string.IsNullOrEmpty(tokens.RefreshToken))
            throw new SyncAuthException("Google returned no refresh token.");
        return tokens;
    }

    /// <summary>A new access token for a stored refresh token. A revoked or expired grant is a <see cref="SyncAuthException"/>.</summary>
    public async Task<GoogleTokens> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var (tokens, _) = await RequestTokensAsync(
        [
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
        ], ct);
        return tokens;
    }

    /// <summary>The account behind an access token: the stable subject id and the email.</summary>
    public async Task<CloudAccount> GetAccountAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var (status, root) = await SendAsync(request, ct);
        if (status == HttpStatusCode.Unauthorized)
            throw new SyncAuthException("Google refused the access token.");
        if ((int)status is < 200 or > 299)
            throw new InvalidOperationException($"Reading the Google account failed with HTTP {(int)status}.");

        var id = GetString(root, "sub");
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("The Google account has no id.");
        return new CloudAccount(id, GetString(root, "email"));
    }

    private async Task<(GoogleTokens Tokens, string? Scope)> RequestTokensAsync(
        List<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        form.Add(new("client_id", clientId));
        if (!string.IsNullOrEmpty(clientSecret))
            form.Add(new("client_secret", clientSecret));

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        var (status, root) = await SendAsync(request, ct);
        if ((int)status is < 200 or > 299)
        {
            // Only the error code: the description is Google's text and never needed.
            var error = GetString(root, "error");
            if (error == "invalid_grant")
                throw new SyncAuthException("Google refused the grant.");
            throw new InvalidOperationException($"The Google token request failed with HTTP {(int)status} ({error ?? "no error code"}).");
        }

        var accessToken = GetString(root, "access_token");
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("Google returned no access token.");
        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 3600;
        return (new GoogleTokens(accessToken, GetString(root, "refresh_token"), DateTime.UtcNow.AddSeconds(expiresIn)),
            GetString(root, "scope"));
    }

    /// <summary>Sends and parses the JSON body (an empty object when there is none or it is not JSON).</summary>
    private async Task<(HttpStatusCode Status, JsonElement Root)> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                root = document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : Empty();
            }
            catch (JsonException)
            {
                root = Empty();
            }

            return (response.StatusCode, root);
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
    }

    private static JsonElement Empty()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
