namespace Markd.Services.Sync;

/// <summary>The waiting panel shown while an interactive sign-in runs in the browser (implemented per layout).</summary>
public interface ISignInWaitService
{
    /// <summary>Shows "Waiting for sign-in in your browser…" with Cancel until signIn completes; Cancel cancels its token.</summary>
    Task<T?> RunAsync<T>(string providerName, Func<CancellationToken, Task<T?>> signIn) where T : class;
}
