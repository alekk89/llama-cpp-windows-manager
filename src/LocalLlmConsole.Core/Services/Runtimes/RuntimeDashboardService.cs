
namespace LocalLlmConsole.Services;

public static class RuntimeDashboardService
{
    public static RuntimeSlotSnapshot? ParseSlotSnapshot(string raw)
        => RuntimeSlotSnapshotParser.Parse(raw);

    public static RuntimeMtpTokenSnapshot? ParseMtpTokenStats(string raw)
        => RuntimeMtpLogParser.Parse(raw);

    public static string MtpTokenSummaryLabel(
        double? liveGeneratedRate,
        double? averageGeneratedRate,
        double? liveAcceptedRate,
        double? averageAcceptedRate,
        double? generatedTotal,
        double? acceptedTotal)
        => $"{TokenActivityLine("Gen", liveGeneratedRate, averageGeneratedRate, generatedTotal)}\n{TokenActivityLine("Accepted", liveAcceptedRate, averageAcceptedRate, acceptedTotal)}";

    public static string TokenActivitySummaryLabel(
        double? liveGeneratedRate,
        double? averageGeneratedRate,
        double? livePromptRate,
        double? averagePromptRate,
        double? generatedTotal,
        double? promptTotal)
        => $"{TokenActivityLine("Gen", liveGeneratedRate, averageGeneratedRate, generatedTotal)}\n{TokenActivityLine("Prompt", livePromptRate, averagePromptRate, promptTotal)}";

    public static string TokenAverageAndTotalSummaryLabel(
        double? averageGeneratedRate,
        double? averagePromptRate,
        double? generatedTotal,
        double? promptTotal,
        double? cachedPromptTotal = null)
        => $"Generated: {TokenRateLabel(averageGeneratedRate)} | Total generated: {TokenCountLabel(generatedTotal)}\n"
           + $"Prompt: {TokenRateLabel(averagePromptRate)} | Total prompt: {TokenCountLabel(promptTotal)} | Cache hit: {TokenCountLabel(cachedPromptTotal)}";

    public static double? CounterRate(double? current, double? previous, DateTimeOffset now, DateTimeOffset? previousPollAt, double minElapsedSeconds)
    {
        if (current is null || previous is null || previousPollAt is null || current < previous) return null;
        var elapsed = (now - previousPollAt.Value).TotalSeconds;
        return elapsed < minElapsedSeconds ? null : (current.Value - previous.Value) / elapsed;
    }

    public static double? DeltaRate(double current, double? previous, double elapsedSeconds, bool includeZero)
    {
        if (previous is null || current < previous.Value || elapsedSeconds <= 0) return null;
        var delta = current - previous.Value;
        if (delta <= 0 && !includeZero) return null;
        return delta / elapsedSeconds;
    }

    public static double? SumNullable(double? current, double? next)
        => next is null ? current : (current ?? 0) + next.Value;

    public static double? MaxNullable(double? current, double? next)
    {
        if (current is null) return next;
        if (next is null) return current;
        return Math.Max(current.Value, next.Value);
    }

    public static long WholePositiveDelta(double? current, double? previous)
    {
        if (current is null || previous is null || current.Value < previous.Value) return 0;
        return Math.Max(0, (long)Math.Floor(current.Value - previous.Value));
    }

    public static long WholePositiveDeltaAndRemember(double? current, ref double? previous)
    {
        var delta = WholePositiveDelta(current, previous);
        if (current is not null) previous = current;
        return delta;
    }

    public static double PositiveAmountDeltaAndRemember(double? current, ref double? previous)
    {
        var delta = current is not null && previous is not null && current.Value >= previous.Value
            ? Math.Max(0, current.Value - previous.Value)
            : 0;
        if (current is not null) previous = current;
        return delta;
    }

    public static bool PositiveDelta(double? current, double? previous)
        => current is not null && previous is not null && current.Value > previous.Value;

    public static double? Rate(double? amount, double? seconds)
        => amount is not null && seconds is > 0 ? amount.Value / seconds.Value : null;

    public static string TokenSummaryLabel(double? generated, double? prompt)
    {
        return $"Gen {TokenCountLabel(generated)}\nPrompt {TokenCountLabel(prompt)}";
    }

    public static string RateLabel(double? live, double? average)
    {
        if (live is null && average is null) return "Unknown";
        if (live is not null && average is not null) return $"{FormatTokenRate(live.Value)} t/s ({FormatTokenRate(average.Value)} avg)";
        return live is not null ? $"{FormatTokenRate(live.Value)} t/s" : $"{FormatTokenRate(average!.Value)} avg";
    }

