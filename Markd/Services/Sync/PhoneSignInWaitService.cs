using Markd.Pages;

namespace Markd.Services.Sync;

/// <summary>
/// Phones: runs the sign-in behind a modal waiting page. The page is modal, so the screen that started the sign-in
/// cannot start a second one; a call that still arrives while one runs is treated as cancelled.
/// </summary>
public sealed class PhoneSignInWaitService : ISignInWaitService
{
    private bool _active;

    public async Task<T?> RunAsync<T>(string providerName, Func<CancellationToken, Task<T?>> signIn) where T : class
    {
        if (_active)
            return null;

        _active = true;
        try
        {
            using var cts = new CancellationTokenSource();
            SignInWaitPage? page = null;
            var shown = PhoneModalPrompt.ShowAsync(() => page = new SignInWaitPage(providerName, cts.Cancel), async p =>
                {
                    await p.Closed;
                    return (object?)null;
                }, null);

            try
            {
                return await signIn(cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                return null;
            }
            finally
            {
                if (page is not null)
                {
                    // Closing before the push lands would leave the page on the stack: wait until it is up (or the
                    // push failed or it was abandoned, which completes shown).
                    await Task.WhenAny(page.Appeared, shown);
                    await page.CloseAsync();
                }
                await shown;
            }
        }
        finally
        {
            _active = false;
        }
    }
}
