using Microsoft.Maui.Devices;

namespace Markd.Services;

public interface IAppShellService
{
    DevicePlatform Platform { get; }
    Task GoToAsync(string route);
    Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel);
}

public sealed class AppShellService : IAppShellService
{
    public DevicePlatform Platform => DeviceInfo.Current.Platform;

    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel) =>
        Shell.Current.DisplayAlertAsync(title, message, accept, cancel);
}