    public static string RuntimeSettingsLabel(
        double? kvUsage,
        double? kvTokens,
        double? contextSize,
        int launchContextSize,
        int parallelSlots = 1,
        string kvUnified = "auto")
    {
        var lines = new List<string>
        {
            $"Context {ContextSizeLabel(contextSize, launchContextSize, parallelSlots, kvUnified)}"
        };
        if (parallelSlots > 1)
            lines.Add($"Slots: {parallelSlots:N0} enabled");
        lines.Add($"KV cache {KvCacheLabel(kvUsage, kvTokens)}");
        return string.Join("\n", lines);
    }

    public static string RuntimeKvCacheLabel(
        double? reportedUsage,
        double? tokens,
        double? capacityTokens,
        string kvUnified = "auto")
    {
        var usagePercent = KvCacheUsagePercent(reportedUsage, tokens, capacityTokens);
        var usedParts = new List<string>();
        if (tokens is not null) usedParts.Add($"{tokens.Value:N0} t");
        if (usagePercent is not null) usedParts.Add($"{usagePercent.Value:0.#}%");
        var used = usedParts.Count == 0 ? "Unknown" : string.Join(" | ", usedParts);
        var capacity = capacityTokens is > 0 ? $"{capacityTokens.Value:N0} t" : "Unknown";
        var allocation = kvUnified.ToLowerInvariant() switch
        {
            "on" => "unified",
            "off" => "partitioned",
            _ => "automatic"
        };
        return $"Used {used}\nCapacity {capacity} | {allocation}";
    }

    public static double? KvCacheUsagePercent(double? reportedUsage, double? tokens, double? capacityTokens)
    {
        if (reportedUsage is { } usage && double.IsFinite(usage))
            return Math.Clamp(usage <= 1 ? usage * 100 : usage, 0, 100);
        if (tokens is { } used && capacityTokens is > 0 && double.IsFinite(used))
            return Math.Clamp(100 * used / capacityTokens.Value, 0, 100);
        return null;
    }

    public static string RuntimeSlotsLabel(
        IReadOnlyList<PrometheusSample> samples,
        RuntimeSlotSnapshot? slotSnapshot = null,
        int configuredSlots = 1)
    {
        var active = RuntimeMetrics.First(samples, ["requests", "processing"], [])
            ?? SlotProcessingCount(slotSnapshot)
            ?? 0;
        var queued = RuntimeMetrics.First(samples, ["requests", "deferred"], []) ?? 0;
        var busy = RuntimeMetrics.First(samples, ["busy", "slots", "decode"], [])
            ?? RuntimeMetrics.First(samples, ["n", "busy", "slots", "per", "decode"], [])
            ?? SlotProcessingCount(slotSnapshot)
            ?? 0;
        var capacity = Math.Max(
            Math.Max(configuredSlots, slotSnapshot?.SlotCounters?.Count ?? 0),
            (int)Math.Ceiling(Math.Max(0, active)));
        return $"Active {active:N0}/{capacity:N0} | Queued {queued:N0}\nBusy/decode {busy:0.0}";
    }

    public static double? GeneratedTokenCounter(IReadOnlyList<PrometheusSample> samples)
        => RuntimeMetrics.Sum(samples, ["tokens", "predicted", "total"], ["seconds", "duration"])
           ?? RuntimeMetrics.Sum(samples, ["tokens", "generated", "total"], ["seconds", "duration"])
           ?? RuntimeMetrics.Sum(samples, ["tokens", "decoded", "total"], ["seconds", "duration"])
           ?? RuntimeMetrics.Sum(samples, ["tokens", "eval", "total"], ["seconds", "duration"])
           ?? RuntimeMetrics.Sum(samples, ["tokens", "predicted"], ["seconds", "duration", "per"])
           ?? RuntimeMetrics.Sum(samples, ["tokens", "generated"], ["seconds", "duration", "per"])
           ?? RuntimeMetrics.Sum(samples, ["tokens", "decoded"], ["seconds", "duration", "per"]);

    public static double? PromptTokensProcessedCounter(IReadOnlyList<PrometheusSample> samples)
        => RuntimeMetrics.Sum(samples, ["prompt", "tokens", "total"], ["seconds", "duration", "cached", "cache"])
           ?? RuntimeMetrics.Sum(samples, ["prompt", "tokens"], ["seconds", "duration", "per", "cached", "cache"]);

