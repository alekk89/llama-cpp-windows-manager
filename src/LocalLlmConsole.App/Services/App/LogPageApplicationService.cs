using LocalLlmConsole;

namespace LocalLlmConsole.Services;

public enum LogPageDeleteApplicationOutcome
{
    Blocked,
    Cancelled,
    Deleted
}

public enum LogPageOpenApplicationOutcome
{
    Blocked,
    Opened
}

public sealed record LogPreviewApplicationRequest(
    LogFileRow? Row,
    string ApiKey,
    bool HasRows);

public sealed record LogPageDeleteApplicationActions(
    Func<LogDeleteCommandPlan, bool> Confirm,
    Func<string, Func<Task>, Task> RunBusyAsync,
    Action ClearPreview,
    Func<Task> RefreshAsync,
    Action<string> SetStatus);

public sealed record LogPageOpenApplicationActions(
    Action<string> OpenPath,
    Action<string> SetStatus);

public sealed class LogPageApplicationService
{
    private readonly LogPageWorkflowService _workflow;

    public LogPageApplicationService(LogPageWorkflowService workflow)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
    }

    public Task<LogPageRefreshData> LoadAsync(
        LoadedModelSessionSnapshot? selectedSession,
        CancellationToken cancellationToken = default)
        => _workflow.LoadAsync(selectedSession, cancellationToken);

    public Task<string> BuildPreviewAsync(
        LogPreviewApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = LogPathFromRow(request.Row);
        return _workflow.BuildPreviewAsync(new LogPreviewRequest(
            path,
            request.Row?.Type ?? "Log",
            Path.GetFileName(path),
            request.Row?.Related ?? "",
            request.Row?.Updated ?? "",
            request.Row?.Size ?? "",
            request.ApiKey,
            request.HasRows),
            cancellationToken);
    }

    public bool TryValidateForOpen(string path, out string error)
        => _workflow.TryValidateForOpen(path, out error);

    public LogPageOpenApplicationOutcome Open(string path, LogPageOpenApplicationActions actions)
    {
        Validate(actions);

        if (!_workflow.TryValidateForOpen(path, out var error))
        {
            actions.SetStatus(error);
            return LogPageOpenApplicationOutcome.Blocked;
        }

        actions.OpenPath(path);
        return LogPageOpenApplicationOutcome.Opened;
    }

    public LogDeleteCommandPlan BuildSelectedDeletionCommand(
        IEnumerable<string> selectedPaths,
        IEnumerable<LoadedModelSessionSnapshot> sessions)
        => _workflow.BuildSelectedDeletionCommand(selectedPaths, sessions);

    public LogDeleteCommandPlan BuildSingleDeletionCommand(
        string path,
        IEnumerable<LoadedModelSessionSnapshot> sessions)
        => _workflow.BuildSingleDeletionCommand(path, sessions);

    public Task<LogDeleteCommandPlan> BuildAllDeletionCommandAsync(
        IEnumerable<LoadedModelSessionSnapshot> sessions,
        CancellationToken cancellationToken = default)
        => _workflow.BuildAllDeletionCommandAsync(sessions, cancellationToken);

    public async Task<LogPageDeleteApplicationOutcome> DeleteAsync(
        LogDeleteCommandPlan commandPlan,
        LogPageDeleteApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandPlan);
        Validate(actions);

        if (!commandPlan.CanDelete)
        {
            actions.SetStatus(commandPlan.StatusMessage);
            return LogPageDeleteApplicationOutcome.Blocked;
        }

        if (!actions.Confirm(commandPlan))
            return LogPageDeleteApplicationOutcome.Cancelled;

        await actions.RunBusyAsync(commandPlan.RunningStatus, async () =>
        {
            var result = await _workflow.DeleteAsync(commandPlan, cancellationToken);
            actions.ClearPreview();
            await actions.RefreshAsync();
            actions.SetStatus(result.StatusMessage);
        });
        return LogPageDeleteApplicationOutcome.Deleted;
    }

    private static void Validate(LogPageDeleteApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.Confirm);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.ClearPreview);
        ArgumentNullException.ThrowIfNull(actions.RefreshAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }

    private static void Validate(LogPageOpenApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.OpenPath);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }

    private static string LogPathFromRow(LogFileRow? row)
        => row?.FullPath ?? "";
}
