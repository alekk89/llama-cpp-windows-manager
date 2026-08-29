namespace LocalLlmConsole.Services;

public readonly record struct HttpListenerRequestDrainResult(
    Task Completion,
    bool CompletedWithinTimeout);

public sealed class HttpListenerRequestTracker
{
    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(1);
    private readonly object _sync = new();
    private readonly HashSet<Task> _tasks = [];
    private readonly HashSet<HttpListenerContext> _contexts = [];

    public void Track(HttpListenerContext context, Task task, string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        lock (_sync)
        {
            _tasks.Add(task);
            _contexts.Add(context);
        }

        task.ContinueWith(
            completed =>
            {
                lock (_sync)
                {
                    _tasks.Remove(completed);
                    _contexts.Remove(context);
                }
                if (completed.IsFaulted && completed.Exception is not null)
                    Trace.TraceError($"{failureMessage} {completed.Exception}");
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async Task<HttpListenerRequestDrainResult> AbortAndDrainAsync(
        string completionFailureMessage,
        TimeSpan? timeout = null)
    {
        Task[] tasks;
        HttpListenerContext[] contexts;
        lock (_sync)
        {
            tasks = _tasks.ToArray();
            contexts = _contexts.ToArray();
        }

        foreach (var context in contexts)
            Abort(context);
        if (tasks.Length == 0)
            return new HttpListenerRequestDrainResult(Task.CompletedTask, CompletedWithinTimeout: true);

        var completion = Task.WhenAll(tasks);
        var completedWithinTimeout = await BoundedTaskDrain.ObserveWithinAsync(
            completion,
            timeout ?? DefaultDrainTimeout,
            "HTTP request handlers did not stop within the shutdown interval; continuing shutdown.",
            completionFailureMessage);
        return new HttpListenerRequestDrainResult(completion, completedWithinTimeout);
    }

    public static async Task ObserveCompletionAsync(Task task, string failureMessage)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"{failureMessage} {ex}");
        }
    }

    private static void Abort(HttpListenerContext context)
    {
        try { context.Response.Abort(); }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not abort HTTP response during shutdown: {ex.Message}");
        }
    }
}

internal static class BoundedTaskDrain
{
    public static async Task<bool> ObserveWithinAsync(
        Task task,
        TimeSpan timeout,
        string timeoutMessage,
        string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeoutMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        try
        {
            await task.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            Trace.TraceWarning(timeoutMessage);
            _ = HttpListenerRequestTracker.ObserveCompletionAsync(task, failureMessage);
            return false;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"{failureMessage} {ex}");
            return true;
        }
    }
}
