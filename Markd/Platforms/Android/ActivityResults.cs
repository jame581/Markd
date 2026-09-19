using Android.App;
using Android.Content;

namespace Markd.Platforms.Android;

/// <summary>
/// Awaitable StartActivityForResult; MainActivity forwards every result here.
/// Known limitation: if Android kills the process while the picker is open, the pending save is lost;
/// the user simply exports again.
/// </summary>
public static class ActivityResults
{
    private static readonly Dictionary<int, TaskCompletionSource<(Result Code, Intent? Data)>> Pending = [];
    private static int _nextRequestCode = 4200;

    public static Task<(Result Code, Intent? Data)> StartAsync(Intent intent)
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No activity to start from.");
        var requestCode = Interlocked.Increment(ref _nextRequestCode);
        var source = new TaskCompletionSource<(Result, Intent?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[requestCode] = source;
#pragma warning disable CS0618, CA1422 // AndroidX Activity Result API needs registration before start; this single-activity app does not need it.
        activity.StartActivityForResult(intent, requestCode);
#pragma warning restore CS0618, CA1422
        return source.Task;
    }

    public static void OnResult(int requestCode, Result resultCode, Intent? data)
    {
        if (Pending.Remove(requestCode, out var source))
            source.TrySetResult((resultCode, data));
    }
}
