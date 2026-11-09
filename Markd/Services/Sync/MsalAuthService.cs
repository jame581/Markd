using System.Net;
using System.Net.Sockets;
using Markd.Core.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
#if WINDOWS
using Markd.Core.Localization;
using Microsoft.Identity.Client.Extensions.Msal;
#endif

namespace Markd.Services.Sync;

/// <summary>
/// Microsoft sign-in (personal accounts) through MSAL and the system browser: a custom tab on Android with the
/// <c>msal{client id}://auth</c> redirect, the default browser with a loopback redirect on Windows. MSAL keeps the
/// tokens: in its own SharedPreferences cache on Android and in a DPAPI file (msal-cache.bin) beside the sync passphrase
/// file on Windows. The account id is the home account's object id, the same on every device. Signing out removes the
/// accounts from the cache and never revokes anything.
/// </summary>
public sealed class MsalAuthService : ICloudAuthService
{
    private const string Authority = "consumers";
    private const string CacheFileName = "msal-cache.bin";
    private static readonly string[] Scopes = ["Files.ReadWrite.AppFolder", "User.Read"];

    private readonly string _dataDirectory;
    private readonly INetworkStatus _network;
    private readonly ILogger<MsalAuthService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _loaded;
    private IPublicClientApplication? _app;
    private IAccount? _msalAccount;
    private CloudAccount? _account;

    /// <param name="dataDirectory">Where the Windows token cache lives; unused on Android, where MSAL keeps its own.</param>
    public MsalAuthService(string dataDirectory, INetworkStatus network, ILogger<MsalAuthService> logger)
    {
        _dataDirectory = dataDirectory;
        _network = network;
        _logger = logger;
    }

    public CloudAccount? Account => _account;

    public bool SignInLeavesApp => true;

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_loaded)
                return;
            _loaded = true;

            var app = PublicClientApplicationBuilder.Create(CloudConfig.MicrosoftClientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, Authority)
                .WithRedirectUri(RedirectUri)
                .Build();
#if WINDOWS
            try
            {
                var storage = new StorageCreationPropertiesBuilder(CacheFileName, _dataDirectory).Build();
                var cacheHelper = await MsalCacheHelper.CreateAsync(storage);
                cacheHelper.RegisterCache(app.UserTokenCache);
            }
            catch (Exception ex)
            {
                // Without the file the sign-in still works for this session; the next start asks again.
                _logger.LogWarning("Opening the Microsoft token cache failed: {Error}", ex.GetType().Name);
            }
