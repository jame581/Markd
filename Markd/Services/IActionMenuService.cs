namespace Markd.Services;

/// <summary>An overflow menu anchored to a view. Returns the chosen item, or null when dismissed.</summary>
public interface IActionMenuService
{
    Task<string?> ShowAsync(VisualElement anchor, IReadOnlyList<string> items, string? destructiveItem = null);
}

/// <summary>Fallback for platforms without a native popup menu: an action sheet.</summary>
public sealed class ActionSheetMenuService : IActionMenuService
{
    public async Task<string?> ShowAsync(VisualElement anchor, IReadOnlyList<string> items, string? destructiveItem = null)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return null;

        var choice = await page.DisplayActionSheetAsync(null, "Cancel", destructiveItem, items.Where(i => i != destructiveItem).ToArray());
        return choice is null or "Cancel" ? null : choice;
    }
}
