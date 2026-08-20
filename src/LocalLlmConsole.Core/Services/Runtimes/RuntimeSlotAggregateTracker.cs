namespace LocalLlmConsole.Services;

internal static class RuntimeSlotAggregateTracker
{
    public static SlotAggregateObservation ObserveSlots(
        RuntimeMetricSummaryState state,
        RuntimeSlotSnapshot? snapshot,
        DateTimeOffset now)
    {
        if (snapshot is null)
            return new SlotAggregateObservation(null, null, null, null, null, null);

        var counters = SlotCounters(snapshot);
        if (!state.SlotCountersInitialized)
        {
            state.CumulativeSlotPromptTokens = counters.Sum(counter => Math.Max(0, counter.PromptTokensProcessed));
            state.CumulativeSlotGeneratedTokens = counters.Sum(counter => Math.Max(0, counter.GeneratedTokens));
            state.CumulativeSlotMtpGeneratedTokens = SumOptional(counters.Select(counter => counter.MtpGeneratedTokens));
            state.CumulativeSlotMtpAcceptedTokens = SumOptional(counters.Select(counter => counter.MtpAcceptedTokens));
            state.SlotCountersInitialized = true;
            RememberSlotCounters(state, counters, now);
            return new SlotAggregateObservation(
                null,
                null,
                state.CumulativeSlotPromptTokens,
                state.CumulativeSlotGeneratedTokens,
                state.CumulativeSlotMtpGeneratedTokens,
                state.CumulativeSlotMtpAcceptedTokens);
        }

        double promptDelta = 0;
        double generationDelta = 0;
        double? mtpGeneratedDelta = null;
        double? mtpAcceptedDelta = null;
        foreach (var counter in counters)
        {
            var hadPrevious = state.LastSlotCounters.TryGetValue(counter.SlotId, out var previous);
            promptDelta += hadPrevious
                ? SlotCounterDelta(counter.PromptTokensProcessed, previous!.PromptTokensProcessed, counter.TaskId, previous.TaskId)
                : Math.Max(0, counter.PromptTokensProcessed);
            generationDelta += hadPrevious
                ? SlotCounterDelta(counter.GeneratedTokens, previous!.GeneratedTokens, counter.TaskId, previous.TaskId)
                : Math.Max(0, counter.GeneratedTokens);
            mtpGeneratedDelta = RuntimeDashboardService.SumNullable(
                mtpGeneratedDelta,
                OptionalSlotCounterDelta(counter.MtpGeneratedTokens, hadPrevious ? previous!.MtpGeneratedTokens : null, counter.TaskId, hadPrevious ? previous!.TaskId : null));
            mtpAcceptedDelta = RuntimeDashboardService.SumNullable(
                mtpAcceptedDelta,
                OptionalSlotCounterDelta(counter.MtpAcceptedTokens, hadPrevious ? previous!.MtpAcceptedTokens : null, counter.TaskId, hadPrevious ? previous!.TaskId : null));
        }

        state.CumulativeSlotPromptTokens += promptDelta;
        state.CumulativeSlotGeneratedTokens += generationDelta;
        state.CumulativeSlotMtpGeneratedTokens = RuntimeDashboardService.SumNullable(state.CumulativeSlotMtpGeneratedTokens, mtpGeneratedDelta);
        state.CumulativeSlotMtpAcceptedTokens = RuntimeDashboardService.SumNullable(state.CumulativeSlotMtpAcceptedTokens, mtpAcceptedDelta);
        var elapsed = state.LastSlotPollAt is { } previousPollAt ? (now - previousPollAt).TotalSeconds : 0;
        RememberSlotCounters(state, counters, now);
        return new SlotAggregateObservation(
            elapsed >= 0.25 ? promptDelta / elapsed : null,
            elapsed >= 0.25 ? generationDelta / elapsed : null,
            state.CumulativeSlotPromptTokens,
            state.CumulativeSlotGeneratedTokens,
            state.CumulativeSlotMtpGeneratedTokens,
            state.CumulativeSlotMtpAcceptedTokens);
    }

    private static IReadOnlyList<RuntimeSlotCounterSnapshot> SlotCounters(RuntimeSlotSnapshot snapshot)
        => snapshot.SlotCounters is { Count: > 0 } counters
            ? counters
            : [new RuntimeSlotCounterSnapshot("aggregate", "", snapshot.PromptTokensProcessed, snapshot.GeneratedTokens, snapshot.IsProcessing)];

    private static double SlotCounterDelta(double current, double previous, string currentTaskId, string previousTaskId)
    {
        if (current >= previous && string.Equals(currentTaskId, previousTaskId, StringComparison.Ordinal))
            return current - previous;

        return Math.Max(0, current);
    }

    private static double? OptionalSlotCounterDelta(double? current, double? previous, string currentTaskId, string? previousTaskId)
    {
        if (current is null) return null;
        if (previous is not null && current >= previous && string.Equals(currentTaskId, previousTaskId, StringComparison.Ordinal))
            return current - previous;
        return Math.Max(0, current.Value);
    }

    private static double? SumOptional(IEnumerable<double?> values)
    {
        double? total = null;
        foreach (var value in values)
            total = RuntimeDashboardService.SumNullable(total, value);
        return total;
    }

    private static void RememberSlotCounters(
        RuntimeMetricSummaryState state,
        IReadOnlyList<RuntimeSlotCounterSnapshot> counters,
        DateTimeOffset capturedAt)
    {
        foreach (var counter in counters)
        {
            state.LastSlotCounters[counter.SlotId] = new RuntimeSlotCounterState(
                counter.TaskId,
                counter.PromptTokensProcessed,
                counter.GeneratedTokens,
                counter.MtpGeneratedTokens,
                counter.MtpAcceptedTokens);
        }
        state.LastSlotPollAt = capturedAt;
    }


}
