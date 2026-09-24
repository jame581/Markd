using CommunityToolkit.Mvvm.Input;
using Markd.Core.Localization;
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
        OpenLicenceCommand = new AsyncRelayCommand(() => Launcher.Default.OpenAsync($"{RepositoryUrl}/blob/master/LICENSE"));
        ShowAcknowledgementsCommand = new AsyncRelayCommand(ShowAcknowledgementsAsync);
    }

    public string VersionText => string.Format(LocalizationManager.Instance.Culture, Strings.Settings_Version, AppVersion.Version, AppVersion.Build);

    public IAsyncRelayCommand OpenSourceCommand { get; }
    public IAsyncRelayCommand OpenLicenceCommand { get; }
    public IAsyncRelayCommand ShowAcknowledgementsCommand { get; }

    private Task ShowAcknowledgementsAsync() =>
        _shellService.ShowMessageAsync(
            Strings.About_Acknowledgements,
            Strings.About_AcknowledgementsBody,
            Strings.Common_Close);
}
