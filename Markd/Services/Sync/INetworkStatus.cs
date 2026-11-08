namespace Markd.Services.Sync;

public interface INetworkStatus
{
    bool IsOnline { get; }

    /// <summary>Raised when the device goes from offline to online.</summary>
    event EventHandler? BecameOnline;
}