    public static double? PromptCachedTokenCounter(IReadOnlyList<PrometheusSample> samples)
        => RuntimeMetrics.Sum(samples, ["prompt", "tokens", "cached", "total"], ["seconds", "duration"])
           ?? RuntimeMetrics.Sum(samples, ["prompt", "tokens", "cache", "total"], ["seconds", "duration"]);

    public static double? GeneratedSecondsCounter(IReadOnlyList<PrometheusSample> samples)
        => RuntimeMetrics.Sum(samples, ["tokens", "predicted", "seconds", "total"], [])
           ?? RuntimeMetrics.Sum(samples, ["tokens", "generated", "seconds", "total"], [])
           ?? RuntimeMetrics.Sum(samples, ["eval", "time"], ["prompt"]);

    public static double? PromptSecondsCounter(IReadOnlyList<PrometheusSample> samples)
        => RuntimeMetrics.Sum(samples, ["prompt", "seconds", "total"], [])
           ?? RuntimeMetrics.Sum(samples, ["prompt", "time"], []);

    public static double? CompletedRequestCounter(IReadOnlyList<PrometheusSample> samples)
        => RuntimeMetrics.Sum(samples, ["requests", "completed", "total"], [])
           ?? RuntimeMetrics.Sum(samples, ["requests", "total"], ["processing", "deferred", "queued", "failed", "error"]);

    public static double? FailedRequestCounter(IReadOnlyList<PrometheusSample> samples)
        => RuntimeMetrics.Sum(samples, ["requests", "failed", "total"], [])
           ?? RuntimeMetrics.Sum(samples, ["requests", "error", "total"], []);

    public static double? PromptActivityTokenCounter(IReadOnlyList<PrometheusSample> samples)
        => SumNullable(PromptTokensProcessedCounter(samples), PromptCachedTokenCounter(samples));

    public static double? MtpGeneratedTokenCounter(IReadOnlyList<PrometheusSample> samples)
        => RuntimeMetrics.Sum(samples, ["mtp", "tokens", "generated", "total"], ["seconds", "duration", "accepted", "acc", "rejected"])
           ?? RuntimeMetrics.Sum(samples, ["draft", "tokens", "generated", "total"], ["seconds", "duration", "accepted", "acc", "rejected"])
           ?? RuntimeMetrics.Sum(samples, ["speculative", "tokens", "generated", "total"], ["seconds", "duration", "accepted", "acc", "rejected"])
           ?? RuntimeMetrics.Sum(samples, ["spec", "tokens", "generated", "total"], ["seconds", "duration", "accepted", "acc", "rejected"])
           ?? RuntimeMetrics.Sum(samples, ["mtp", "tokens", "total"], ["seconds", "duration", "accepted", "acc", "rejected"])
           ?? RuntimeMetrics.Sum(samples, ["draft", "tokens", "total"], ["seconds", "duration", "accepted", "acc", "rejected"])
           ?? RuntimeMetrics.Sum(samples, ["speculative", "tokens", "total"], ["seconds", "duration", "accepted", "acc", "rejected"]);

    public static double? MtpAcceptedTokenCounter(IReadOnlyList<PrometheusSample> samples)
        => RuntimeMetrics.Sum(samples, ["mtp", "tokens", "accepted", "total"], ["seconds", "duration"])
           ?? RuntimeMetrics.Sum(samples, ["draft", "tokens", "accepted", "total"], ["seconds", "duration"])
           ?? RuntimeMetrics.Sum(samples, ["speculative", "tokens", "accepted", "total"], ["seconds", "duration"])
           ?? RuntimeMetrics.Sum(samples, ["spec", "tokens", "accepted", "total"], ["seconds", "duration"])
           ?? RuntimeMetrics.Sum(samples, ["mtp", "acc", "tokens", "total"], ["seconds", "duration"])
           ?? RuntimeMetrics.Sum(samples, ["draft", "acc", "tokens", "total"], ["seconds", "duration"])
           ?? RuntimeMetrics.Sum(samples, ["speculative", "acc", "tokens", "total"], ["seconds", "duration"]);

