namespace LocalLlmConsole.Services;

public sealed record BackgroundTaskApplicationActions(
    Action<string> SetStatus,
    Func<Exception, Task> WriteErrorAsync);

public sealed class BackgroundTaskApplicationService
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _activeTasks = [];
    private bool _stopping;

    public Task RunAsync(
        Func<Task> action,
        string failureMessage,
        BackgroundTaskApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
        ArgumentNullException.ThrowIfNull(actions.WriteErrorAsync);

        var tracked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_stopping) return Task.CompletedTask;
            _activeTasks.Add(tracked.Task);
        }

        var task = RunCoreAsync(action, failureMessage, actions);
        task.ContinueWith(
            _ =>
            {
                lock (_gate) _activeTasks.Remove(tracked.Task);
                tracked.TrySetResult();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    public async Task DrainAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            _stopping = true;
            tasks = _activeTasks.ToArray();
        }

        if (tasks.Length > 0)
            await Task.WhenAll(tasks);
    }

    private static async Task RunCoreAsync(
        Func<Task> action,
        string failureMessage,
        BackgroundTaskApplicationActions actions)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // Superseded UI refreshes are expected when the user changes selection quickly.
        }
        catch (Exception ex)
        {
            actions.SetStatus($"{failureMessage}: {ex.Message}");
            await actions.WriteErrorAsync(ex);
        }
    }
}
