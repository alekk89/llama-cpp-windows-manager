namespace LocalLlmConsole.Services;

/// <summary>Prompt tokens observed between two polls that saw new prompt activity.</summary>
public sealed record RuntimePromptCacheWindow(double CachedTokens, double PromptTokens)
{
    public double HitPercent => PromptTokens > 0 ? Math.Clamp(100 * CachedTokens / PromptTokens, 0, 100) : 0;
}

public sealed partial class RuntimeMetricSummaryTracker
{
    /// <summary>
    /// Tokens produced per target-model verification step:
    /// (accepted draft tokens + steps) / steps. Each step yields one target token
    /// plus its accepted draft tokens, so plain decoding scores 1. Only counters
    /// from the same metrics endpoint are combined; without a step counter the
    /// value is unavailable instead of being estimated from the draft length.
    /// </summary>
    internal static double? SpeculativeTokensPerStep(IReadOnlyList<PrometheusSample> samples)
    {
        var steps = RuntimeDashboardService.SpeculativeVerificationStepCounter(samples);
        var accepted = RuntimeDashboardService.MtpAcceptedTokenCounter(samples);
        if (steps is not > 0 || accepted is not { } acceptedTokens || acceptedTokens < 0 || !double.IsFinite(acceptedTokens))
            return null;
        return (acceptedTokens + steps.Value) / steps.Value;
    }

    // The recent window keeps the latest poll interval with prompt activity, so
    // an idle server continues to show the hit rate of its last prompt batch.
    private static RuntimePromptCacheWindow? ObservePromptCacheWindow(
        RuntimeMetricSummaryState state,
        double? processed,
        double? cached)
    {
        if (processed is not { } processedTokens || cached is not { } cachedTokens)
            return state.LastPromptCacheWindow;
        var previousProcessed = state.LastCacheWindowProcessedCounter;
        var previousCached = state.LastCacheWindowCachedCounter;
        state.LastCacheWindowProcessedCounter = processedTokens;
        state.LastCacheWindowCachedCounter = cachedTokens;
        if (previousProcessed is not { } lastProcessed || previousCached is not { } lastCached)
            return state.LastPromptCacheWindow;
        if (processedTokens < lastProcessed || cachedTokens < lastCached)
            return state.LastPromptCacheWindow = null;

        var cachedDelta = cachedTokens - lastCached;
        var promptDelta = processedTokens - lastProcessed + cachedDelta;
        if (promptDelta > 0)
            state.LastPromptCacheWindow = new RuntimePromptCacheWindow(cachedDelta, promptDelta);
        return state.LastPromptCacheWindow;
    }
}
