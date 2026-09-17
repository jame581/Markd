using Markd.ViewModels;

namespace Markd.Home;

public sealed record HomeSurfaceState(
    bool IsTwoPane,
    bool ShowMobileFeaturedCard,
    bool ShowDesktopDetailPane,
    OccasionSummary? SelectedSummary);
