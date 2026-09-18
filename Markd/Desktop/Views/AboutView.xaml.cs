using Markd.Core.Localization;
using Markd.ViewModels;

namespace Markd.Desktop.Views;

public partial class AboutView : ContentView, IDesktopSection
{
    public AboutView()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<AboutViewModel>();
        LocalizationManager.Instance.CultureChanged += (_, _) => HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? HeaderChanged;

    public string Title => Strings.Home_AboutMarkd;
    public string Subtitle => Strings.About_SubtitleDesktop;
    public bool ShowsNewOccasion => false;
    public bool HasHeaderRule => true;
    public string? BackLabel => null;

    public Task ShowAsync() => Task.CompletedTask;

    public void Hide()
    {
    }

    public bool GoBack() => false;
}
