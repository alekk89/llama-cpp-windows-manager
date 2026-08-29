namespace LocalLlmConsole.Services;

public sealed record RuntimeBuildJobControlResult(bool Success, string StatusMessage);

public sealed record RuntimeBuildJobRetryPlan(
    bool CanRetry,
    string StatusMessage,
    RuntimeBuildPreset? Preset = null,
    bool Update = false,
    RuntimeSourceEntry? Source = null);

public sealed class RuntimeBuildJobControlService
{
    private readonly StateStore _stateStore;
    private readonly JobEngine _jobs;
    private readonly RuntimeBuildMarkerService _markers;
    private readonly RuntimeBuildCancellationRegistry _cancellations;
    private readonly string _workspaceRoot;

    public RuntimeBuildJobControlService(
        StateStore stateStore,
        JobEngine jobs,
        RuntimeBuildMarkerService markers,
        RuntimeBuildCancellationRegistry cancellations,
        string workspaceRoot)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _markers = markers ?? throw new ArgumentNullException(nameof(markers));
        _cancellations = cancellations ?? throw new ArgumentNullException(nameof(cancellations));
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot))
            : workspaceRoot;
    }

    public CancellationTokenSource RegisterCancellation(string jobId)
        => _cancellations.Register(jobId);

    public void UnregisterCancellation(string jobId, CancellationTokenSource cancellation)
        => _cancellations.Unregister(jobId, cancellation);

    public async Task<RuntimeBuildJobControlResult> CancelAsync(
        JobRecord job,
        string defaultWslDistro,
        long maxLogBytes,
        CancellationToken cancellationToken = default)
    {
        var payload = RuntimeBuildJobService.ParsePayload(job.PayloadJson);
        if (!RuntimeBuildJobService.CanCancel(job) || payload is null)
            return new RuntimeBuildJobControlResult(false, "Only active runtime build jobs can be cancelled.");

        _cancellations.TryCancel(job.Id);
        if (payload.Mode == RuntimeMode.Wsl && !string.IsNullOrWhiteSpace(payload.ProcessMarker))
        {
            var distro = string.IsNullOrWhiteSpace(payload.WslDistro) ? defaultWslDistro : payload.WslDistro;
            await _markers.CleanupAsync(distro, payload.ProcessMarker);
        }

        const string message = "Cancel requested by user.";
        await RuntimeBuildJobService.AppendJobLogAsync(job.LogPath, JobStatus.Cancelled, message, maxLogBytes);
        await _jobs.UpdateAsync(
            job,
            JobStatus.Cancelled,
            RuntimeBuildJobService.Payload(payload.Preset, payload.Action, payload.InstallDir, message, payload.ProcessMarker, payload.WslDistro, payload.SourceDir),
            cancellationToken);
        return new RuntimeBuildJobControlResult(true, $"Cancel requested for {payload.Preset.Label}.");
    }

    public RuntimeBuildJobRetryPlan PlanRetry(JobRecord job)
    {
        var payload = RuntimeBuildJobService.ParsePayload(job.PayloadJson);
        if (!RuntimeBuildJobService.CanRetry(job) || payload is null)
            return new RuntimeBuildJobRetryPlan(false, "Only failed, cancelled, or interrupted runtime build jobs can be retried.");

        return new RuntimeBuildJobRetryPlan(
            true,
            "",
            payload.Preset,
            payload.Action.Equals("update", StringComparison.OrdinalIgnoreCase),
            RuntimeSourceFromBuildPayload(payload));
    }

    public async Task<RuntimeBuildJobControlResult> ClearAsync(JobRecord job, CancellationToken cancellationToken = default)
    {
        if (!RuntimeBuildJobService.CanClear(job))
            return new RuntimeBuildJobControlResult(false, "Only completed, failed, cancelled, or interrupted runtime jobs can be cleared.");

        await _stateStore.DeleteJobAsync(job.Id);
        if (LogFileService.TryValidateWorkspaceLogFile(_workspaceRoot, job.LogPath, out var fullPath, out _))
            LogFileService.DeleteLogs([fullPath]);

        return new RuntimeBuildJobControlResult(true, $"Cleared runtime job {job.Id}.");
    }

    public static RuntimeSourceEntry? RuntimeSourceFromBuildPayload(RuntimeBuildJobPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.SourceDir) || !Directory.Exists(payload.SourceDir))
            return null;

        var commit = RuntimeMetadataService.TryReadGitHeadCommit(payload.SourceDir);
        if (string.IsNullOrWhiteSpace(commit))
            commit = RuntimeMetadataService.InferCommitFromText(payload.SourceDir);
        return new RuntimeSourceEntry(
            payload.Preset.Id,
            payload.Preset.Label,
            payload.Preset.RepoUrl,
            payload.Preset.Branch,
            payload.Preset.Cuda,
            payload.SourceDir,
            commit,
            DateTimeOffset.UtcNow,
            RuntimeBuildCatalogService.BackendKey(payload.Preset),
            payload.Mode);
    }
}
