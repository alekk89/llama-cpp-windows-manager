namespace LocalLlmConsole;

public sealed partial class OverviewDashboardController
{
    private static void ApplyEfficiencyReadings(
        RuntimeMetricAtomicSnapshot atomic,
        Action<string, string, double?, string, string> setReading)
    {
        var perStep = Finite(atomic.SpeculativeTokensPerStep);
        setReading(OverviewDashboardMetricIds.SpeculativeSpeedup, Number(perStep, "0.00"), perStep, "×",
            perStep is null ? "" : Loc.T("Dashboard.Detail.SpeculativeSpeedupBound"));
        var hitRate = Finite(atomic.RecentPromptCacheHitPercent);
        setReading(OverviewDashboardMetricIds.PromptCacheHitRate, Number(hitRate, "0.#"), hitRate, "%",
            hitRate is not null && atomic.RecentPromptCachedTokens is { } cached && atomic.RecentPromptTokens is { } total
                ? Loc.T("Dashboard.Detail.PromptCacheHitTokens",
                    cached.ToString("N0", CultureInfo.CurrentCulture),
                    total.ToString("N0", CultureInfo.CurrentCulture))
                : "");
    }

    private static string CacheReuseDetail(RuntimeMetricAtomicSnapshot atomic)
    {
        var cachedTokens = atomic.PromptCachedTokens is { } cached ? $"{cached:N0} cached tokens" : "";
        if (Finite(atomic.RecentPromptCacheHitPercent) is not { } hitRate) return cachedTokens;
        var recent = Loc.T("Dashboard.Detail.PromptCacheHitRate", hitRate.ToString("0.#", CultureInfo.CurrentCulture));
        return cachedTokens.Length == 0 ? recent : $"{cachedTokens} · {recent}";
    }

    private static string SpeculativeSpeedupDetail(double? tokensPerStep)
        => Finite(tokensPerStep) is { } perStep
            ? Loc.T("Dashboard.Detail.SpeculativeSpeedup", perStep.ToString("0.00", CultureInfo.CurrentCulture))
            : "";

    private static double? Finite(double? value)
        => value is { } number && double.IsFinite(number) ? number : null;
}
