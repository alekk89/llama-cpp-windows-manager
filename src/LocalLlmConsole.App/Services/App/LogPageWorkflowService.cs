namespace LocalLlmConsole.Services;

public sealed record LogPageRefreshData(
    IReadOnlyList<LogPageFile> Files,
    IReadOnlyDictionary<string, JobRecord> JobsByLogPath,
    string ActiveLogPath,
    string ActiveModel);

public sealed record LogPageFile(
    string FullPath,
    string Name,
    DateTime LastWriteTime,
    DateTime LastWriteTimeUtc,
    long Length);

public sealed record LogPreviewRequest(
    string Path,
    string Type,
    string FileName,
    string Related,
    string Updated,
    string Size,
    string ApiKey,
    bool HasRows);

public sealed record LogDeleteResult(
    int Deleted,
    int Skipped,
    string StatusMessage);

public sealed record LogDeleteCommandPlan(
    bool CanDelete,
    LogDeletionPlan DeletionPlan,
    string ConfirmationTitle,
    string ConfirmationMessage,
    string RunningStatus,
    string DeletedNoun,
    string CompletedStatusOverride,
    string StatusMessage)
{
    public static LogDeleteCommandPlan Blocked(string statusMessage)
        => new(
            CanDelete: false,
            DeletionPlan: new LogDeletionPlan([], 0),
            ConfirmationTitle: "",
            ConfirmationMessage: "",
            RunningStatus: "",
            DeletedNoun: "",
            CompletedStatusOverride: "",
            statusMessage);
}

public sealed class LogPageWorkflowService
{
    private readonly string _workspaceRoot;
    private readonly StateStore _stateStore;