    public static double? MtpGeneratedSecondsCounter(IReadOnlyList<PrometheusSample> samples)
        => RuntimeMetrics.Sum(samples, ["mtp", "tokens", "generated", "seconds", "total"], ["accepted", "acc", "rejected"])
           ?? RuntimeMetrics.Sum(samples, ["draft", "tokens", "generated", "seconds", "total"], ["accepted", "acc", "rejected"])
           ?? RuntimeMetrics.Sum(samples, ["speculative", "tokens", "generated", "seconds", "total"], ["accepted", "acc", "rejected"])
           ?? RuntimeMetrics.Sum(samples, ["spec", "tokens", "generated", "seconds", "total"], ["accepted", "acc", "rejected"])
           ?? RuntimeMetrics.Sum(samples, ["mtp", "seconds", "total"], ["accepted", "acc", "rejected", "prompt"])
           ?? RuntimeMetrics.Sum(samples, ["draft", "seconds", "total"], ["accepted", "acc", "rejected", "prompt"])
           ?? RuntimeMetrics.Sum(samples, ["speculative", "seconds", "total"], ["accepted", "acc", "rejected", "prompt"]);

    public static double? MtpAcceptedSecondsCounter(IReadOnlyList<PrometheusSample> samples)
        => RuntimeMetrics.Sum(samples, ["mtp", "tokens", "accepted", "seconds", "total"], [])
           ?? RuntimeMetrics.Sum(samples, ["draft", "tokens", "accepted", "seconds", "total"], [])
           ?? RuntimeMetrics.Sum(samples, ["speculative", "tokens", "accepted", "seconds", "total"], [])
           ?? RuntimeMetrics.Sum(samples, ["spec", "tokens", "accepted", "seconds", "total"], [])
           ?? RuntimeMetrics.Sum(samples, ["mtp", "acc", "seconds", "total"], [])
           ?? RuntimeMetrics.Sum(samples, ["draft", "acc", "seconds", "total"], [])
           ?? RuntimeMetrics.Sum(samples, ["speculative", "acc", "seconds", "total"], []);

    public static string ContextSizeLabel(
        double? contextSize,
        int launchContextSize,
        int parallelSlots = 1,
        string kvUnified = "auto")
    {
        var slots = Math.Max(1, parallelSlots);
        var baseContext = slots > 1
            ? launchContextSize > 0
                ? (double?)launchContextSize
                : contextSize is > 0
                    ? contextSize
                    : null
            : contextSize is > 0
                ? contextSize
                : launchContextSize > 0
                    ? launchContextSize
                    : null;

        if (baseContext is not null)
        {
            var totalContext = string.Equals(kvUnified, "off", StringComparison.OrdinalIgnoreCase)
                ? baseContext.Value * slots
                : baseContext.Value;
            return $"{totalContext:N0} total";
        }

        return slots > 1 && string.Equals(kvUnified, "off", StringComparison.OrdinalIgnoreCase)
            ? $"Model default x {slots:N0} slots"
            : "Model default";
    }

    public static double? ReadDouble(JsonObject obj, params string[] keys)
        => RuntimeSlotSnapshotParser.ReadDouble(obj, keys);

    public static bool ReadBool(JsonObject obj, params string[] keys)
        => RuntimeSlotSnapshotParser.ReadBool(obj, keys);

    private static double? SlotProcessingCount(RuntimeSlotSnapshot? snapshot)
    {
        if (snapshot?.SlotCounters is { Count: > 0 } counters)
            return counters.Count(counter => counter.IsProcessing);
        return snapshot?.IsProcessing == true ? 1 : null;
    }

    private static string KvCacheLabel(double? usage, double? tokens)
    {
        var parts = new List<string>();
        if (usage is not null)
        {
            var percent = usage <= 1 ? usage.Value * 100 : usage.Value;
            parts.Add($"{percent:0.#}%");
        }
        if (tokens is not null) parts.Add($"{tokens.Value:N0} tokens");
        return parts.Count == 0 ? "Unknown" : string.Join(", ", parts);
    }

    private static string TokenCountLabel(double? value)
        => value is null ? "?" : value.Value.ToString("N0");

    private static string TokenActivityLine(string kind, double? liveRate, double? averageRate, double? totalTokens)
    {
        var parts = new List<string> { $"{TokenRateLabel(liveRate)} ({kind})" };
        if (averageRate is > 0) parts.Add($"{TokenRateLabel(averageRate)} (Avg)");
        if (totalTokens is not null) parts.Add($"{TokenCountLabel(totalTokens)} t (Total)");
        return string.Join(" | ", parts);
    }

    private static string TokenRateLabel(double? value)
        => value is null ? "Unknown" : $"{FormatTokenRate(value.Value)} t/s";

    private static string FormatTokenRate(double value)
        => value.ToString("0.0", CultureInfo.InvariantCulture);
}
