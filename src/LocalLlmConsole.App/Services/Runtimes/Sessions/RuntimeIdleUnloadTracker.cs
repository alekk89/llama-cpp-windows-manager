namespace LocalLlmConsole.Services;

public sealed class RuntimeIdleUnloadTracker
{
    private sealed class IdleState
    {
        public double? PromptCounter;
        public double? GeneratedCounter;
        public DateTimeOffset? LastActivityAt;
    }

    private readonly Dictionary<string, IdleState> _states = new(StringComparer.Ordinal);

    public int Count => _states.Count;

    public bool Observe(
        string runtimeKey,
        RuntimeSlotSnapshot? slotSnapshot,
        double? generatedCounter,
        double? promptCounter,
        int idleMinutes,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(runtimeKey) || idleMinutes <= 0)
            return false;

        var observedGenerated = RuntimeDashboardService.MaxNullable(generatedCounter, slotSnapshot?.GeneratedTokens);
        var observedPrompt = RuntimeDashboardService.MaxNullable(promptCounter, slotSnapshot?.PromptTokensProcessed);
        if (!_states.TryGetValue(runtimeKey, out var state))
        {
            _states[runtimeKey] = new IdleState
            {
                GeneratedCounter = observedGenerated,
                PromptCounter = observedPrompt,
                LastActivityAt = now
            };
            return false;
        }

        var hasTokenDelta = RuntimeDashboardService.PositiveDelta(observedGenerated, state.GeneratedCounter)
            || RuntimeDashboardService.PositiveDelta(observedPrompt, state.PromptCounter);
        var active = slotSnapshot?.IsProcessing == true || hasTokenDelta;
        if (observedGenerated is not null)
            state.GeneratedCounter = observedGenerated;
        if (observedPrompt is not null)
            state.PromptCounter = observedPrompt;

        if (active || state.LastActivityAt is null)
        {
            state.LastActivityAt = now;
            return false;
        }

        return now - state.LastActivityAt.Value >= TimeSpan.FromMinutes(idleMinutes);
    }

    public void Reset() => _states.Clear();

    public void Reset(string runtimeKey)
    {
        if (!string.IsNullOrWhiteSpace(runtimeKey))
            _states.Remove(runtimeKey);
    }

    public void RetainRuntimeKeys(IEnumerable<string> runtimeKeys)
    {
        var active = runtimeKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in _states.Keys.Where(key => !active.Contains(key)).ToArray())
            _states.Remove(key);
    }
}
