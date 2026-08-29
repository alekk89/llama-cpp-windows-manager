
namespace LocalLlmConsole.Services;

public sealed class JobEngine
{
    private readonly StateStore _store;
    private readonly string _logRoot;

    public JobEngine(StateStore store, string logRoot)
    {
        _store = store;
        _logRoot = logRoot;
        Directory.CreateDirectory(_logRoot);
    }

    public async Task<JobRecord> CreateAsync(string kind, string payloadJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var id = $"{kind}-{Guid.NewGuid():N}";
        var job = new JobRecord(id, kind, JobStatus.Queued, payloadJson, Path.Combine(_logRoot, $"{id}.log"), now, now);
        await _store.UpsertJobAsync(job);
        return job;
    }

    public async Task UpdateAsync(JobRecord job, JobStatus status, string payloadJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await UpsertValidatedAsync(job, status, payloadJson);
    }

    public Task RecoverAfterRestartAsync() => _store.MarkInterruptedJobsAsync();

    public static bool IsValidStatusTransition(JobStatus from, JobStatus to)
    {
        if (from == to)
            return true;

        return from switch
        {
            JobStatus.Queued => to is JobStatus.Running or JobStatus.Paused or JobStatus.Cancelled or JobStatus.Failed or JobStatus.Completed or JobStatus.Interrupted,
            JobStatus.Running => to is JobStatus.Paused or JobStatus.Cancelled or JobStatus.Failed or JobStatus.Completed or JobStatus.Interrupted,
            JobStatus.Paused => to is JobStatus.Queued or JobStatus.Cancelled or JobStatus.Failed or JobStatus.Completed,
            JobStatus.Cancelled or JobStatus.Failed or JobStatus.Interrupted => to is JobStatus.Queued or JobStatus.Cancelled or JobStatus.Failed or JobStatus.Completed,
            JobStatus.Completed => false,
            _ => false
        };
    }

    private async Task UpsertValidatedAsync(JobRecord job, JobStatus status, string payloadJson)
    {
        var current = await _store.GetJobAsync(job.Id)
            ?? throw new InvalidOperationException($"Job {job.Id} no longer exists.");
        if (!IsValidStatusTransition(current.Status, status))
            throw new InvalidOperationException($"Invalid job status transition for {job.Id}: {current.Status} -> {status}.");

        var updated = current with
        {
            Status = status,
            PayloadJson = payloadJson,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (await _store.TryUpdateJobAsync(updated, current.Status))
            return;

        var latest = await _store.GetJobAsync(job.Id)
            ?? throw new InvalidOperationException($"Job {job.Id} no longer exists.");
        if (latest.Status == status)
            return;
        if (!IsValidStatusTransition(latest.Status, status))
            throw new InvalidOperationException($"Invalid job status transition for {job.Id}: {latest.Status} -> {status}.");
        throw new InvalidOperationException($"Job {job.Id} changed while updating from {current.Status} to {status}. Current status is {latest.Status}; retry against the latest job state.");
    }
}
