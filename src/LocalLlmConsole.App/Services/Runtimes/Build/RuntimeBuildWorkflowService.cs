namespace LocalLlmConsole.Services;

public delegate Task<RuntimeBuildExecutionResult> RuntimeBuildExecutor(RuntimeBuildExecutionRequest request);

public delegate Task<RuntimeSourceUpdateCheck> RuntimeBuildUpdateChecker(RuntimeBuildPreset preset, CancellationToken cancellationToken);

public enum RuntimeBuildWorkflowResultKind
{
    Completed,
    Cancelled,
    NoUpdate
}

public sealed record RuntimeBuildWorkflowRequest(
    RuntimeBuildPreset Preset,
    AppSettings Settings,
    RuntimeBuildPlan Plan,
    RuntimeSourceEntry? Source,
    JobRecord Job,
    bool Update,
    string WslDistro,
    long MaxLogBytes,
    CancellationToken CancellationToken = default,
    Func<string, Task>? ProgressAsync = null);

public sealed record RuntimeBuildWorkflowResult(
    RuntimeBuildWorkflowResultKind Kind,
    string StatusMessage,
    RuntimeBuildExecutionResult? ExecutionResult = null);

public sealed class RuntimeBuildWorkflowService
{
    private readonly JobEngine _jobs;
    private readonly RuntimeBuildExecutor _executeBuildAsync;
    private readonly RuntimeBuildUpdateChecker _checkUpdateAsync;

    public RuntimeBuildWorkflowService(
        JobEngine jobs,
        RuntimeBuildExecutionService executor,
        RuntimeSourceRepositoryService sources,
        StateStore stateStore)
        : this(
            jobs,
            request => executor.ExecuteAsync(request),
            async (preset, cancellationToken) =>
            {
                var installed = RuntimeSourceRepositoryService.LatestInstalledRuntime(preset, await stateStore.ListRuntimesAsync());
                return await sources.CheckUpdateAsync(preset, installed, cancellationToken);
            })
    {
    }

    public RuntimeBuildWorkflowService(
        JobEngine jobs,
        RuntimeBuildExecutor executeBuildAsync,
        RuntimeBuildUpdateChecker checkUpdateAsync)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _executeBuildAsync = executeBuildAsync ?? throw new ArgumentNullException(nameof(executeBuildAsync));
        _checkUpdateAsync = checkUpdateAsync ?? throw new ArgumentNullException(nameof(checkUpdateAsync));
    }

    public async Task<RuntimeBuildWorkflowResult> RunAsync(RuntimeBuildWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sourceDir = request.Source?.SourceDir ?? "";
        try
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            await UpdateJobAsync(
                request,
                JobStatus.Running,
                request.Plan.Action,
                request.Plan.InstallDir,
                request.Update ? "Checking remote repository..." : "Building downloaded source...",
                sourceDir);

            if (request.Update)
            {
                var check = await _checkUpdateAsync(request.Preset, request.CancellationToken);
                request.CancellationToken.ThrowIfCancellationRequested();
                if (check.IsInstalled && !check.HasUpdate)
                {
                    var message = $"Already up to date at {check.LocalCommit}. No new build was created.";
                    await UpdateJobAsync(request, JobStatus.Completed, "update", request.Plan.InstallDir, message, sourceDir);
                    return new RuntimeBuildWorkflowResult(RuntimeBuildWorkflowResultKind.NoUpdate, message);
                }

                var updateMessage = check.IsInstalled && check.HasUpdate
                    ? $"Update found: {RuntimeMetadataService.ShortCommit(check.LocalCommit)} -> {RuntimeMetadataService.ShortCommit(check.RemoteCommit)}. Building new runtime..."
                    : "No installed build was detected. Building a fresh runtime...";
                await UpdateJobAsync(request, JobStatus.Running, "update", request.Plan.InstallDir, updateMessage, sourceDir);
            }

            var buildStart = DateTimeOffset.UtcNow;
            var buildTask = _executeBuildAsync(new RuntimeBuildExecutionRequest(
                request.Preset,
                request.Settings,
                request.Plan,
                request.Source,
                request.Job.LogPath,
                request.Update,
                request.CancellationToken));
            using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken);
            var progressTask = request.ProgressAsync is not null
                ? RunBuildProgressLoopAsync(buildTask, buildStart, request, progressCts.Token)
                : Task.CompletedTask;
            try
            {
                var result = await buildTask;
                progressCts.Cancel();
                await progressTask;
                await UpdateJobAsync(request, JobStatus.Completed, request.Plan.Action, request.Plan.InstallDir, result.StatusMessage, sourceDir);
                return new RuntimeBuildWorkflowResult(RuntimeBuildWorkflowResultKind.Completed, result.StatusMessage, result);
            }
            catch
            {
                progressCts.Cancel();
                try { await progressTask; }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Runtime build progress task completed with an observed exception: {ex.Message}");
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            if (request.CancellationToken.IsCancellationRequested)
            {
                await UpdateJobAsync(request, JobStatus.Cancelled, request.Plan.Action, request.Plan.InstallDir, "Cancelled by user.", sourceDir);
                return new RuntimeBuildWorkflowResult(RuntimeBuildWorkflowResultKind.Cancelled, $"Cancelled {request.Preset.Label} build.");
            }

            await UpdateJobAsync(request, JobStatus.Failed, request.Plan.Action, request.Plan.InstallDir, ex.Message, sourceDir);
            throw;
        }
    }

    private async Task UpdateJobAsync(
        RuntimeBuildWorkflowRequest request,
        JobStatus status,
        string action,
        string installDir,
        string message,
        string sourceDir)
    {
        await RuntimeBuildJobService.AppendJobLogAsync(request.Job.LogPath, status, message, request.MaxLogBytes);
        await _jobs.UpdateAsync(
            request.Job,
            status,
            RuntimeBuildJobService.Payload(
                request.Preset,
                action,
                installDir,
                message,
                request.Plan.ProcessMarker,
                request.WslDistro,
                sourceDir),
            request.CancellationToken.IsCancellationRequested ? CancellationToken.None : request.CancellationToken);
    }

    private static async Task RunBuildProgressLoopAsync(
        Task buildTask,
        DateTimeOffset buildStart,
        RuntimeBuildWorkflowRequest request,
        CancellationToken progressCancellationToken)
    {
        while (!buildTask.IsCompleted && !progressCancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), progressCancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (buildTask.IsCompleted)
                return;

            var elapsed = DateTimeOffset.UtcNow - buildStart;
            var message = $"Building... ({elapsed.TotalMinutes:F0}m {elapsed.Seconds}s elapsed)";
            try
            {
                await request.ProgressAsync!(message);
            }
            catch
            {
                // Progress failures should never abort the build.
            }
        }
    }
}
