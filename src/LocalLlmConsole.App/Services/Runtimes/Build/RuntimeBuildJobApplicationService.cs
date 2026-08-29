namespace LocalLlmConsole.Services;

public enum RuntimeBuildJobApplicationOutcome
{
    Blocked,
    Cancelled,
    Applied
}

public sealed record RuntimeBuildJobClearConfirmation(
    JobRecord Job,
    string Title,
    string Message);

public sealed record RuntimeBuildJobApplicationActions(
    Func<RuntimeBuildJobClearConfirmation, bool> ConfirmClear,
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<RuntimeBuildJobRetryPlan, Task> RetryBuildAsync,
    Action<string> SetStatus);

public sealed class RuntimeBuildJobApplicationService
{
    private readonly RuntimeBuildJobControlService _controls;

    public RuntimeBuildJobApplicationService(RuntimeBuildJobControlService controls)
    {
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
    }

    public async Task<RuntimeBuildJobApplicationOutcome> CancelAsync(
        JobRecord job,
        AppSettings settings,
        long maxLogBytes,
        RuntimeBuildJobApplicationActions actions)
    {
        Validate(job, settings, actions);

        var result = await _controls.CancelAsync(job, settings.WslDistro, maxLogBytes);
        actions.SetStatus(result.StatusMessage);
        return result.Success ? RuntimeBuildJobApplicationOutcome.Applied : RuntimeBuildJobApplicationOutcome.Blocked;
    }

    public async Task<RuntimeBuildJobApplicationOutcome> RetryAsync(
        JobRecord job,
        RuntimeBuildJobApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(job);
        Validate(actions);

        var retry = _controls.PlanRetry(job);
        if (!retry.CanRetry || retry.Preset is null)
        {
            actions.SetStatus(retry.StatusMessage);
            return RuntimeBuildJobApplicationOutcome.Blocked;
        }

        await actions.RetryBuildAsync(retry);
        return RuntimeBuildJobApplicationOutcome.Applied;
    }

    public async Task<RuntimeBuildJobApplicationOutcome> ClearAsync(
        JobRecord job,
        RuntimeBuildJobApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(job);
        Validate(actions);

        if (!RuntimeBuildJobService.CanClear(job))
        {
            actions.SetStatus("Only completed, failed, cancelled, or interrupted runtime jobs can be cleared.");
            return RuntimeBuildJobApplicationOutcome.Blocked;
        }

        var confirmation = new RuntimeBuildJobClearConfirmation(
            job,
            "Clear runtime job",
            $"Clear this runtime job and its log?{Environment.NewLine}{Environment.NewLine}{job.Id}");
        if (!actions.ConfirmClear(confirmation))
            return RuntimeBuildJobApplicationOutcome.Cancelled;

        var outcome = RuntimeBuildJobApplicationOutcome.Applied;
        await actions.RunBusyAsync("Clearing runtime job...", async () =>
        {
            var result = await _controls.ClearAsync(job);
            actions.SetStatus(result.StatusMessage);
            if (!result.Success)
                outcome = RuntimeBuildJobApplicationOutcome.Blocked;
        });
        return outcome;
    }

    private static void Validate(
        JobRecord job,
        AppSettings settings,
        RuntimeBuildJobApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(settings);
        Validate(actions);
    }

    private static void Validate(RuntimeBuildJobApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.ConfirmClear);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.RetryBuildAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
