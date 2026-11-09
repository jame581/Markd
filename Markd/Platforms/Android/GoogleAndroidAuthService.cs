using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common;
using Android.Gms.Common.Apis;
using Android.Gms.Extensions;
using Markd.Core.Sync;
using Markd.Services.Sync;
using Markd.Services.Sync.Cloud;
using Microsoft.Extensions.Logging;
using GoogleAccount = Android.Accounts.Account;

namespace Markd.Platforms.Android;

/// <summary>
/// Google sign-in on Android through Play services' AuthorizationClient: the account chooser and consent are native
/// sheets, and Play services keeps the grant and caches access tokens. Only the account id and email are stored, in
/// Preferences. Signing out clears the last access token and forgets the account but never revokes the grant.
/// </summary>
public sealed class GoogleAndroidAuthService : ICloudAuthService
{
    private const string IdKey = "markd.sync.google.id";
    private const string EmailKey = "markd.sync.google.email";
    private const string GoogleAccountType = "com.google";
    private static readonly TimeSpan ClearTokenTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] Scopes = [GoogleOAuthClient.DriveAppDataScope, "openid", "email"];

    private readonly INetworkStatus _network;
    private readonly ILogger<GoogleAndroidAuthService> _logger;
    // Only GetAccountAsync is used, which needs no client id: Play services does the OAuth part.
    private readonly GoogleOAuthClient _userInfo = new(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, string.Empty, null);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _loaded;
    private CloudAccount? _account;
    private string? _accessToken;

    public GoogleAndroidAuthService(INetworkStatus network, ILogger<GoogleAndroidAuthService> logger)
    {
        _network = network;
        _logger = logger;
    }

    /// <summary>True when Play services is installed, enabled and current enough on this device.</summary>
    public static bool IsAvailable()
    {
        try
        {
            return GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(global::Android.App.Application.Context)
                == ConnectionResult.Success;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public CloudAccount? Account => _account;

    public bool SignInLeavesApp => false;

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_loaded)
                return;
            _loaded = true;
            var id = Preferences.Default.Get<string?>(IdKey, null);
            _account = string.IsNullOrEmpty(id) ? null : new CloudAccount(id, Preferences.Default.Get<string?>(EmailKey, null));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Reading the stored Google account failed: {Error}", ex.GetType().Name);
            _account = null;
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
            if (_account is not { } account)
                throw new SyncAuthException("Not signed in to Google.");

            var client = Identity.GetAuthorizationClient(global::Android.App.Application.Context);
            if (forceRefresh && _accessToken is { } stale)
            {
                // Play services hands back its cached token until it is cleared.
                _accessToken = null;
                await ClearTokenAsync(client, stale, ct);
            }

            var result = await AuthorizeAsync(client, BuildRequest(account.Email), ct);
            if (result.HasResolution)
                throw new SyncAuthException("Google needs the user to sign in again.");
            if (!GrantsDriveAppData(result) || string.IsNullOrEmpty(result.AccessToken))
                throw new SyncAuthException("Google did not grant access to the app's Drive data.");

            _accessToken = result.AccessToken;
            return result.AccessToken;
        }
        catch (Exception ex) when (!IsMapped(ex))
        {
            throw Map(ex);
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

        try
        {
            var activity = Platform.CurrentActivity ?? throw new SyncAuthException("No activity to sign in from.");
            var client = Identity.GetAuthorizationClient(activity);

            AuthorizationResult result;
            try
            {
                result = await AuthorizeAsync(client, BuildRequest(loginHint), ct);
            }
            catch (ApiException ex) when (!string.IsNullOrWhiteSpace(loginHint) && !IsOffline(ex)
                && ex.StatusCode != CommonStatusCodes.Canceled)
            {
                // The hinted account is not on this device: let the user pick one instead.
                _logger.LogInformation("Authorizing the hinted Google account failed ({Status}), asking for one", ex.StatusCode);
                result = await AuthorizeAsync(client, BuildRequest(null), ct);
            }

            if (result.HasResolution)
            {
                var sender = result.PendingIntent?.IntentSender ?? throw new SyncAuthException("Google returned no consent screen.");
                var (code, data) = await MainThread
                    .InvokeOnMainThreadAsync(() => ActivityResults.StartIntentSenderAsync(sender))
                    .WaitAsync(ct);
                if (code != global::Android.App.Result.Ok || data is null)
                    return null;
                result = client.GetAuthorizationResultFromIntent(data);
            }

            if (!GrantsDriveAppData(result))
                throw new SignInMissingPermissionException();
            if (string.IsNullOrEmpty(result.AccessToken))
                throw new SyncAuthException("Google returned no access token.");

            var account = await _userInfo.GetAccountAsync(result.AccessToken, ct);
            await _gate.WaitAsync(ct);
            try
            {
                Preferences.Default.Set(IdKey, account.Id);
                if (account.Email is { } email)
                    Preferences.Default.Set(EmailKey, email);
                else
                    Preferences.Default.Remove(EmailKey);
                _account = account;
                _accessToken = result.AccessToken;
            }
            finally
            {
                _gate.Release();
            }

            return account;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (ApiException ex) when (ex.StatusCode == CommonStatusCodes.Canceled)
        {
            return null;
        }
        catch (Exception ex) when (!IsMapped(ex))
        {
            throw Map(ex);
        }
    }

    public async Task SignOutAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var stale = _accessToken;
            _account = null;
            _accessToken = null;
            _loaded = true;
            Preferences.Default.Remove(IdKey);
            Preferences.Default.Remove(EmailKey);
            if (stale is not null)
            {
                // Bounded, so a stuck Play services task cannot hold the gate.
                using var timeout = new CancellationTokenSource(ClearTokenTimeout);
                await ClearTokenAsync(Identity.GetAuthorizationClient(global::Android.App.Application.Context), stale, timeout.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Signing out of Google failed: {Error}", ex.GetType().Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static AuthorizationRequest BuildRequest(string? email)
    {
        var builder = AuthorizationRequest.InvokeBuilder()
            .SetRequestedScopes(Scopes.Select(s => new Scope(s)).ToList());
        if (!string.IsNullOrWhiteSpace(email))
            builder.SetAccount(new GoogleAccount(email, GoogleAccountType));
        return builder.Build();
    }

    private static async Task<AuthorizationResult> AuthorizeAsync(IAuthorizationClient client, AuthorizationRequest request, CancellationToken ct) =>
        await client.Authorize(request).AsAsync<AuthorizationResult>().WaitAsync(ct);

    private static async Task ClearTokenAsync(IAuthorizationClient client, string token, CancellationToken ct)
    {
        var request = ClearTokenRequest.InvokeBuilder().SetToken(token).Build();
        await client.ClearToken(request).AsAsync().WaitAsync(ct);
    }

    private static bool GrantsDriveAppData(AuthorizationResult result) =>
        result.GrantedScopes?.Contains(GoogleOAuthClient.DriveAppDataScope) == true;

    private bool IsOffline(ApiException ex) => ex.StatusCode == CommonStatusCodes.NetworkError || !_network.IsOnline;

    /// <summary>Exceptions that already follow the ICloudAuthService contract and pass through unchanged.</summary>
    private static bool IsMapped(Exception ex) =>
        ex is OperationCanceledException or SyncAuthException or SyncOfflineException or SignInMissingPermissionException;

    /// <summary>
    /// Any other failure (ApiException, Java exceptions from the GMS tasks) becomes offline or a sign-in error. The
    /// original is not attached: its message may carry account details.
    /// </summary>
    private Exception Map(Exception ex)
    {
        bool offline;
        if (ex is ApiException api)
        {
            _logger.LogInformation("Google authorization failed with status {Status}", api.StatusCode);
            offline = IsOffline(api);
        }
        else
        {
            _logger.LogInformation("Google authorization failed: {Error}", ex.GetType().Name);
            offline = !_network.IsOnline;
        }

        return offline ? new SyncOfflineException() : new SyncAuthException("Google refused the authorization.");
    }
}
