namespace LocalLlmConsole.Services;

public enum DownloadHistoryApplicationOutcome
{
    NoSelection,
    Blocked,
    Cancelled,
    Applied
}

public enum DownloadHistoryTimerRefreshOutcome
{
    Skipped,
    Applied
}

public sealed record DownloadHistoryShowActions(
    Func<bool> IsHistoryHostVisible,
    Action ShowHistoryHost,
    Action ConfigureHistoryGrid,
    Func<Task> RefreshDownloadHistoryAsync,
    Action<string> SelectDownloadHistoryJob,
    Action StartRefreshTimer,
    Action<string> SetStatus);

public sealed record DownloadHistoryTimerRefreshActions(
    Func<bool> TryBeginRefresh,
    Func<Task> RefreshDownloadHistoryAsync,
    Action CompleteRefresh);

public sealed record DownloadHistoryCommandApplicationActions(
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<Task> RefreshDownloadHistoryAsync,
    Action<string> SetStatus,
    Action<string> StartMonitor);

public sealed record DownloadHistoryDeleteApplicationActions(
    Func<DownloadHistoryDeletePlan, bool> Confirm,
    DownloadHistoryCommandApplicationActions CommandActions);

public sealed class DownloadHistoryApplicationService
{
    private const string NoSelectionStatus = "Select a download history row first.";

    private readonly DownloadHistoryWorkflowService _workflow;

    public DownloadHistoryApplicationService(DownloadHistoryWorkflowService workflow)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
    }

    public Task<IReadOnlyList<JobRecord>> ListJobsAsync()
        => _workflow.ListJobsAsync();

    public async Task<DownloadHistoryApplicationOutcome> ShowAsync(
        string selectedJobId,
        DownloadHistoryShowActions actions)
    {
        Validate(actions);

        if (!actions.IsHistoryHostVisible())
            actions.ShowHistoryHost();

        actions.ConfigureHistoryGrid();
        await actions.RefreshDownloadHistoryAsync();
        actions.SelectDownloadHistoryJob(selectedJobId);
        actions.StartRefreshTimer();
        actions.SetStatus(string.IsNullOrWhiteSpace(selectedJobId)
            ? "Showing download history."
            : "Showing download history for the started model download.");
        return DownloadHistoryApplicationOutcome.Applied;
    }

    public async Task<DownloadHistoryTimerRefreshOutcome> RefreshTimerAsync(
        DownloadHistoryTimerRefreshActions actions)
    {
        Validate(actions);

        if (!actions.TryBeginRefresh())
            return DownloadHistoryTimerRefreshOutcome.Skipped;

        try
        {
            await actions.RefreshDownloadHistoryAsync();
            return DownloadHistoryTimerRefreshOutcome.Applied;
        }
        finally
        {
            actions.CompleteRefresh();
        }
    }

    public async Task<DownloadHistoryApplicationOutcome> ResumeAsync(
        JobRecord? job,
        AppSettings settings,
        DownloadHistoryCommandApplicationActions actions)
    {
        Validate(actions);

        if (job is null)
            return NoSelection(actions);

        var resumePlan = _workflow.BuildResumePlan(job);
        if (!resumePlan.CanResume)
        {
            actions.SetStatus(resumePlan.StatusMessage);
            return DownloadHistoryApplicationOutcome.Blocked;
        }

        await actions.RunBusyAsync("Starting download...", async () =>
        {
            var result = await _workflow.ResumeAsync(job, settings);
            await RefreshAfterCommandAsync(actions);
            if (result.StartMonitor)
                actions.StartMonitor(job.Id);
            actions.SetStatus(result.StatusMessage);
        });
        return DownloadHistoryApplicationOutcome.Applied;
    }

    public async Task<DownloadHistoryApplicationOutcome> PauseAsync(
        JobRecord? job,
        DownloadHistoryCommandApplicationActions actions)
    {
        Validate(actions);

        if (job is null)
            return NoSelection(actions);

        await actions.RunBusyAsync("Pausing download...", async () =>
        {
            var result = await _workflow.PauseAsync(job);
            await RefreshAfterCommandAsync(actions);
            actions.SetStatus(result.StatusMessage);
        });
        return DownloadHistoryApplicationOutcome.Applied;
    }

    public async Task<DownloadHistoryApplicationOutcome> StopAsync(
        JobRecord? job,
        DownloadHistoryCommandApplicationActions actions)
    {
        Validate(actions);

        if (job is null)
            return NoSelection(actions);

        await actions.RunBusyAsync("Stopping download...", async () =>
        {
            var result = await _workflow.StopAsync(job);
            await RefreshAfterCommandAsync(actions);
            actions.SetStatus(result.StatusMessage);
        });
        return DownloadHistoryApplicationOutcome.Applied;
    }

    public async Task<DownloadHistoryApplicationOutcome> DeleteAsync(
        JobRecord? job,
        AppSettings settings,
        DownloadHistoryDeleteApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        Validate(actions);

        if (job is null)
            return NoSelection(actions.CommandActions);

        var deletePlan = _workflow.BuildDeletePlan(job);
        if (!actions.Confirm(deletePlan))
            return DownloadHistoryApplicationOutcome.Cancelled;

        await actions.CommandActions.RunBusyAsync("Deleting model download...", async () =>
        {
            var deletion = await _workflow.DeleteAsync(job, settings, cancellationToken);
            await RefreshAfterCommandAsync(actions.CommandActions);
            actions.CommandActions.SetStatus(deletion.StatusMessage);
        });
        return DownloadHistoryApplicationOutcome.Applied;
    }

    public Task WaitUntilInactiveOrTerminalAsync(
        string jobId,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default)
        => _workflow.WaitUntilInactiveOrTerminalAsync(jobId, pollInterval, cancellationToken);

    private static async Task RefreshAfterCommandAsync(DownloadHistoryCommandApplicationActions actions)
        => await actions.RefreshDownloadHistoryAsync();

    private static DownloadHistoryApplicationOutcome NoSelection(DownloadHistoryCommandApplicationActions actions)
    {
        actions.SetStatus(NoSelectionStatus);
        return DownloadHistoryApplicationOutcome.NoSelection;
    }

    private static void Validate(DownloadHistoryDeleteApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.Confirm);
        Validate(actions.CommandActions);
    }

    private static void Validate(DownloadHistoryShowActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.IsHistoryHostVisible);
        ArgumentNullException.ThrowIfNull(actions.ShowHistoryHost);
        ArgumentNullException.ThrowIfNull(actions.ConfigureHistoryGrid);
        ArgumentNullException.ThrowIfNull(actions.RefreshDownloadHistoryAsync);
        ArgumentNullException.ThrowIfNull(actions.SelectDownloadHistoryJob);
        ArgumentNullException.ThrowIfNull(actions.StartRefreshTimer);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }

    private static void Validate(DownloadHistoryTimerRefreshActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.TryBeginRefresh);
        ArgumentNullException.ThrowIfNull(actions.RefreshDownloadHistoryAsync);
        ArgumentNullException.ThrowIfNull(actions.CompleteRefresh);
    }

    private static void Validate(DownloadHistoryCommandApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshDownloadHistoryAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
        ArgumentNullException.ThrowIfNull(actions.StartMonitor);
    }
}
