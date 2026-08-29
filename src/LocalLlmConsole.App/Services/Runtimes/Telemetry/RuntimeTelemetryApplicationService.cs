namespace LocalLlmConsole.Services;

public sealed record RuntimeIdleUnloadApplicationActions(
    Func<string, Task<ModelRecord?>> FindModelByIdAsync,
    Func<ModelRecord, Task> StopModelRuntimeAsync,
    Action<string> SetStatus,
    Func<string, int, EffectiveModelRetentionPolicy>? ResolveRetentionPolicy = null);

public sealed class RuntimeTelemetryApplicationService
{
    private readonly RuntimeMetricPollerService _poller;
    private readonly RuntimeDashboardRefreshCoordinator _refreshCoordinator;
    private readonly RuntimeMetricSummaryTracker _metricSummaries;
    private readonly RuntimeLifetimeCounterTracker _lifetimeCounters;
    private readonly RuntimeIdleUnloadPolicyService _idleUnloadPolicy;

    public RuntimeTelemetryApplicationService(
        RuntimeMetricPollerService poller,
        RuntimeDashboardRefreshCoordinator refreshCoordinator,
        RuntimeMetricSummaryTracker metricSummaries,
        RuntimeLifetimeCounterTracker lifetimeCounters,
        RuntimeIdleUnloadPolicyService idleUnloadPolicy)
    {
        _poller = poller ?? throw new ArgumentNullException(nameof(poller));
        _refreshCoordinator = refreshCoordinator ?? throw new ArgumentNullException(nameof(refreshCoordinator));
        _metricSummaries = metricSummaries ?? throw new ArgumentNullException(nameof(metricSummaries));
        _lifetimeCounters = lifetimeCounters ?? throw new ArgumentNullException(nameof(lifetimeCounters));
        _idleUnloadPolicy = idleUnloadPolicy ?? throw new ArgumentNullException(nameof(idleUnloadPolicy));
    }

    public bool ShouldRunRefreshTimer(string currentPage, bool hasRunningSessions)
        => _refreshCoordinator.ShouldRunTimer(currentPage, hasRunningSessions);

    public IDisposable? TryBeginRefresh(RuntimeDashboardRefreshTarget target)
        => _refreshCoordinator.TryBeginRefresh(target);

    public Task<RuntimeMetricPollResult[]> PollSessionsAsync(
        IEnumerable<LoadedModelSessionSnapshot> sessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var pollableSessions = _refreshCoordinator.PollableSessions(sessions);
        return _poller.PollSessionsAsync(pollableSessions, cancellationToken);
    }

    public RuntimeMetricSummaryResult ApplyMetricSummary(
        string runtimeKey,
        IReadOnlyList<PrometheusSample> samples,
        AppSettings metricsSettings,
        RuntimeSlotSnapshot? slotSnapshot,
        RuntimeMtpTokenSnapshot? mtpTokenSnapshot,
        DateTimeOffset? capturedAt = null)
        => _metricSummaries.Apply(runtimeKey, samples, metricsSettings, slotSnapshot, mtpTokenSnapshot, capturedAt);

    public IReadOnlyList<PrometheusSample> LastKnownSamples(string runtimeKey)
        => _metricSummaries.LastKnownSamples(runtimeKey);

    public void ResetMetricCounters()
        => _metricSummaries.Reset();

    public IReadOnlyList<TokenUsageDelta> ObserveLifetimeTokenDeltas(
        IReadOnlyList<RuntimeMetricPollResult> pollResults,
        DateTimeOffset? capturedAt = null)
    {
        ArgumentNullException.ThrowIfNull(pollResults);

        _lifetimeCounters.RetainRuntimeKeys(pollResults.Select(result => result.RuntimeKey));
        var deltas = new List<TokenUsageDelta>();
        foreach (var result in pollResults)
        {
            var generatedCounter = RuntimeDashboardService.GeneratedTokenCounter(result.Samples);
            var promptCounter = RuntimeDashboardService.PromptTokensProcessedCounter(result.Samples);
            var cachedPromptCounter = RuntimeDashboardService.PromptCachedTokenCounter(result.Samples);
            var generatedSecondsCounter = RuntimeDashboardService.GeneratedSecondsCounter(result.Samples);
            var promptSecondsCounter = RuntimeDashboardService.PromptSecondsCounter(result.Samples);
            var requestCounter = RuntimeDashboardService.CompletedRequestCounter(result.Samples);
            var failedRequestCounter = RuntimeDashboardService.FailedRequestCounter(result.Samples);
            var delta = _lifetimeCounters.Observe(
                result.RuntimeKey,
                result.Session.ModelId,
                result.Session.ModelName,
                generatedCounter,
                promptCounter,
                result.SlotSnapshot,
                cachedPromptCounter,
                capturedAt,
                generatedSecondsCounter,
                promptSecondsCounter,
                requestCounter,
                failedRequestCounter);

            if (delta.HasActivity)
            {
                deltas.Add(delta with
                {
                    LaunchProfileId = result.Session.LaunchProfileId,
                    LaunchProfileName = result.Session.LaunchProfileName,
                    RuntimeId = result.Session.RuntimeId,
                    RuntimeName = result.Session.RuntimeName,
                    RuntimeMode = result.Session.Mode,
                    RuntimeBackend = result.Session.Backend
                });
            }
        }

        return deltas;
    }

