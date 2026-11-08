namespace Markd.Services.Sync;

/// <summary>Temporary: runs the sign-in without a waiting panel. Replaced by the phone and desktop panels.</summary>
public sealed class PassThroughSignInWaitService : ISignInWaitService
{
    public Task<T?> RunAsync<T>(string providerName, Func<CancellationToken, Task<T?>> signIn) where T : class =>
        signIn(CancellationToken.None);
}