#endif
            _app = app;
            SetAccount((await app.GetAccountsAsync()).FirstOrDefault());
        }
        catch (Exception ex)
        {
            // A damaged cache: signed out, so sync asks to sign in again.
            _logger.LogWarning("Reading the Microsoft account failed: {Error}", ex.GetType().Name);
            SetAccount(null);
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
            if (_app is not { } app || _msalAccount is not { } account)
                throw new SyncAuthException("Not signed in to Microsoft.");

            var result = await app.AcquireTokenSilent(Scopes, account)
                .WithForceRefresh(forceRefresh)
                .ExecuteAsync(ct);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogInformation("Microsoft needs the user to sign in again ({Code})", ex.ErrorCode);
            throw new SyncAuthException("Microsoft needs the user to sign in again.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // MSAL can wrap the caller's cancellation; the contract passes it through as itself.
            throw new OperationCanceledException(ct);
        }
        catch (Exception ex) when (ex is not SyncAuthException)
        {
            if (IsOffline(ex))
                throw new SyncOfflineException();
            // Neither offline nor a sign-in problem (a service outage, a broken cache): a plain failure, so sync
            // reports an error and tries again later instead of asking for a sign-in that would not help.
            var code = (ex as MsalException)?.ErrorCode ?? ex.GetType().Name;
            _logger.LogWarning("Getting a Microsoft access token failed: {Code}", code);
            throw new InvalidOperationException($"Getting a Microsoft access token failed ({code}).");
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

        Task<AuthenticationResult>? pending = null;
        try
        {
            var app = _app ?? throw new SyncAuthException("Microsoft sign-in is not available.");
            var builder = app.AcquireTokenInteractive(Scopes)
                .WithUseEmbeddedWebView(false);
            if (!string.IsNullOrWhiteSpace(loginHint))
                builder = builder.WithLoginHint(loginHint);
#if ANDROID
            var activity = Platform.CurrentActivity ?? throw new SyncAuthException("No activity to sign in from.");
            builder = builder.WithParentActivityOrWindow(activity);
#elif WINDOWS
            builder = builder.WithSystemWebViewOptions(BrowserPages());
#endif

            // MSAL on Android only returns when the custom tab does, so WaitAsync makes Cancel on the waiting panel
            // return at once; a flow the user still finishes afterwards is undone below.
            pending = builder.ExecuteAsync(ct);
            var result = await pending.WaitAsync(ct);

            await _gate.WaitAsync(ct);
            try
            {
                if (result.Account?.HomeAccountId?.ObjectId is not { Length: > 0 })
                {
                    // Without an object id there is no stable account id to link: nothing is kept.
                    if (result.Account is { } unusable)
                        await RemoveQuietlyAsync(app, unusable);
                    throw new SyncAuthException("Microsoft returned an account without an id.");
                }

                // The cache holds only the signed-in account, so the next start cannot pick another one.
                foreach (var other in await app.GetAccountsAsync())
                {
                    if (other.HomeAccountId?.Identifier != result.Account.HomeAccountId.Identifier)
                        await RemoveQuietlyAsync(app, other);
                }

                SetAccount(result.Account);
            }
            finally
            {
                _gate.Release();
            }

            return _account;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            if (pending is not null)
                _ = ForgetLateSignInAsync(pending);
            return null;
        }
        catch (MsalClientException ex) when (ex.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            return null;
        }
        catch (Exception ex) when (ex is not (SyncAuthException or SyncOfflineException))
        {
            // The caller's cancellation was handled above, so an OperationCanceledException here is a request timeout.
            // The original is not attached: an MSAL message may carry account details.
            _logger.LogInformation("Microsoft sign-in failed: {Code}", (ex as MsalException)?.ErrorCode ?? ex.GetType().Name);
            if (IsOffline(ex))
                throw new SyncOfflineException();
            throw new SyncAuthException("Microsoft refused the sign-in.");
        }
    }

    public async Task SignOutAsync()
    {
        // Loads first, so a sign-out before anything else still finds the cached accounts to remove.
        await LoadAsync();
        await _gate.WaitAsync();
        try
        {
            SetAccount(null);
            if (_app is { } app)
            {
                foreach (var account in await app.GetAccountsAsync())
                    await app.RemoveAsync(account);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Signing out of Microsoft failed: {Error}", ex.GetType().Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string RedirectUri =>
#if ANDROID
        $"msal{CloudConfig.MicrosoftClientId}://auth";
#else
        "http://localhost";
#endif

    private void SetAccount(IAccount? account)
    {
        _msalAccount = account;
        _account = account?.HomeAccountId?.ObjectId is { Length: > 0 } id ? new CloudAccount(id, account.Username) : null;
        if (_account is null)
            _msalAccount = null;
    }

    private async Task RemoveQuietlyAsync(IPublicClientApplication app, IAccount account)
    {
        try
        {
            await app.RemoveAsync(account);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Removing a cached Microsoft account failed: {Error}", ex.GetType().Name);
        }
    }

    /// <summary>
    /// A cancelled sign-in whose browser flow still completes would leave an account in the cache that the app never
    /// took. It is removed again, unless it is the account already signed in.
    /// </summary>
    private async Task ForgetLateSignInAsync(Task<AuthenticationResult> pending)
    {
        try
        {
            var result = await pending;
            if (_app is not { } app || result.Account is not { } late)
                return;

            await _gate.WaitAsync();
            try
            {
                if (late.HomeAccountId?.Identifier != _msalAccount?.HomeAccountId?.Identifier)
                    await app.RemoveAsync(late);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception)
        {
            // Cancelled or failed after all: nothing was added.
        }
    }

    /// <summary>Offline when the device reports no network or the failure came from the transport.</summary>
    private bool IsOffline(Exception ex)
    {
        if (!_network.IsOnline)
            return true;
        for (var inner = ex; inner is not null; inner = inner.InnerException)
        {
            // An OperationCanceledException here is a request timeout: the caller's own cancellation never gets this far.
            if (inner is HttpRequestException or SocketException or WebException or TimeoutException or OperationCanceledException)
                return true;
        }

        return false;
    }

#if WINDOWS
    /// <summary>The pages the loopback redirect shows in the browser, in the app's language.</summary>
    private static SystemWebViewOptions BrowserPages()
    {
        var culture = LocalizationManager.Instance.Culture;
        string Page(string message) =>
            $"<!doctype html><html lang=\"{culture.TwoLetterISOLanguageName}\"><head><meta charset=\"utf-8\">"
            + "<title>Markd</title></head><body style=\"font-family:sans-serif;margin:3em;text-align:center\">"
            + $"<p>{WebUtility.HtmlEncode(message)}</p></body></html>";

        return new SystemWebViewOptions
        {
            HtmlMessageSuccess = Page(Strings.Sync_BrowserDone),
            HtmlMessageError = Page(Strings.Sync_BrowserFailed),
        };
    }
#endif
}
