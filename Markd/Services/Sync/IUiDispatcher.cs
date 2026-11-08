namespace Markd.Services.Sync;

/// <summary>Runs work on the UI thread (app: main thread; tests: inline).</summary>
public interface IUiDispatcher
{
    Task InvokeAsync(Func<Task> action);
}
