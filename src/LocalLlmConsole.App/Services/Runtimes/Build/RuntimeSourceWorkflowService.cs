namespace LocalLlmConsole.Services;

public sealed record RuntimeSourceDownloadWorkflowRequest(
    RuntimeBuildPreset Preset,
    AppSettings Settings,
    long MaxLogBytes,
    CancellationToken CancellationToken = default);

public sealed record RuntimeSourceDownloadWorkflowResult(
    RuntimeSourceEntry Source,
    string StatusMessage,
    JobRecord Job);

public sealed record RuntimeSourceUpdateCheckWorkflowRequest(
    RuntimeBuildPreset Preset,
    RuntimeSourceVersion LocalVersion,
    long MaxLogBytes,
    DateTimeOffset? CheckedAt = null,
    CancellationToken CancellationToken = default);

public sealed record RuntimeSourceUpdateCheckWorkflowResult(
    RuntimeUpdateState State,
    string Message,
    JobRecord Job);

public sealed class RuntimeSourceWorkflowService
{
    private readonly RuntimeSourceRepositoryService _sources;
    private readonly JobEngine _jobs;

    public RuntimeSourceWorkflowService(
        RuntimeSourceRepositoryService sources,
        JobEngine jobs)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public async Task<RuntimeSourceDownloadWorkflowResult> DownloadAsync(RuntimeSourceDownloadWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sourceDir = RuntimeBuildCatalogService.SourceDir(request.Settings.RuntimeRoot, request.Preset);
        var job = await _jobs.CreateAsync("runtime-download", RuntimeBuildJobService.Payload(request.Preset, "download", sourceDir, "Queued."), request.CancellationToken);

        try
        {
            await UpdateJobAsync(request, job, JobStatus.Running, "download", sourceDir, "Downloading repository source...");
            var result = await _sources.DownloadAsync(new RuntimeSourceDownloadRequest(
                request.Preset,
                request.Settings,
                job.LogPath,
                request.MaxLogBytes,
                request.CancellationToken));
            await UpdateJobAsync(request, job, JobStatus.Completed, "download", result.Source.SourceDir, result.StatusMessage);
            return new RuntimeSourceDownloadWorkflowResult(result.Source, result.StatusMessage, job);
        }
        catch (Exception ex)
        {
            await UpdateJobAsync(request, job, JobStatus.Failed, "download", sourceDir, ex.Message);
            throw;
        }
    }

    public async Task<RuntimeSourceUpdateCheckWorkflowResult> CheckUpdateAsync(RuntimeSourceUpdateCheckWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = await _jobs.CreateAsync("runtime-update-check", RuntimeBuildJobService.Payload(request.Preset, "check", request.LocalVersion.Path, "Queued."), request.CancellationToken);

        try
        {
            await UpdateJobAsync(request, job, JobStatus.Running, "Checking remote repository...");
            var remoteCommit = await _sources.RemoteCommitAsync(request.Preset, request.CancellationToken);
            var hasLocalVersion = !string.IsNullOrWhiteSpace(request.LocalVersion.Commit);
            var hasUpdate = !hasLocalVersion || !RuntimeMetadataService.CommitsMatch(request.LocalVersion.Commit, remoteCommit);
            var message = !hasLocalVersion
                ? $"Source available at {RuntimeMetadataService.DisplayCommit(remoteCommit)}. Use Download, then Build."
                : hasUpdate
                ? $"Update available: {RuntimeMetadataService.DisplayCommit(request.LocalVersion.Commit)} -> {RuntimeMetadataService.DisplayCommit(remoteCommit)}. Use Download, then build the downloaded source."
                : $"Already up to date at {RuntimeMetadataService.DisplayCommit(request.LocalVersion.Commit)}.";
            var state = new RuntimeUpdateState(hasUpdate, request.LocalVersion.Commit, remoteCommit, request.CheckedAt ?? DateTimeOffset.UtcNow);
            await UpdateJobAsync(request, job, JobStatus.Completed, message);
            return new RuntimeSourceUpdateCheckWorkflowResult(state, message, job);
        }
        catch (Exception ex)
        {
            await UpdateJobAsync(request, job, JobStatus.Failed, ex.Message);
            throw;
        }
    }

    private async Task UpdateJobAsync(
        RuntimeSourceDownloadWorkflowRequest request,
        JobRecord job,
        JobStatus status,
        string action,
        string installDir,
        string message)
    {
        await RuntimeBuildJobService.AppendJobLogAsync(job.LogPath, status, message, request.MaxLogBytes);
        await _jobs.UpdateAsync(
            job,
            status,
            RuntimeBuildJobService.Payload(request.Preset, action, installDir, message),
            request.CancellationToken);
    }

    private async Task UpdateJobAsync(
        RuntimeSourceUpdateCheckWorkflowRequest request,
        JobRecord job,
        JobStatus status,
        string message)
    {
        await RuntimeBuildJobService.AppendJobLogAsync(job.LogPath, status, message, request.MaxLogBytes);
        await _jobs.UpdateAsync(
            job,
            status,
            RuntimeBuildJobService.Payload(request.Preset, "check", request.LocalVersion.Path, message),
            request.CancellationToken);
    }
}
