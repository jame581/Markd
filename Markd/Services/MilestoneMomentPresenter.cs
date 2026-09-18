using Markd.Pages;

namespace Markd.Services;

/// <summary>Phones: shows the milestone moment as a Material basic dialog and waits until it is closed.</summary>
public sealed class MilestoneMomentPresenter : IMilestoneMomentPresenter
{
    public async Task ShowAsync(MilestoneMoment moment)
    {
        if (Shell.Current?.Navigation is not { } navigation)
            return;

        var page = new MilestoneMomentPage(moment);
        await navigation.PushModalAsync(page, false);
        await page.Closed;
    }
}
