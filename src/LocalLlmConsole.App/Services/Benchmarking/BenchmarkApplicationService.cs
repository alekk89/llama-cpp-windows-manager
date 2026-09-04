using System.Collections.Concurrent;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed record BenchmarkRunSnapshot(
    JobRecord Job,
    BenchmarkJobPayload Payload,
    int PersistedResultRows);

public sealed partial class BenchmarkApplicationService : IAsyncDisposable
{
    public const string JobKind = "benchmark";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
    private readonly StateStore _store;
    private readonly JobEngine _jobs;
    private readonly LoadedModelSessionManager _sessions;
    private readonly BenchmarkPlanService _planner;
    private readonly BenchmarkCapabilityService _capabilities;
    private readonly BenchmarkProcessRunner _processRunner;
    private readonly BenchmarkServingRunner _servingRunner;
    private readonly string _workspaceRoot;
    private readonly SemaphoreSlim _queueGate = new(1, 1);
    private readonly SemaphoreSlim _admissionGate = new(1, 1);
    private readonly ConcurrentDictionary<string, ActiveRun> _activeRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly long _maximumLogBytes;
    private int _shuttingDown;

    public BenchmarkApplicationService(
        StateStore store,
        JobEngine jobs,
        LoadedModelSessionManager sessions,
        BenchmarkCapabilityService capabilities,
        BenchmarkProcessRunner processRunner,
        string workspaceRoot = "",
        long maximumLogBytes = 16 * 1024 * 1024)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _servingRunner = new BenchmarkServingRunner(
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            ownsHttpClient: true);
        _workspaceRoot = workspaceRoot ?? "";
        _planner = new BenchmarkPlanService();
        _maximumLogBytes = Math.Max(maximumLogBytes, 1024 * 1024);
    }

    public event EventHandler<BenchmarkRunSnapshot>? ProgressChanged;

    public int ActiveQueueTaskCount => _activeRuns.Count;

    public async Task<BenchmarkRunSnapshot> StartAsync(
        BenchmarkPlan plan,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed) throw new InvalidOperationException("Starting a benchmark requires explicit confirmation because it applies sustained system load.");
        await _admissionGate.WaitAsync(cancellationToken);
        try
        {
            if (!_activeRuns.IsEmpty) throw new InvalidOperationException("Another benchmark run is already active.");
            var preview = await ValidateAsync(plan, cancellationToken);
            if (!preview.IsValid) throw new InvalidOperationException(string.Join(Environment.NewLine, preview.Errors));
            var payload = new BenchmarkJobPayload(
                plan,
                preview.WorkItems,
                preview.WorkItems.Select(item => new BenchmarkWorkItemCheckpoint(item.Key)).ToArray(),
                null, 0, 0, 0, 0, 1, "Queued", null, null);
            var job = await _jobs.CreateAsync(JobKind, Serialize(payload), cancellationToken);
            Launch(job.Id, isResume: false);
            return new BenchmarkRunSnapshot(job, payload, 0);
        }
        finally { _admissionGate.Release(); }
    }

    public async Task<IReadOnlyList<BenchmarkRunSnapshot>> ListAsync(
        int limit = 100,
        CancellationToken cancellationToken = default,
        int offset = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jobs = await _store.ListBenchmarkJobsAsync(Math.Clamp(limit, 1, 500), Math.Max(offset, 0), cancellationToken);
        var snapshots = new List<BenchmarkRunSnapshot>(jobs.Count);
        foreach (var job in jobs)
            snapshots.Add(await SnapshotAsync(job, cancellationToken));
        return snapshots;
    }

    public async Task<BenchmarkRunSnapshot> InspectAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var job = await _store.GetJobAsync(jobId) ?? throw new KeyNotFoundException($"Benchmark run '{jobId}' was not found.");
        if (!job.Kind.Equals(JobKind, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Job '{jobId}' is not a benchmark run.");
        return await SnapshotAsync(job, cancellationToken);
    }

    public async Task<BenchmarkRunSnapshot> WaitForRevisionAsync(
        string jobId,
        long afterRevision,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var current = await InspectAsync(jobId, cancellationToken);
        if (current.Payload.Revision > afterRevision || IsTerminal(current.Job.Status)) return current;
        var completion = new TaskCompletionSource<BenchmarkRunSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, BenchmarkRunSnapshot snapshot)
        {
            if (snapshot.Job.Id.Equals(jobId, StringComparison.OrdinalIgnoreCase)
                && (snapshot.Payload.Revision > afterRevision || IsTerminal(snapshot.Job.Status)))
                completion.TrySetResult(snapshot);
        }
        ProgressChanged += Handler;
        try
        {
            current = await InspectAsync(jobId, cancellationToken);
            if (current.Payload.Revision > afterRevision || IsTerminal(current.Job.Status)) return current;
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : timeout);
            try { return await completion.Task.WaitAsync(timeoutCancellation.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return await InspectAsync(jobId, cancellationToken);
            }
        }
        finally
        {
            ProgressChanged -= Handler;
        }
    }

    public async Task PauseAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var snapshot = await InspectAsync(jobId, cancellationToken);
        if (snapshot.Job.Status == JobStatus.Paused) return;
        if (!_activeRuns.TryGetValue(jobId, out var active))
            throw new InvalidOperationException("The benchmark is not currently running.");
        active.PauseRequested = true;
    }

    public async Task CancelAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var snapshot = await InspectAsync(jobId, cancellationToken);
        if (snapshot.Job.Status is JobStatus.Cancelled or JobStatus.Completed or JobStatus.Failed) return;
        if (_activeRuns.TryGetValue(jobId, out var active))
        {
            active.Cancellation.Cancel();
            return;
        }
        var payload = snapshot.Payload with
        {
            Outcome = BenchmarkRunOutcome.Cancelled,
            Message = "Cancelled",
            CompletedAt = DateTimeOffset.UtcNow,
            Revision = snapshot.Payload.Revision + 1
        };
        await PublishMessageAsync(snapshot.Job, payload, JobStatus.Cancelled);
    }

    public async Task ResumeAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await _admissionGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await InspectAsync(jobId, cancellationToken);
            if (snapshot.Job.Status == JobStatus.Completed) throw new InvalidOperationException("Completed benchmark runs cannot be resumed.");
            if (_activeRuns.ContainsKey(jobId)) return;
            if (!_activeRuns.IsEmpty) throw new InvalidOperationException("Another benchmark run is already active.");
            var payload = snapshot.Payload with { Outcome = null, Message = "Queued to resume", CompletedAt = null, Revision = snapshot.Payload.Revision + 1 };
            await _jobs.UpdateAsync(snapshot.Job, JobStatus.Queued, Serialize(payload), cancellationToken);
            Launch(jobId, isResume: true);
        }
        finally { _admissionGate.Release(); }
    }

    private void Launch(string jobId, bool isResume)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _shuttingDown) == 1, this);
        var active = new ActiveRun();
        if (!_activeRuns.TryAdd(jobId, active)) throw new InvalidOperationException("The benchmark run is already active.");
        active.Task = Task.Run(() => ExecuteAsync(jobId, active, isResume));
    }

    private async Task ExecuteAsync(string jobId, ActiveRun active, bool isResume)
    {
        var gateAcquired = false;
        try
        {
            await _queueGate.WaitAsync(active.Cancellation.Token);
            gateAcquired = true;
            var snapshot = await InspectAsync(jobId);
            var payload = snapshot.Payload with
            {
                StartedAt = snapshot.Payload.StartedAt ?? DateTimeOffset.UtcNow,
                Message = isResume ? "Resumed" : "Running",
                Revision = snapshot.Payload.Revision + 1
            };
            await PublishMessageAsync(snapshot.Job, payload, JobStatus.Running);
            await using var computeLease = await _sessions.AcquireBenchmarkLeaseAsync(payload.Plan.StopActiveSessions, active.Cancellation.Token);
            using var awake = BenchmarkSystemAwakeLease.Acquire(payload.Plan.PreventSystemSleep);
            for (var index = 0; index < payload.WorkItems.Count; index++)
            {
                if (active.PauseRequested)
                {
                    payload = payload with { CurrentWorkItemIndex = index, Message = "Paused", Revision = payload.Revision + 1 };
                    await PublishMessageAsync((await InspectAsync(jobId)).Job, payload, JobStatus.Paused);
                    return;
                }
                active.Cancellation.Token.ThrowIfCancellationRequested();
                var checkpoint = payload.Checkpoints[index];
                if (checkpoint.Status == BenchmarkWorkItemStatus.Passed) continue;
                var item = payload.WorkItems[index];
                var attempt = checkpoint.Attempt + 1;
                payload = UpdateCheckpoint(payload, index, checkpoint with { Status = BenchmarkWorkItemStatus.Running, Attempt = attempt, Error = "" }) with
                {
                    CurrentWorkItemIndex = index,
                    Message = $"Running {item.ModelName} with {item.RuntimeName}",
                    Revision = payload.Revision + 1
                };
                await PublishMessageAsync((await InspectAsync(jobId)).Job, payload, JobStatus.Running);
                var outcome = await ExecuteWorkItemAsync(jobId, payload, item, attempt, computeLease, active.Cancellation.Token);
                if (outcome.Cancelled)
                {
                    if (!outcome.VerifiedStopped) throw new InvalidOperationException("Cancellation could not verify that llama-bench stopped.");
                    throw new OperationCanceledException(active.Cancellation.Token);
                }
                if (outcome.ExitCode == 0 && outcome.ResultRows > 0)
                {
                    await _store.CompleteBenchmarkAttemptAsync(jobId, item.Key, attempt);
                    payload = UpdateCheckpoint(payload, index, new BenchmarkWorkItemCheckpoint(item.Key, BenchmarkWorkItemStatus.Passed, attempt, outcome.ResultRows));
                }
                else
                {
                    var error = !string.IsNullOrWhiteSpace(outcome.Error)
                        ? outcome.Error
                        : outcome.ExitCode == 0 ? "The benchmark emitted no valid result rows." : $"The benchmark runner exited with code {outcome.ExitCode}.";
                    payload = UpdateCheckpoint(payload, index, new BenchmarkWorkItemCheckpoint(item.Key, BenchmarkWorkItemStatus.Failed, attempt, outcome.ResultRows, error));
                    if (ShouldRetry(payload.Plan.FailurePolicy, attempt))
                    {
                        index--;
                        continue;
                    }
                    if (ShouldStop(payload.Plan.FailurePolicy))
                        throw new InvalidOperationException(error);
                }
                payload = Recalculate(payload) with { Revision = payload.Revision + 1 };
                await PublishMessageAsync((await InspectAsync(jobId)).Job, payload, JobStatus.Running);
                if (active.PauseRequested && index + 1 < payload.WorkItems.Count)
                {
                    payload = payload with { CurrentWorkItemIndex = index + 1, Message = "Paused", Revision = payload.Revision + 1 };
                    await PublishMessageAsync((await InspectAsync(jobId)).Job, payload, JobStatus.Paused);
                    return;
                }
                if (payload.Plan.CooldownSeconds > 0 && index + 1 < payload.WorkItems.Count)
                    await Task.Delay(TimeSpan.FromSeconds(payload.Plan.CooldownSeconds), active.Cancellation.Token);
            }
            await computeLease.DisposeAsync();
            payload = Recalculate(payload) with
            {
                Outcome = payload.FailedWorkItems > 0 ? BenchmarkRunOutcome.Partial : BenchmarkRunOutcome.Success,
                Message = payload.FailedWorkItems > 0 ? "Completed with failed work items" : "Completed",
                CompletedAt = DateTimeOffset.UtcNow,
                Revision = payload.Revision + 1
            };
            await PublishMessageAsync((await InspectAsync(jobId)).Job, payload, JobStatus.Completed);
        }
        catch (OperationCanceledException) when (active.Cancellation.IsCancellationRequested)
        {
            await MarkTerminalAsync(jobId,
                Volatile.Read(ref _shuttingDown) == 1 ? JobStatus.Interrupted : JobStatus.Cancelled,
                Volatile.Read(ref _shuttingDown) == 1 ? BenchmarkRunOutcome.Interrupted : BenchmarkRunOutcome.Cancelled,
                Volatile.Read(ref _shuttingDown) == 1 ? "Interrupted during application shutdown" : "Cancelled");
        }
        catch (Exception ex)
        {
            await MarkTerminalAsync(jobId, JobStatus.Failed, BenchmarkRunOutcome.Failed, ex.Message);
        }
        finally
        {
            _activeRuns.TryRemove(jobId, out _);
            if (gateAcquired) _queueGate.Release();
            active.Cancellation.Dispose();
        }
    }

    private void PublishTransient(JobRecord job, BenchmarkJobPayload payload, string message, int resultRows)
        => PublishProgress(new BenchmarkRunSnapshot(
            job,
            payload with { Message = message, ResultRows = resultRows },
            resultRows));

    private async Task AppendLogAsync(string? logPath, string text)
    {
        if (string.IsNullOrWhiteSpace(logPath) || string.IsNullOrWhiteSpace(text)) return;
        await BoundedLogFile.AppendAsync(logPath, text + Environment.NewLine, _maximumLogBytes);
    }

    private async Task MarkTerminalAsync(string jobId, JobStatus status, BenchmarkRunOutcome outcome, string message)
    {
        try
        {
            var snapshot = await InspectAsync(jobId);
            var payload = Recalculate(snapshot.Payload) with
            {
                Outcome = outcome,
                Message = message,
                CompletedAt = DateTimeOffset.UtcNow,
                Revision = snapshot.Payload.Revision + 1
            };
            await PublishMessageAsync(snapshot.Job, payload, status);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Could not persist terminal benchmark state for {jobId}: {ex}");
        }
    }

    private async Task PublishMessageAsync(JobRecord job, BenchmarkJobPayload payload, JobStatus status)
    {
        await _jobs.UpdateAsync(job, status, Serialize(payload));
        var updated = await _store.GetJobAsync(job.Id) ?? job with { Status = status, PayloadJson = Serialize(payload) };
        PublishProgress(new BenchmarkRunSnapshot(updated, payload, payload.ResultRows));
    }

    private async Task<BenchmarkRunSnapshot> SnapshotAsync(JobRecord job, CancellationToken cancellationToken)
    {
        var payload = Deserialize(job.PayloadJson);
        var count = await _store.CountBenchmarkResultsAsync(job.Id, cancellationToken);
        return new BenchmarkRunSnapshot(job, payload, count);
    }

    private void PublishProgress(BenchmarkRunSnapshot snapshot)
    {
        foreach (EventHandler<BenchmarkRunSnapshot> handler in ProgressChanged?.GetInvocationList() ?? [])
        {
            try { handler(this, snapshot); }
            catch (Exception ex) { Trace.TraceWarning($"Benchmark progress subscriber failed: {ex.Message}"); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) == 1) return;
        var active = _activeRuns.Values.ToArray();
        foreach (var run in active) run.Cancellation.Cancel();
        var tasksCompleted = false;
        try
        {
            await Task.WhenAll(active.Select(run => run.Task ?? Task.CompletedTask)).WaitAsync(TimeSpan.FromSeconds(20));
            tasksCompleted = true;
        }
        catch (Exception ex) { Trace.TraceWarning($"Benchmark shutdown cleanup did not finish cleanly: {ex.Message}"); }
        if (tasksCompleted)
        {
            _servingRunner.Dispose();
            _queueGate.Dispose();
            _admissionGate.Dispose();
        }
    }

    private sealed class ActiveRun
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public Task? Task { get; set; }
        public volatile bool PauseRequested;
    }

    private sealed record WorkItemOutcome(int ExitCode, int ResultRows, bool Cancelled, bool VerifiedStopped, string Error);
}
