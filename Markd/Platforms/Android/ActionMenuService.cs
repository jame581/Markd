using Android.Text;
using Android.Text.Style;
using Markd.Controls;
using Markd.Services;
using Microsoft.Maui.Platform;
using APopupMenu = AndroidX.AppCompat.Widget.PopupMenu;

namespace Markd.Platforms.Android;

/// <summary>Material overflow menu anchored to the tapped view; the destructive item is tinted with the error colour.</summary>
public sealed class ActionMenuService : IActionMenuService
{
    public Task<string?> ShowAsync(VisualElement anchor, IReadOnlyList<string> items, string? destructiveItem = null)
    {
        var result = new TaskCompletionSource<string?>();
        if (anchor.Handler?.PlatformView is not global::Android.Views.View view || view.Context is null)
        {
            result.SetResult(null);
            return result.Task;
        }

        var menu = new APopupMenu(view.Context, view);
        var dark = ThemeColors.IsDark;
        for (var i = 0; i < items.Count; i++)
        {
            var title = new SpannableString(items[i]);
            if (items[i] == destructiveItem)
                title.SetSpan(new ForegroundColorSpan(ThemeColors.Get("Danger", dark).ToPlatform()), 0, title.Length(), SpanTypes.ExclusiveExclusive);

            menu.Menu.Add(0, i, i, title);
        }

        menu.MenuItemClick += (_, e) => result.TrySetResult(items[e.Item!.ItemId]);
        menu.DismissEvent += (_, _) => result.TrySetResult(null);
        menu.Show();
        return result.Task;
    }
}
