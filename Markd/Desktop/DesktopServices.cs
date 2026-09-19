using Markd.Services;
using Markd.ViewModels;
using Microsoft.Maui.Devices;

namespace Markd.Desktop;

/// <summary>Desktop DI: the root page replaces Shell, and every view-model seam is answered by the shell's overlays.</summary>
public static class DesktopRegistration
{
    public static void AddDesktopShell(IServiceCollection services)
    {
        services.AddSingleton<DesktopShellPage>();
        services.AddSingleton<IAppShellService, DesktopShellService>();
        services.AddSingleton<IFeedbackService, DesktopFeedbackService>();
        services.AddSingleton<IMilestoneMomentPresenter, DesktopMilestoneMomentPresenter>();
        services.AddTransient<IMilestoneEditorService, DesktopMilestoneEditorService>();
        services.AddSingleton<IFileExportService, FileSaverExportService>();
        services.AddSingleton<IActionMenuService, ActionSheetMenuService>();
        services.AddSingleton<IPasswordPromptService, DesktopPasswordPromptService>();
    }
}

/// <summary>Routes and dialogs for view models, resolved lazily so the page can itself depend on view models.</summary>
public sealed class DesktopShellService(IServiceProvider services) : IAppShellService
{
    private DesktopShellPage Shell => services.GetRequiredService<DesktopShellPage>();

    public DevicePlatform Platform => DevicePlatform.WinUI;

    public Task GoToAsync(string route) => Shell.NavigateAsync(route);

    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel, bool destructive = false) =>
        Shell.ConfirmAsync(title, message, accept, cancel, destructive);

    public Task ShowMessageAsync(string title, string message, string close) =>
        Shell.ConfirmAsync(title, message, close, null);
}

/// <summary>The bottom-right toast. The desktop confirms destructive actions instead of offering undo.</summary>
public sealed class DesktopFeedbackService(IServiceProvider services) : IFeedbackService
{
    public Task ShowAsync(string title, string? detail = null)
    {
        services.GetRequiredService<DesktopShellPage>().ShowToast(title, detail);
        return Task.CompletedTask;
    }

    public Task<bool> ShowUndoAsync(string message)
    {
        services.GetRequiredService<DesktopShellPage>().ShowToast(message, null);
        return Task.FromResult(false);
    }
}

public sealed class DesktopMilestoneMomentPresenter(IServiceProvider services) : IMilestoneMomentPresenter
{
    public Task ShowAsync(MilestoneMoment moment) =>
        services.GetRequiredService<DesktopShellPage>().ShowMilestoneMomentAsync(moment);
}

public sealed class DesktopMilestoneEditorService(IServiceProvider services) : IMilestoneEditorService
{
    public Task<MilestoneEditorResult?> PromptAsync() =>
        services.GetRequiredService<DesktopShellPage>().PromptMilestoneAsync();
}

public sealed class DesktopPasswordPromptService(IServiceProvider services) : IPasswordPromptService
{
    private DesktopShellPage Shell => services.GetRequiredService<DesktopShellPage>();

    public Task<ExportChoice?> PromptExportAsync() => Shell.PromptExportAsync();

    public Task<string?> PromptImportPasswordAsync(string fileName, bool previousAttemptFailed) =>
        Shell.PromptPasswordAsync(fileName, previousAttemptFailed);
}
