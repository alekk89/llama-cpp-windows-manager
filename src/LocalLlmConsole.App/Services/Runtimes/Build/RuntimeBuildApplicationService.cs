namespace LocalLlmConsole.Services;

public enum RuntimeBuildApplicationOutcome
{
    Completed,
    Cancelled,
    NoUpdate
}

public sealed record RuntimeBuildApplicationRequest(
    RuntimeBuildPreset Preset,
    AppSettings Settings,
    bool Update,
    RuntimeSourceEntry? Source,
    long MaxLogBytes,
    DateTimeOffset? QueuedAt = null);

public sealed record RuntimeBuildApplicationActions(
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<Task> RefreshRuntimesAsync,
    Func<Task> RefreshOverviewAsync,
    Action<string> SetStatus,
    Action<string, string> ShowInformation,
    Func<string, Task>? ProgressStatusAsync = null);

public sealed class RuntimeBuildApplicationService
{
    private readonly JobEngine _jobs;
    private readonly RuntimeBuildPrerequisiteService _prerequisites;
    private readonly RuntimeBuildWorkflowService _workflow;
    private readonly RuntimeBuildJobControlService _jobControls;
    private readonly RuntimeCatalogDataService _catalogData;

    public RuntimeBuildApplicationService(
        JobEngine jobs,
        RuntimeBuildPrerequisiteService prerequisites,
        RuntimeBuildWorkflowService workflow,
        RuntimeBuildJobControlService jobControls,
        RuntimeCatalogDataService catalogData)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _prerequisites = prerequisites ?? throw new ArgumentNullException(nameof(prerequisites));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _jobControls = jobControls ?? throw new ArgumentNullException(nameof(jobControls));
        _catalogData = catalogData ?? throw new ArgumentNullException(nameof(catalogData));
    }

    public async Task<RuntimeBuildApplicationOutcome> BuildSourceAsync(
        RuntimeSourceEntry source,
        AppSettings settings,
        long maxLogBytes,
        RuntimeBuildApplicationActions actions,
        DateTimeOffset? queuedAt = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);

        var preset = ResolveSourcePreset(source, settings.RuntimeRoot);
        return await BuildAsync(new RuntimeBuildApplicationRequest(
            preset,
            settings,
            Update: false,
            source,
            maxLogBytes,
            queuedAt), actions);
    }

    public RuntimeBuildPreset ResolveSourcePreset(RuntimeSourceEntry source, string runtimeRoot)
    {
        ArgumentNullException.ThrowIfNull(source);

        return _catalogData.BuildPresets(runtimeRoot)
            .FirstOrDefault(candidate => string.Equals(candidate.Id, source.PresetId, StringComparison.OrdinalIgnoreCase))
            ?? new RuntimeBuildPreset(
                source.PresetId,
                source.Label,
                source.RepoUrl,
                source.Branch,
                source.Cuda,
                Backend: source.Backend,
                Mode: source.Mode);
    }

    public async Task<RuntimeBuildApplicationOutcome> BuildAsync(
        RuntimeBuildApplicationRequest request,
        RuntimeBuildApplicationActions actions)
    {
        Validate(request, actions);

        var backend = RuntimeBuildCatalogService.BuildBackend(request.Preset);
        var mode = RuntimeBuildCatalogService.BuildMode(request.Preset);
        await _prerequisites.EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(mode, backend, request.Settings.WslDistro));

        Directory.CreateDirectory(request.Settings.RuntimeRoot);
        Directory.CreateDirectory(request.Settings.CacheRoot);
        var plan = RuntimeBuildJobService.CreatePlan(
            request.Preset,
            request.Update,
            request.Source,
            request.Settings,
            request.QueuedAt ?? DateTimeOffset.UtcNow);
        var payloadSourceDir = request.Source?.SourceDir ?? "";
        var job = await _jobs.CreateAsync(
            "runtime-build",
            RuntimeBuildJobService.Payload(
                request.Preset,
                plan.Action,
                plan.InstallDir,
                plan.QueuedMessage,
                plan.ProcessMarker,
                request.Settings.WslDistro,
                payloadSourceDir));
        var buildCancellation = _jobControls.RegisterCancellation(job.Id);

        var outcome = RuntimeBuildApplicationOutcome.Completed;
        await actions.RunBusyAsync($"{(request.Update ? "Updating" : "Building")} {request.Preset.Label}...", async () =>
        {
            try
            {
                var result = await _workflow.RunAsync(new RuntimeBuildWorkflowRequest(
                    request.Preset,
                    request.Settings,
                    plan,
                    request.Source,
                    job,
                    request.Update,
                    request.Settings.WslDistro,
                    request.MaxLogBytes,
                    buildCancellation.Token,
                    ProgressAsync: actions.ProgressStatusAsync));
                outcome = await ApplyResultAsync(result, actions);
            }
            finally
            {
                _jobControls.UnregisterCancellation(job.Id, buildCancellation);
            }
        });
        return outcome;
    }

    private static async Task<RuntimeBuildApplicationOutcome> ApplyResultAsync(
        RuntimeBuildWorkflowResult result,
        RuntimeBuildApplicationActions actions)
    {
        if (result.Kind == RuntimeBuildWorkflowResultKind.NoUpdate)
        {
            await actions.RefreshOverviewAsync();
            actions.ShowInformation("Runtime update", result.StatusMessage);
            return RuntimeBuildApplicationOutcome.NoUpdate;
        }

        if (result.Kind == RuntimeBuildWorkflowResultKind.Cancelled)
        {
            actions.SetStatus(result.StatusMessage);
            return RuntimeBuildApplicationOutcome.Cancelled;
        }

        await actions.RefreshRuntimesAsync();
        await actions.RefreshOverviewAsync();
        actions.SetStatus(result.StatusMessage);
        return RuntimeBuildApplicationOutcome.Completed;
    }

    private static void Validate(
        RuntimeBuildApplicationRequest request,
        RuntimeBuildApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Preset);
        ArgumentNullException.ThrowIfNull(request.Settings);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshRuntimesAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshOverviewAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
        ArgumentNullException.ThrowIfNull(actions.ShowInformation);
    }
}
