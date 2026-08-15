namespace LocalLlmConsole.Services;

public sealed class RuntimeIdleUnloadPolicyService
{
    private sealed record IdleCandidate(
        RuntimeMetricPollResult Result,
        EffectiveModelRetentionPolicy Policy);

    private readonly RuntimeIdleUnloadTracker _tracker;
    private bool _isApplying;

    public RuntimeIdleUnloadPolicyService()
        : this(new RuntimeIdleUnloadTracker())
    {
    }

    public RuntimeIdleUnloadPolicyService(RuntimeIdleUnloadTracker tracker)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
    }

    public bool IsApplying => _isApplying;

    public int TrackedRuntimeCount => _tracker.Count;

    public async Task<int> ApplyAsync(
        IReadOnlyList<RuntimeMetricPollResult> pollResults,
        int idleMinutes,
        DateTimeOffset now,
        Func<RuntimeMetricPollResult, CancellationToken, Task> unloadAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pollResults);
        ArgumentNullException.ThrowIfNull(unloadAsync);

        return await ApplyAsync(
            pollResults,
            _ => new EffectiveModelRetentionPolicy(
                idleMinutes > 0,
                idleMinutes,
                ModelGroupEvictionPriority.Normal),
            now,
            unloadAsync,
            maximumUnloads: int.MaxValue,
            cancellationToken);
    }

    public async Task<int> ApplyAsync(
        IReadOnlyList<RuntimeMetricPollResult> pollResults,
        Func<RuntimeMetricPollResult, EffectiveModelRetentionPolicy> policyFor,
        DateTimeOffset now,
        Func<RuntimeMetricPollResult, CancellationToken, Task> unloadAsync,
        int maximumUnloads = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pollResults);
        ArgumentNullException.ThrowIfNull(policyFor);
        ArgumentNullException.ThrowIfNull(unloadAsync);

        if (_isApplying)
            return 0;

        if (pollResults.Count == 0)
        {
            Reset();
            return 0;
        }

        _tracker.RetainRuntimeKeys(pollResults.Select(result => result.RuntimeKey));
        var idleSessions = IdleSessions(pollResults, policyFor, now)
            .OrderBy(candidate => candidate.Policy.EvictionPriority)
            .ThenBy(candidate => candidate.Result.Session.StartedAt)
            .ThenBy(candidate => candidate.Result.Session.ModelName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, maximumUnloads))
            .ToArray();
        if (idleSessions.Length == 0)
            return 0;

        _isApplying = true;
        try
        {
            var unloaded = 0;
            foreach (var idle in idleSessions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await unloadAsync(idle.Result, cancellationToken);
                unloaded++;
            }

            return unloaded;
        }
        finally
        {
            _isApplying = false;
        }
    }

    public void Reset()
        => _tracker.Reset();

    public void Reset(string runtimeKey)
        => _tracker.Reset(runtimeKey);

    private List<RuntimeMetricPollResult> IdleSessions(
        IReadOnlyList<RuntimeMetricPollResult> pollResults,
        int idleMinutes,
        DateTimeOffset now)
    {
        var idleSessions = new List<RuntimeMetricPollResult>();
        foreach (var result in pollResults)
        {
            var generatedCounter = RuntimeDashboardService.GeneratedTokenCounter(result.Samples);
            var promptCounter = RuntimeDashboardService.PromptActivityTokenCounter(result.Samples);
            if (_tracker.Observe(result.RuntimeKey, result.SlotSnapshot, generatedCounter, promptCounter, idleMinutes, now))
                idleSessions.Add(result);
        }

        return idleSessions;
    }

    private List<IdleCandidate> IdleSessions(
        IReadOnlyList<RuntimeMetricPollResult> pollResults,
        Func<RuntimeMetricPollResult, EffectiveModelRetentionPolicy> policyFor,
        DateTimeOffset now)
    {
        var idleSessions = new List<IdleCandidate>();
        foreach (var result in pollResults)
        {
            var policy = policyFor(result);
            if (!policy.AllowsIdleUnload || policy.IdleMinutes <= 0)
            {
                _tracker.Reset(result.RuntimeKey);
                continue;
            }

            var generatedCounter = RuntimeDashboardService.GeneratedTokenCounter(result.Samples);
            var promptCounter = RuntimeDashboardService.PromptActivityTokenCounter(result.Samples);
            if (_tracker.Observe(result.RuntimeKey, result.SlotSnapshot, generatedCounter, promptCounter, policy.IdleMinutes, now))
                idleSessions.Add(new IdleCandidate(result, policy));
        }

        return idleSessions;
    }
}
