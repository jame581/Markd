using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Markd.Core.Localization;
using Markd.Core.Sync;
using Markd.Services.Sync;
using Markd.Services.Sync.Cloud;
using Microsoft.Extensions.Logging;

namespace Markd.Platforms.Windows
{
    /// <summary>
    /// Google sign-in on Windows: the system browser with a loopback redirect to 127.0.0.1 and PKCE. The refresh token,
    /// account id and email live in a DPAPI file (google-token.bin) beside the sync passphrase file, since SecureStorage
    /// is unverified on the unpackaged build. The access token is only kept in memory.
    /// </summary>
    public sealed class GoogleWindowsAuthService : ICloudAuthService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Markd.Sync.GoogleToken");
        private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);
        private const int ListenAttempts = 10;

        private readonly string _path;
        private readonly GoogleOAuthClient _client;
        private readonly ILogger<GoogleWindowsAuthService> _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private bool _loaded;
        private StoredToken? _stored;
        private string? _accessToken;
        private DateTime _accessExpiresUtc;

        public GoogleWindowsAuthService(string dataDirectory, ILogger<GoogleWindowsAuthService> logger)
            : this(dataDirectory, new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, logger)
        {
        }

        public GoogleWindowsAuthService(string dataDirectory, HttpClient http, ILogger<GoogleWindowsAuthService> logger)
        {
            _path = Path.Combine(dataDirectory, "google-token.bin");
            _client = new GoogleOAuthClient(http, CloudConfig.GoogleDesktopClientId, CloudConfig.GoogleDesktopClientSecret);
            _logger = logger;
        }

        public CloudAccount? Account => _stored is { } stored ? new CloudAccount(stored.AccountId, stored.Email) : null;

        public bool SignInLeavesApp => true;

        public async Task LoadAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_loaded)
                    return;
                _loaded = true;
                if (!File.Exists(_path))
                    return;

                var bytes = ProtectedData.Unprotect(await File.ReadAllBytesAsync(_path), Entropy, DataProtectionScope.CurrentUser);
                var stored = JsonSerializer.Deserialize<StoredToken>(bytes);
                _stored = stored is { RefreshToken.Length: > 0, AccountId.Length: > 0 } ? stored : null;
            }
            catch (Exception ex)
            {
                // A damaged file or one from another Windows account: signed out, so sync asks to sign in again.
                _logger.LogWarning("Reading the Google token file failed: {Error}", ex.GetType().Name);
                _stored = null;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
        {
            await LoadAsync();
            await _gate.WaitAsync(ct);
            try
            {
                if (_stored is not { } stored)
                    throw new SyncAuthException("Not signed in to Google.");
                if (!forceRefresh && _accessToken is { } cached && DateTime.UtcNow < _accessExpiresUtc - ExpiryMargin)
                    return cached;

                var tokens = await _client.RefreshAsync(stored.RefreshToken, ct);
                if (!string.IsNullOrEmpty(tokens.RefreshToken) && tokens.RefreshToken != stored.RefreshToken)
                {
                    // Kept in memory even when the file cannot be written, so this session goes on with the new one.
                    var rotated = stored with { RefreshToken = tokens.RefreshToken };
                    _stored = rotated;
                    try
                    {
                        await SaveAsync(rotated);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Saving the rotated Google refresh token failed: {Error}", ex.GetType().Name);
                    }
                }

                _accessToken = tokens.AccessToken;
                _accessExpiresUtc = tokens.ExpiresUtc;
                return tokens.AccessToken;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<CloudAccount?> SignInAsync(string? loginHint, CancellationToken ct)
        {
            await LoadAsync();
            if (ct.IsCancellationRequested)
                return null;

            using var listener = StartListener(out var redirectUri);
            // Stopping the listener ends the pending GetContextAsync, so Cancel on the waiting panel returns at once.
            using var stop = ct.Register(() => StopQuietly(listener));
            try
            {
                var (verifier, challenge) = GoogleOAuthClient.CreatePkce();
                var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                var url = _client.BuildAuthorizationUrl(redirectUri, challenge, state, loginHint, forceConsent: true);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

                while (true)
                {
                    var context = await listener.GetContextAsync();
                    var query = context.Request.QueryString;
                    if (context.Request.Url?.AbsolutePath != "/" || query["state"] is not { } returnedState)
                    {
                        // The browser also asks for a favicon; nothing else concerns this sign-in.
                        Respond(context, HttpStatusCode.NotFound, null);
                        continue;
                    }

                    if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(returnedState), Encoding.ASCII.GetBytes(state)))
                    {
                        // A stale tab from an earlier attempt: keep waiting for this attempt's redirect.
                        _logger.LogInformation("Ignored a Google redirect from another sign-in attempt");
                        Respond(context, HttpStatusCode.BadRequest, Strings.Sync_BrowserFailed);
                        continue;
                    }

                    if (query["error"] is { } error)
                    {
                        Respond(context, HttpStatusCode.OK, Strings.Sync_BrowserFailed);
                        if (error == "access_denied")
                            return null;
                        throw new SyncAuthException("Google refused the sign-in.");
                    }

                    if (query["code"] is not { Length: > 0 } code)
                    {
                        Respond(context, HttpStatusCode.BadRequest, Strings.Sync_BrowserFailed);
                        throw new SyncAuthException("Google returned no authorization code.");
                    }

                    try
                    {
                        var account = await CompleteAsync(code, verifier, redirectUri, ct);
                        Respond(context, HttpStatusCode.OK, Strings.Sync_BrowserDone);
                        return account;
                    }
                    catch
                    {
                        Respond(context, HttpStatusCode.OK, Strings.Sync_BrowserFailed);
                        throw;
                    }
                }
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                // Cancelled (the stopped listener throws HttpListenerException or ObjectDisposedException, the token
                // calls OperationCanceledException): null, as the contract says.
                return null;
            }
            finally
            {
                StopQuietly(listener);
            }
        }

        public async Task SignOutAsync()
        {
            await _gate.WaitAsync();
            try
            {
                _stored = null;
                _accessToken = null;
                _loaded = true;
                File.Delete(_path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Deleting the Google token file failed: {Error}", ex.GetType().Name);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Exchanges the code, reads the account and stores both. Nothing is stored when any step fails.</summary>
        private async Task<CloudAccount> CompleteAsync(string code, string verifier, string redirectUri, CancellationToken ct)
        {
            var tokens = await _client.ExchangeCodeAsync(code, verifier, redirectUri, ct);
            var account = await _client.GetAccountAsync(tokens.AccessToken, ct);
            var stored = new StoredToken(tokens.RefreshToken!, account.Id, account.Email);

            await _gate.WaitAsync(ct);
            try
            {
                await SaveAsync(stored);
                _stored = stored;
                _accessToken = tokens.AccessToken;
                _accessExpiresUtc = tokens.ExpiresUtc;
            }
            finally
            {
                _gate.Release();
            }

            return account;
        }

        private async Task SaveAsync(StoredToken stored)
        {
            var protectedBytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(stored), Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await File.WriteAllBytesAsync(temp, protectedBytes);
            File.Move(temp, _path, overwrite: true);
        }

        /// <summary>A listener on a free loopback port. Another process can take the port in between, so it retries.</summary>
        private static HttpListener StartListener(out string redirectUri)
        {
            for (var attempt = 1; ; attempt++)
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();

                redirectUri = $"http://127.0.0.1:{port}/";
                var listener = new HttpListener();
                listener.Prefixes.Add(redirectUri);
                try
                {
                    listener.Start();
                    return listener;
                }
                catch (HttpListenerException) when (attempt < ListenAttempts)
                {
                    listener.Close();
                }
            }
        }

        private static void StopQuietly(HttpListener listener)
        {
            try
            {
                if (listener.IsListening)
                    listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>A minimal page in the app's language, or an empty response when there is no message.</summary>
        private void Respond(HttpListenerContext context, HttpStatusCode status, string? message)
        {
            try
            {
                var response = context.Response;
                response.StatusCode = (int)status;
                if (message is not null)
                {
                    var culture = LocalizationManager.Instance.Culture;
                    var text = WebUtility.HtmlEncode(message);
                    var html = $"<!doctype html><html lang=\"{culture.TwoLetterISOLanguageName}\"><head><meta charset=\"utf-8\">"
                        + $"<title>Markd</title></head><body style=\"font-family:sans-serif;margin:3em;text-align:center\">"
                        + $"<p>{text}</p></body></html>";
                    var bytes = Encoding.UTF8.GetBytes(html);
                    response.ContentType = "text/html; charset=utf-8";
                    response.ContentLength64 = bytes.Length;
                    response.OutputStream.Write(bytes);
                }

                response.Close();
            }
            catch (Exception ex)
            {
                // The browser tab may be gone; the sign-in result does not depend on it.
                _logger.LogInformation("Answering the browser failed: {Error}", ex.GetType().Name);
            }
        }

        private sealed record StoredToken(string RefreshToken, string AccountId, string? Email);
    }
}
