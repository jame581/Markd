namespace Markd.Services.Sync;

public sealed class MainThreadDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Func<Task> action) => MainThread.InvokeOnMainThreadAsync(action);
}
