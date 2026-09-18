using Microsoft.Maui.Devices;

namespace Markd.Services;

/// <summary>
/// Navigation and dialogs for view models. Routes are Shell-style strings
/// ("OccasionDetailPage?id=5", "..", "//calendar"); the desktop shell interprets the same routes.
/// </summary>
public interface IAppShellService
{
    DevicePlatform Platform { get; }
    Task GoToAsync(string route);
    Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel, bool destructive = false);
    Task ShowMessageAsync(string title, string message, string close);
}

public sealed class AppShellService : IAppShellService
{
    public DevicePlatform Platform => DeviceInfo.Current.Platform;

    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel, bool destructive = false) =>
        Shell.Current.DisplayAlertAsync(title, message, accept, cancel);

    public Task ShowMessageAsync(string title, string message, string close) =>
        Shell.Current.DisplayAlertAsync(title, message, close);
}
