namespace LocalLlmConsole.Services;

public sealed record JobRowProjection(JobRecord Job, UiRow Row);

public sealed class JobRowProjectionService
{
    private readonly Func<string, bool> _fileExists;

    public JobRowProjectionService(Func<string, bool>? fileExists = null)
    {
        _fileExists = fileExists ?? File.Exists;
    }

    public IReadOnlyList<JobRowProjection> Project(IEnumerable<JobRecord> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        return jobs.Select(Project).ToArray();
    }

    public async Task<IReadOnlyList<JobRowProjection>> ProjectAsync(
        IEnumerable<JobRecord> jobs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var snapshot = jobs.ToArray();
        return await Task.Run(() => Project(snapshot), cancellationToken);
    }

    public JobRowProjection Project(JobRecord job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var logExists = _fileExists(job.LogPath);
        var canCancel = RuntimeBuildJobService.CanCancel(job);
        var canRetry = RuntimeBuildJobService.CanRetry(job);
        var canClear = RuntimeBuildJobService.CanClear(job);
        return new JobRowProjection(job, new UiRow
        {
            C1 = job.Status.ToString(),
            C2 = job.Kind,
            C3 = job.Id,
            C4 = job.UpdatedAt.ToLocalTime().ToString("g"),
            C5 = LogFileService.RuntimeJobProgressSummary(job),
            C6 = "Log",
            C7 = "Cancel",
            C8 = "Retry",
            C9 = "Clear",
            T1 = logExists ? "Open this job's log file." : "This job does not have a log file yet.",
            T2 = canCancel ? "Cancel this running or queued runtime job." : "Only queued or running jobs can be cancelled.",
            T3 = canRetry ? "Retry this failed or interrupted runtime job." : "Only failed or interrupted runtime jobs can be retried.",
            T4 = canClear ? "Remove this finished runtime job from the list." : "Only finished runtime jobs can be cleared.",
            B1 = logExists,
            B2 = canCancel,
            B3 = canRetry,
            B4 = canClear,
            Data = JsonSerializer.SerializeToNode(job) as JsonObject ?? new JsonObject()
        });
    }
}
