using Microsoft.Extensions.Logging;

namespace Markd.Services.Sync;

/// <summary>
/// Network state from MAUI Connectivity; raises <see cref="BecameOnline"/> when internet access returns. When the
/// platform refuses Connectivity (for example a missing permission), the device counts as online and no events are
/// raised: a sync attempt then reports Offline itself instead of sync never starting.
/// </summary>
public sealed class MauiNetworkStatus : INetworkStatus
{
    private readonly ILogger<MauiNetworkStatus> _logger;
    private readonly bool _available;
    private bool _wasOnline;

    public MauiNetworkStatus(ILogger<MauiNetworkStatus> logger)
    {
        _logger = logger;
        try
        {
            _wasOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
            _available = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Network status is unavailable; treating the device as online");
            _wasOnline = true;
        }
    }

    public bool IsOnline
    {
        get
        {
            if (!_available)
                return true;

            try
            {
                return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reading the network status failed; treating the device as online");
                return true;
            }
        }
    }

    public event EventHandler? BecameOnline;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        var online = e.NetworkAccess == NetworkAccess.Internet;
        var cameOnline = online && !_wasOnline;
        _wasOnline = online;
        if (cameOnline)
            BecameOnline?.Invoke(this, EventArgs.Empty);
    }
}
