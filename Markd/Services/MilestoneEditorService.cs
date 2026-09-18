using Markd.Pages;
using Markd.ViewModels;

namespace Markd.Services;

public interface IMilestoneEditorService
{
    Task<MilestoneEditorResult?> PromptAsync();
}

/// <summary>Phones: the Material dialog page, pushed modally.</summary>
public sealed class MilestoneEditorService : IMilestoneEditorService
{
    public async Task<MilestoneEditorResult?> PromptAsync()
    {
        var page = ServiceHelper.GetRequiredService<MilestoneEditorPage>();
        await Shell.Current.Navigation.PushModalAsync(page, false);
        return await page.WaitForResultAsync();
    }
}
