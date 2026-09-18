using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Markd.Controls;

namespace Markd.Services;

/// <summary>
/// Material snackbar: inverse surface, radius 10, UNDO in inverse primary, 5 s timeout.
/// Raises <see cref="VisibilityChanged"/> so the extended FAB can move above it.
/// </summary>
public sealed class SnackbarFeedbackService : IFeedbackService
{
    private static int _generation;

    public static event EventHandler<bool>? VisibilityChanged;

    /// <summary>The view the snackbar sits above (the bottom navigation bar of the visible tab page), if any.</summary>
    public static IView? Anchor { get; set; }

    public async Task ShowAsync(string title, string? detail = null)
    {
        var text = string.IsNullOrWhiteSpace(detail) ? title : $"{title} · {detail}";
        await ShowCoreAsync(text, null, null, TimeSpan.FromSeconds(4));
    }

    public async Task<bool> ShowUndoAsync(string message)
    {
        var result = new TaskCompletionSource<bool>();
        await ShowCoreAsync(message, () => result.TrySetResult(true), "UNDO", TimeSpan.FromSeconds(5), result);
        return await result.Task;
    }

    private static async Task ShowCoreAsync(string text, Action? action, string? actionText, TimeSpan duration, TaskCompletionSource<bool>? completion = null)
    {
        var dark = ThemeColors.IsDark;
        var options = new SnackbarOptions
        {
            BackgroundColor = ThemeColors.Get("InverseSurface", dark),
            TextColor = ThemeColors.Get("InverseOnSurface", dark),
            ActionButtonTextColor = ThemeColors.Get("InversePrimary", dark),
            CornerRadius = new CornerRadius(10),
            Font = Microsoft.Maui.Font.OfSize("FigtreeRegular", 14.5),
            ActionButtonFont = Microsoft.Maui.Font.OfSize("FigtreeSemiBold", 14.5)
        };

        var snackbar = Snackbar.Make(text, action, actionText ?? string.Empty, duration, options, Anchor);
        var generation = Interlocked.Increment(ref _generation);
        VisibilityChanged?.Invoke(null, true);
        await snackbar.Show();

        _ = Task.Delay(duration + TimeSpan.FromMilliseconds(250)).ContinueWith(_ =>
        {
            completion?.TrySetResult(false);

            // A newer snackbar replaced this one; it lowers the FAB when it times out.
            if (generation == Volatile.Read(ref _generation))
                MainThread.BeginInvokeOnMainThread(() => VisibilityChanged?.Invoke(null, false));
        });
    }
}
