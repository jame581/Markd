namespace Markd.Services.Sync;

/// <summary>Network state from MAUI Connectivity; raises <see cref="BecameOnline"/> when internet access returns.</summary>
public sealed class MauiNetworkStatus : INetworkStatus
{
    private bool _wasOnline;

    public MauiNetworkStatus()
    {
        _wasOnline = IsOnline;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

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