    public void ResetLifetimeCounters()
        => _lifetimeCounters.Reset();

    public void ResetLifetimeCounters(LoadedModelSessionSnapshot? session)
    {
        if (session is not null)
            _lifetimeCounters.Reset(RuntimeMetricPollerService.RuntimeKey(session));
    }

    public Task<int> ApplyIdleUnloadPoliciesAsync(
        IReadOnlyList<RuntimeMetricPollResult> pollResults,
        int idleMinutes,
        DateTimeOffset now,
        Func<RuntimeMetricPollResult, CancellationToken, Task> unloadAsync,
        CancellationToken cancellationToken = default)
        => _idleUnloadPolicy.ApplyAsync(pollResults, idleMinutes, now, unloadAsync, cancellationToken);

    public Task<int> ApplyIdleUnloadPoliciesAsync(
        IReadOnlyList<RuntimeMetricPollResult> pollResults,
        int idleMinutes,
        DateTimeOffset now,
        RuntimeIdleUnloadApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        Validate(actions);
        if (actions.ResolveRetentionPolicy is not null)
        {
            var policies = new Dictionary<string, EffectiveModelRetentionPolicy>(StringComparer.OrdinalIgnoreCase);
            foreach (var result in pollResults)
                policies[result.RuntimeKey] = actions.ResolveRetentionPolicy(result.Session.LaunchProfileId, idleMinutes);
            return _idleUnloadPolicy.ApplyAsync(
                pollResults,
                result => policies[result.RuntimeKey],
                now,
                async (idle, _) =>
                {
                    var model = await actions.FindModelByIdAsync(idle.Session.ModelId);
                    if (model is null) return;

                    var policy = policies[idle.RuntimeKey];
                    actions.SetStatus(AutoUnloadStatus(model, policy.IdleMinutes, policy.GroupName));
                    await actions.StopModelRuntimeAsync(model);
                },
                maximumUnloads: 1,
                cancellationToken);
        }

        return ApplyIdleUnloadPoliciesAsync(
            pollResults,
            idleMinutes,
            now,
            async (idle, _) =>
            {
                var model = await actions.FindModelByIdAsync(idle.Session.ModelId);
                if (model is null) return;

                actions.SetStatus(AutoUnloadStatus(model, idleMinutes));
                await actions.StopModelRuntimeAsync(model);
            },
            cancellationToken);
    }

    public void ResetIdleCounters()
        => _idleUnloadPolicy.Reset();

    public void ResetIdleCounters(LoadedModelSessionSnapshot? session)
    {
        if (session is not null)
            _idleUnloadPolicy.Reset(RuntimeMetricPollerService.RuntimeKey(session));
    }

    public static string AutoUnloadStatus(ModelRecord model, int idleMinutes)
    {
        ArgumentNullException.ThrowIfNull(model);
        return $"Auto-unloading {model.Name} after {idleMinutes} idle minute{(idleMinutes == 1 ? "" : "s")}.";
    }

    public static string AutoUnloadStatus(ModelRecord model, int idleMinutes, string groupName)
        => string.IsNullOrWhiteSpace(groupName)
            ? AutoUnloadStatus(model, idleMinutes)
            : $"Auto-unloading {model.Name} from group {groupName} after {idleMinutes} idle minute{(idleMinutes == 1 ? "" : "s")}.";

    private static void Validate(RuntimeIdleUnloadApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.FindModelByIdAsync);
        ArgumentNullException.ThrowIfNull(actions.StopModelRuntimeAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
