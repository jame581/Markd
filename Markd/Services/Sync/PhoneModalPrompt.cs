namespace Markd.Services.Sync;

/// <summary>A phone page that answers the sync coordinator and can be told its answer is no longer coming.</summary>
public interface IAbandonablePrompt
{
    /// <summary>Completes the page's result with its "cancel" or "Later" answer, without touching navigation.</summary>
    void Abandon();
}

/// <summary>
/// Pushes a sync prompt page modally and waits for its answer. The coordinator waits on that answer, so the wait
/// always completes: a failed push, the page leaving the modal stack without answering (navigation away, a window
/// recreated while the app was in the background) and the window closing all count as cancel.
/// </summary>
public static class PhoneModalPrompt
{
    public static async Task<T> ShowAsync<T, TPage>(Func<TPage> create, Func<TPage, Task<T>> result, T fallback)
        where TPage : Page, IAbandonablePrompt
    {
        if (Shell.Current is not { } shell)
            return fallback;

        TPage page;
        try
        {
            page = create();
            await shell.Navigation.PushModalAsync(page, false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Showing {typeof(TPage).Name} failed: {ex}");
            return fallback;
        }

        var window = page.Window ?? Application.Current?.Windows.FirstOrDefault();
        void OnResumed(object? sender, EventArgs e) => AbandonIfGone(page);
        void OnDestroying(object? sender, EventArgs e) => page.Abandon();
        if (window is not null)
        {
            window.Resumed += OnResumed;
            window.Destroying += OnDestroying;
        }

        try
        {
            return await result(page);
        }
        finally
        {
            if (window is not null)
            {
                window.Resumed -= OnResumed;
                window.Destroying -= OnDestroying;
            }
        }
    }

    /// <summary>True while the page is in the app's modal stack.</summary>
    public static bool IsShown(Page page) => Shell.Current?.Navigation.ModalStack.Contains(page) == true;

    /// <summary>
    /// For a page's OnDisappearing: checks on the next UI tick (the modal stack updates after the event) and abandons
    /// the page when it left the stack without answering. A page covered by another modal is still in the stack.
    /// </summary>
    public static void AbandonIfGoneLater<TPage>(TPage page) where TPage : Page, IAbandonablePrompt =>
        page.Dispatcher.Dispatch(() => AbandonIfGone(page));

    private static void AbandonIfGone<TPage>(TPage page) where TPage : Page, IAbandonablePrompt
    {
        if (!IsShown(page))
            page.Abandon();
    }
}
