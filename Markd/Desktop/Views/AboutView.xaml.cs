using Markd.ViewModels;

namespace Markd.Desktop.Views;

public partial class AboutView : ContentView, IDesktopSection
{
    public AboutView()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<AboutViewModel>();
    }

    public event EventHandler? HeaderChanged
    {
        add { }
        remove { }
    }

    public string Title => "About Markd";
    public string Subtitle => "Version, licence and credits";
    public bool ShowsNewOccasion => false;
    public bool HasHeaderRule => true;
    public string? BackLabel => null;

    public Task ShowAsync() => Task.CompletedTask;

    public void Hide()
    {
    }

    public bool GoBack() => false;
}
