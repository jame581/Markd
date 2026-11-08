using Markd.Core.Sync;
using Markd.Desktop.Dialogs;
using Markd.Services;
using Markd.Services.Sync;
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
        services.AddSingleton<IConflictResolutionService, DesktopConflictResolutionService>();
        services.AddSingleton<ISyncPassphrasePromptService, DesktopSyncPassphrasePromptService>();
        services.AddSingleton<ISignInWaitService, DesktopSignInWaitService>();
    }
}

/// <summary>
/// The conflict list as an overlay dialog. Only returns the answers (null for Later or a dismiss); never calls the
/// sync coordinator, which is waiting for them.
/// </summary>
public sealed class DesktopConflictResolutionService(IServiceProvider services) : IConflictResolutionService
{
    public Task<IReadOnlyList<ConflictResolution>?> ResolveAsync(IReadOnlyList<SyncConflict> conflicts) =>
        MainThread.InvokeOnMainThreadAsync(() => services.GetRequiredService<DesktopShellPage>().ResolveConflictsAsync(conflicts));
}

/// <summary>The sync passphrase as an overlay dialog. Always completes; a dismiss is cancelled.</summary>
public sealed class DesktopSyncPassphrasePromptService(IServiceProvider services) : ISyncPassphrasePromptService
{
    public Task<PassphrasePromptResult> PromptAsync(PassphrasePromptKind kind, bool previousAttemptFailed) =>
        MainThread.InvokeOnMainThreadAsync(() =>
            services.GetRequiredService<DesktopShellPage>().PromptSyncPassphraseAsync(kind, previousAttemptFailed));
}

/// <summary>
/// Runs a browser sign-in behind a waiting overlay. The overlay's scrim swallows clicks, so the screen that started
/// the sign-in cannot start a second one; a call that still arrives while one runs is treated as cancelled.
/// </summary>
public sealed class DesktopSignInWaitService(IServiceProvider services) : ISignInWaitService
{
    private bool _active;

    public Task<T?> RunAsync<T>(string providerName, Func<CancellationToken, Task<T?>> signIn) where T : class =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (_active)
                return null;

            _active = true;
            try
            {
                using var cts = new CancellationTokenSource();
                var shell = services.GetRequiredService<DesktopShellPage>();
                var dialog = new SignInWaitDialog(providerName, cts.Cancel);
                var layer = shell.ShowOverlay(dialog, dialog.Cancel);
                var window = shell.Window;
                void OnDestroying(object? sender, EventArgs e) => cts.Cancel();
                if (window is not null)
                    window.Destroying += OnDestroying;

                try
                {
                    return await signIn(cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    return null;
                }
                finally
                {
                    if (window is not null)
                        window.Destroying -= OnDestroying;
                    await layer.CloseAsync();
                }
            }
            finally
            {
                _active = false;
            }
        });
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
