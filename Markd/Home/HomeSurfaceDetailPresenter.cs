using Markd.ViewModels;

namespace Markd.Home;

public sealed class HomeSurfaceDetailPresenter : IHomeSurfaceDetailPresenter
{
    private readonly OccasionDetailViewModel _detailViewModel;

    public HomeSurfaceDetailPresenter(OccasionDetailViewModel detailViewModel)
    {
        _detailViewModel = detailViewModel;
    }

    public int? CurrentOccasionId => _detailViewModel.CurrentOccasion?.Id;

    public async Task LoadAsync(OccasionSummary summary)
    {
        _detailViewModel.StopTimer();
        await _detailViewModel.LoadAsync(summary.Occasion.Id);
        _detailViewModel.StartTimer();
    }

    public void Stop()
    {
        _detailViewModel.StopTimer();
    }
}
