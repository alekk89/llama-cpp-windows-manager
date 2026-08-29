namespace LocalLlmConsole.Services;

public sealed class RuntimeLifetimeCounterTracker
{
    private sealed class CounterState
    {
        public double? PromptCounter;
        public double? CachedPromptCounter;
        public double? GeneratedCounter;
        public double? PromptSecondsCounter;
        public double? GeneratedSecondsCounter;
        public double? RequestCounter;
        public double? FailedRequestCounter;
        public bool SlotsInitialized;
        public bool UsingSlotFallback;
        public Dictionary<string, SlotCounterState> Slots { get; } = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, CounterState> _states = new(StringComparer.Ordinal);

    public int Count => _states.Count;

    public TokenUsageDelta Observe(
        string runtimeKey,
        string modelId,
        string modelName,
        double? generatedCounter,
        double? promptCounter,
        RuntimeSlotSnapshot? slotSnapshot,
        double? cachedPromptCounter = null,
        DateTimeOffset? capturedAt = null,
        double? generatedSecondsCounter = null,
        double? promptSecondsCounter = null,
        double? requestCounter = null,
        double? failedRequestCounter = null)
    {
        if (string.IsNullOrWhiteSpace(runtimeKey) || string.IsNullOrWhiteSpace(modelId))
            return TokenUsageDelta.Empty;

        if (generatedCounter is null && promptCounter is null && cachedPromptCounter is null
            && generatedSecondsCounter is null && promptSecondsCounter is null
            && requestCounter is null && failedRequestCounter is null
            && slotSnapshot is null)
            return TokenUsageDelta.Empty;

        if (!_states.TryGetValue(runtimeKey, out var state))
        {
            state = new CounterState();
            _states[runtimeKey] = state;
            if (generatedCounter is not null || promptCounter is not null || cachedPromptCounter is not null
                || generatedSecondsCounter is not null || promptSecondsCounter is not null
                || requestCounter is not null || failedRequestCounter is not null)
            {
                RememberCumulativeCounters(
                    state,
                    generatedCounter,
                    promptCounter,
                    cachedPromptCounter,
                    generatedSecondsCounter,
                    promptSecondsCounter,
                    requestCounter,
                    failedRequestCounter);
            }
            else if (slotSnapshot is not null)
            {
                RememberSlots(state, SlotCounters(slotSnapshot));
                state.SlotsInitialized = true;
                state.UsingSlotFallback = true;
            }
            return TokenUsageDelta.Empty;
        }

        long generatedDelta;
        long promptDelta;
        long cachedPromptDelta;
        var switchedFromSlots = false;
        if (generatedCounter is not null || promptCounter is not null || cachedPromptCounter is not null
            || generatedSecondsCounter is not null || promptSecondsCounter is not null
            || requestCounter is not null || failedRequestCounter is not null)
        {
            if (state.UsingSlotFallback)
            {
                RememberCumulativeCounters(
                    state,
                    generatedCounter,
                    promptCounter,
                    cachedPromptCounter,
                    generatedSecondsCounter,
                    promptSecondsCounter,
                    requestCounter,
                    failedRequestCounter);
                state.UsingSlotFallback = false;
                switchedFromSlots = true;
                generatedDelta = 0;
                promptDelta = 0;
                cachedPromptDelta = 0;
            }
            else
            {
                generatedDelta = RuntimeDashboardService.WholePositiveDeltaAndRemember(generatedCounter, ref state.GeneratedCounter);
                promptDelta = RuntimeDashboardService.WholePositiveDeltaAndRemember(promptCounter, ref state.PromptCounter);
                cachedPromptDelta = RuntimeDashboardService.WholePositiveDeltaAndRemember(cachedPromptCounter, ref state.CachedPromptCounter);
            }
        }
        else
        {
            if (!state.UsingSlotFallback)
            {
                RememberSlots(state, SlotCounters(slotSnapshot!));
                state.SlotsInitialized = true;
                state.UsingSlotFallback = true;
                promptDelta = 0;
                generatedDelta = 0;
                cachedPromptDelta = 0;
            }
            else
            {
                (promptDelta, generatedDelta) = ObserveSlotDeltas(state, slotSnapshot!);
                cachedPromptDelta = 0;
            }
        }
        var generatedSecondsDelta = switchedFromSlots
            ? 0
            : RuntimeDashboardService.PositiveAmountDeltaAndRemember(generatedSecondsCounter, ref state.GeneratedSecondsCounter);
        var promptSecondsDelta = switchedFromSlots
            ? 0
            : RuntimeDashboardService.PositiveAmountDeltaAndRemember(promptSecondsCounter, ref state.PromptSecondsCounter);
        var requestDelta = switchedFromSlots
            ? 0
            : RuntimeDashboardService.WholePositiveDeltaAndRemember(requestCounter, ref state.RequestCounter);
        var failedRequestDelta = switchedFromSlots
            ? 0
            : RuntimeDashboardService.WholePositiveDeltaAndRemember(failedRequestCounter, ref state.FailedRequestCounter);
        if (generatedDelta <= 0 && promptDelta <= 0 && cachedPromptDelta <= 0
            && requestDelta <= 0 && failedRequestDelta <= 0)
            return TokenUsageDelta.Empty;

        return new TokenUsageDelta(
            modelId,
            modelName,
            promptDelta,
            generatedDelta,
            cachedPromptDelta,
            CacheCounterObserved: cachedPromptCounter is not null,
            CapturedAt: capturedAt,
            PromptSeconds: promptSecondsDelta,
            GeneratedSeconds: generatedSecondsDelta,
            TimingCounterObserved: generatedSecondsCounter is not null || promptSecondsCounter is not null,
            RequestCount: requestDelta,
            FailedRequestCount: failedRequestDelta,
            RequestCounterObserved: requestCounter is not null);
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

    private static (long Prompt, long Generated) ObserveSlotDeltas(CounterState state, RuntimeSlotSnapshot snapshot)
    {
        var counters = SlotCounters(snapshot);
        if (!state.SlotsInitialized)
        {
            RememberSlots(state, counters);
            state.SlotsInitialized = true;
            return (0, 0);
        }

        double prompt = 0;
        double generated = 0;
        foreach (var counter in counters)
        {
            var hadPrevious = state.Slots.TryGetValue(counter.SlotId, out var previous);
            prompt += hadPrevious
                ? CounterDelta(counter.PromptTokensProcessed, previous!.PromptTokens, counter.TaskId, previous.TaskId)
                : Math.Max(0, counter.PromptTokensProcessed);
            generated += hadPrevious
                ? CounterDelta(counter.GeneratedTokens, previous!.GeneratedTokens, counter.TaskId, previous.TaskId)
                : Math.Max(0, counter.GeneratedTokens);
        }
        RememberSlots(state, counters);
        return (Math.Max(0, (long)Math.Floor(prompt)), Math.Max(0, (long)Math.Floor(generated)));
    }

    private static IReadOnlyList<RuntimeSlotCounterSnapshot> SlotCounters(RuntimeSlotSnapshot snapshot)
        => snapshot.SlotCounters is { Count: > 0 } counters
            ? counters
            : [new RuntimeSlotCounterSnapshot("aggregate", "", snapshot.PromptTokensProcessed, snapshot.GeneratedTokens, snapshot.IsProcessing)];

    private static double CounterDelta(double current, double previous, string currentTaskId, string previousTaskId)
        => current >= previous && string.Equals(currentTaskId, previousTaskId, StringComparison.Ordinal)
            ? current - previous
            : Math.Max(0, current);

    private static void RememberSlots(CounterState state, IReadOnlyList<RuntimeSlotCounterSnapshot> counters)
    {
        foreach (var counter in counters)
            state.Slots[counter.SlotId] = new SlotCounterState(counter.TaskId, counter.PromptTokensProcessed, counter.GeneratedTokens);
    }

    private static void RememberCumulativeCounters(
        CounterState state,
        double? generated,
        double? prompt,
        double? cachedPrompt,
        double? generatedSeconds,
        double? promptSeconds,
        double? requests,
        double? failedRequests)
    {
        state.GeneratedCounter = generated;
        state.PromptCounter = prompt;
        state.CachedPromptCounter = cachedPrompt;
        state.GeneratedSecondsCounter = generatedSeconds;
        state.PromptSecondsCounter = promptSeconds;
        state.RequestCounter = requests;
        state.FailedRequestCounter = failedRequests;
    }

    private sealed record SlotCounterState(string TaskId, double PromptTokens, double GeneratedTokens);
}