    public LogPageWorkflowService(string workspaceRoot, StateStore stateStore)
    {
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot))
            : workspaceRoot;
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public string LogRoot => Path.Combine(_workspaceRoot, "logs");

    public async Task<LogPageRefreshData> LoadAsync(
        LoadedModelSessionSnapshot? selectedSession,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(LogRoot);
        var jobs = (await _stateStore.ListJobsAsync())
            .ToDictionary(job => LogFileService.NormalizePath(job.LogPath), StringComparer.OrdinalIgnoreCase);
        var files = await Task.Run(
            () => Directory.EnumerateFiles(LogRoot, "*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Select(file => new LogPageFile(file.FullName, file.Name, file.LastWriteTime, file.LastWriteTimeUtc, file.Length))
                .ToArray(),
            cancellationToken);
        var activeModel = selectedSession is { IsRunning: true } ? selectedSession.ModelName : "";
        var activeLogPath = LogFileService.NormalizePath(selectedSession?.LogPath ?? "");
        return new LogPageRefreshData(files, jobs, activeLogPath, activeModel);
    }

    public async Task<string> BuildPreviewAsync(LogPreviewRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Path) || !File.Exists(request.Path))
            return request.HasRows ? "Select a log file to view it." : "No app or model logs yet.";

        var heading = $"{request.Type} | {request.FileName}{Environment.NewLine}{request.Path}{Environment.NewLine}{request.Related}{Environment.NewLine}Updated {request.Updated} | {request.Size}";
        var tail = await Task.Run(() => LogFileService.Tail(request.Path, 80000), cancellationToken);
        tail = LogFileService.RedactSensitiveText(tail, request.ApiKey);
        return $"{heading}{Environment.NewLine}{Environment.NewLine}{tail}";
    }

    public bool TryValidateForOpen(string path, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Select a log file first.";
            return false;
        }

        return LogFileService.TryValidateWorkspaceLogFile(_workspaceRoot, path, out _, out error);
    }

    public bool IsActiveRuntimeLog(string path, IEnumerable<LoadedModelSessionSnapshot> sessions)
    {
        var normalized = LogFileService.NormalizePath(path);
        return sessions.Any(session =>
            session.IsRunning
            && !string.IsNullOrWhiteSpace(session.LogPath)
            && string.Equals(normalized, LogFileService.NormalizePath(session.LogPath), StringComparison.OrdinalIgnoreCase));
    }

    public string[] ActiveRuntimeLogPaths(IEnumerable<LoadedModelSessionSnapshot> sessions)
        => sessions
            .Where(session => session.IsRunning && !string.IsNullOrWhiteSpace(session.LogPath))
            .Select(session => session.LogPath)
            .ToArray();

    public LogDeletionPlan BuildDeletionPlan(IEnumerable<string> candidates, IEnumerable<LoadedModelSessionSnapshot> sessions)
        => LogFileService.BuildDeletionPlan(_workspaceRoot, candidates, ActiveRuntimeLogPaths(sessions));

    public LogDeleteCommandPlan BuildSelectedDeletionCommand(
        IEnumerable<string> selectedPaths,
        IEnumerable<LoadedModelSessionSnapshot> sessions)
    {
        var paths = selectedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
            return LogDeleteCommandPlan.Blocked("Select one or more log files first.");

        if (paths.Length == 1)
            return BuildSingleDeletionCommand(paths[0], sessions);

        var deletionPlan = BuildDeletionPlan(paths, sessions);
        if (deletionPlan.DeletablePaths.Count == 0)
            return LogDeleteCommandPlan.Blocked("No selected logs can be deleted. Stop the running model before deleting its active runtime log.");

        return new LogDeleteCommandPlan(
            CanDelete: true,
            DeletionPlan: deletionPlan,
            ConfirmationTitle: "Delete selected logs",
            ConfirmationMessage: $"Delete {deletionPlan.DeletablePaths.Count} selected log files?",
            RunningStatus: "Deleting selected logs...",
            DeletedNoun: "selected log file",
            CompletedStatusOverride: "",
            StatusMessage: "");
    }

    public LogDeleteCommandPlan BuildSingleDeletionCommand(
        string path,
        IEnumerable<LoadedModelSessionSnapshot> sessions)
    {
        if (!TryValidateForOpen(path, out var error))
            return LogDeleteCommandPlan.Blocked(error);

        if (IsActiveRuntimeLog(path, sessions))
            return LogDeleteCommandPlan.Blocked("Stop the running model before deleting its active runtime log.");

        return new LogDeleteCommandPlan(
            CanDelete: true,
            DeletionPlan: new LogDeletionPlan([path], 0),
            ConfirmationTitle: "Delete log",
            ConfirmationMessage: $"Delete this log file?{Environment.NewLine}{Environment.NewLine}{Path.GetFileName(path)}",
            RunningStatus: "Deleting log...",
            DeletedNoun: "log file",
            CompletedStatusOverride: $"Deleted log {Path.GetFileName(path)}.",
            StatusMessage: "");
    }

    public async Task<LogDeleteCommandPlan> BuildAllDeletionCommandAsync(
        IEnumerable<LoadedModelSessionSnapshot> sessions,
        CancellationToken cancellationToken = default)
    {
        var candidates = await ListLogCandidatesAsync(cancellationToken);
        if (candidates.Length == 0)
            return LogDeleteCommandPlan.Blocked("No logs to delete.");

        return new LogDeleteCommandPlan(
            CanDelete: true,
            DeletionPlan: BuildDeletionPlan(candidates, sessions),
            ConfirmationTitle: "Delete all logs",
            ConfirmationMessage: $"Delete all log files in:{Environment.NewLine}{Environment.NewLine}{LogRoot}",
            RunningStatus: "Deleting logs...",
            DeletedNoun: "log file",
            CompletedStatusOverride: "",
            StatusMessage: "");
    }

    public Task<string[]> ListLogCandidatesAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(LogRoot);
        return Task.Run(() => Directory.EnumerateFiles(LogRoot, "*.log", SearchOption.TopDirectoryOnly).ToArray(), cancellationToken);
    }

    public async Task<LogDeleteResult> DeleteAsync(
        LogDeleteCommandPlan commandPlan,
        CancellationToken cancellationToken = default)
    {
        if (!commandPlan.CanDelete)
            return new LogDeleteResult(0, 0, commandPlan.StatusMessage);

        var result = await DeleteAsync(commandPlan.DeletionPlan, commandPlan.DeletedNoun, cancellationToken);
        return string.IsNullOrWhiteSpace(commandPlan.CompletedStatusOverride)
            ? result
            : result with { StatusMessage = commandPlan.CompletedStatusOverride };
    }

    public async Task<LogDeleteResult> DeleteAsync(
        LogDeletionPlan plan,
        string deletedNoun,
        CancellationToken cancellationToken = default)
    {
        var deleted = await Task.Run(() => LogFileService.DeleteLogs(plan.DeletablePaths), cancellationToken);
        return new LogDeleteResult(
            deleted,
            plan.SkippedCount,
            LogFileService.FormatDeletionStatus(deleted, plan.SkippedCount, deletedNoun));
    }
}
