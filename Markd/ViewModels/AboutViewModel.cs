using CommunityToolkit.Mvvm.Input;
using Markd.Services;

namespace Markd.ViewModels;

public class AboutViewModel : ViewModelBase
{
    public const string RepositoryUrl = "https://github.com/jame581/Markd";

    private readonly IAppShellService _shellService;

    public AboutViewModel(IAppShellService shellService)
    {
        _shellService = shellService;
        OpenSourceCommand = new AsyncRelayCommand(() => Launcher.Default.OpenAsync(RepositoryUrl));
        OpenLicenceCommand = new AsyncRelayCommand(() => Launcher.Default.OpenAsync($"{RepositoryUrl}/blob/master/LICENSE.txt"));
        ShowAcknowledgementsCommand = new AsyncRelayCommand(ShowAcknowledgementsAsync);
    }

    public string VersionText => $"Version {AppInfo.Current.VersionString} · build {AppInfo.Current.BuildString}";

    public IAsyncRelayCommand OpenSourceCommand { get; }
    public IAsyncRelayCommand OpenLicenceCommand { get; }
    public IAsyncRelayCommand ShowAcknowledgementsCommand { get; }

    private Task ShowAcknowledgementsAsync() =>
        _shellService.ShowMessageAsync(
            "Acknowledgements",
            "Figtree and IBM Plex Mono are used under the SIL Open Font License 1.1.\n\n"
            + "Built with .NET MAUI, EF Core with SQLite, CommunityToolkit.Mvvm, CommunityToolkit.Maui and Plugin.LocalNotification.",
            "Close");
}
