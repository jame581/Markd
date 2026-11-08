using CommunityToolkit.Mvvm.Input;
using Markd.Services.Sync;

namespace Markd.ViewModels;

/// <summary>
/// The Sync section of settings. The sync controller is a singleton and this view model is transient, so it listens to
/// <see cref="ISyncController.StatusChanged"/> only while attached: the constructor attaches, views call
/// <see cref="Detach"/> when they disappear and <see cref="Attach"/> when they come back.
/// </summary>
public sealed class SyncSettingsViewModel : ViewModelBase
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    private readonly ISyncController _sync;
    private readonly TimeProvider _time;
    private readonly IUiDispatcher? _dispatcher;
    private ITimer? _timer;
    private bool _attached;

    /// <param name="dispatcher">Marshals the once-a-minute refresh to the UI thread. Null runs it inline (tests).</param>
    public SyncSettingsViewModel(ISyncController sync, ICloudProviderCatalog catalog, TimeProvider time, IUiDispatcher? dispatcher = null)
    {
        _sync = sync;
        _time = time;
        _dispatcher = dispatcher;
        Providers = catalog.Available;

        ConnectCommand = new AsyncRelayCommand<CloudProviderOption>(
            option => option is null ? Task.CompletedTask : _sync.ConnectAsync(option.Id));
        SyncNowCommand = new AsyncRelayCommand(() => _sync.SyncNowAsync(SyncTrigger.Manual));
        ChangePassphraseCommand = new AsyncRelayCommand(_sync.ChangePassphraseAsync);
        DisconnectCommand = new AsyncRelayCommand(_sync.DisconnectAsync);
        ReplaceCloudCommand = new AsyncRelayCommand(_sync.ReplaceCloudAsync);
        ActionCommand = new AsyncRelayCommand(RunActionAsync, () => ActionText is not null);

        Attach();
    }

    public IReadOnlyList<CloudProviderOption> Providers { get; }

    public bool IsVisible => Providers.Count > 0;

    public bool IsOff => _sync.Status.Kind == SyncStatusKind.Off;

    public bool IsConnected => !IsOff;

    public bool NeedsAttention => _sync.Status.Kind is SyncStatusKind.Conflicts or SyncStatusKind.NeedsPassphrase
        or SyncStatusKind.SignInAgain or SyncStatusKind.UpdateApp or SyncStatusKind.Unreadable
        or SyncStatusKind.ProviderUnavailable;

    public bool IsStatusWarning => NeedsAttention || _sync.Status.Kind == SyncStatusKind.Failed;

    public string AccountText => string.Join(" · ", new[] { _sync.Status.ProviderName, _sync.Status.AccountLabel }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    public string StatusText => SyncStatusText.Describe(_sync.Status, _time.GetUtcNow().UtcDateTime);

    /// <summary>The button next to a status that needs the user, or null when there is nothing to do.</summary>
    public string? ActionText => _sync.Status.Kind switch
    {
        SyncStatusKind.Conflicts => Core.Localization.Strings.Sync_ActionResolve,
        SyncStatusKind.NeedsPassphrase => Core.Localization.Strings.Sync_ActionEnterPassphrase,
        SyncStatusKind.SignInAgain => Core.Localization.Strings.Sync_ActionSignIn,
        SyncStatusKind.Unreadable => Core.Localization.Strings.Sync_ActionReplaceCloud,
        _ => null,
    };

    public AsyncRelayCommand<CloudProviderOption> ConnectCommand { get; }

    public AsyncRelayCommand SyncNowCommand { get; }

    public AsyncRelayCommand ChangePassphraseCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public AsyncRelayCommand ReplaceCloudCommand { get; }

    public AsyncRelayCommand ActionCommand { get; }

    /// <summary>Starts listening for status changes and refreshing the relative time. Idempotent.</summary>
    public void Attach()
    {
        if (_attached)
            return;
        _attached = true;
        _sync.StatusChanged += OnStatusChanged;
        _timer = _time.CreateTimer(_ => RefreshOnUi(), null, RefreshInterval, RefreshInterval);
        Refresh();
    }

    /// <summary>Stops listening and stops the refresh timer. Safe to call repeatedly.</summary>
    public void Detach()
    {
        if (!_attached)
            return;
        _attached = false;
        _sync.StatusChanged -= OnStatusChanged;
        _timer?.Dispose();
        _timer = null;
    }

    protected override void OnLanguageChanged() => Refresh();

    private void OnStatusChanged(object? sender, EventArgs e) => Refresh();

    private void RefreshOnUi()
    {
        if (_dispatcher is null)
            Refresh();
        else
            _ = _dispatcher.InvokeAsync(() =>
            {
                Refresh();
                return Task.CompletedTask;
            });
    }

    private void Refresh()
    {
        OnPropertyChanged(string.Empty);
        ActionCommand.NotifyCanExecuteChanged();
    }

    private Task RunActionAsync() => _sync.Status.Kind switch
    {
        SyncStatusKind.Conflicts => _sync.OpenConflictsAsync(),
        SyncStatusKind.NeedsPassphrase => _sync.EnterPassphraseAsync(),
        SyncStatusKind.SignInAgain => SignInAgainAsync(),
        SyncStatusKind.Unreadable => _sync.ReplaceCloudAsync(),
        _ => Task.CompletedTask,
    };

    // Reconnecting through the provider that is already linked refreshes its sign-in.
    private Task SignInAgainAsync() =>
        Providers.FirstOrDefault(p => p.DisplayName == _sync.Status.ProviderName) is { } provider
            ? _sync.ConnectAsync(provider.Id)
            : _sync.SyncNowAsync(SyncTrigger.Manual);
}
