
namespace LocalLlmConsole.Services;

public sealed record RuntimeMtpTokenSnapshot(
    double? GeneratedTokens,
    double? AcceptedTokens,
    double? GeneratedSeconds = null,
    double? AcceptedSeconds = null);

public sealed record RuntimeSlotCounterSnapshot(
    string SlotId,
    string TaskId,
    double PromptTokensProcessed,
    double GeneratedTokens,
    bool IsProcessing,
    double? MtpGeneratedTokens = null,
    double? MtpAcceptedTokens = null);

public sealed record RuntimeSlotSnapshot(
    double PromptTokensProcessed,
    double GeneratedTokens,
    bool IsProcessing,
    double? PromptTokens,
    double? ContextTokens,
    double? ContextSize,
    double? MtpGeneratedTokens = null,
    double? MtpAcceptedTokens = null,
    IReadOnlyList<RuntimeSlotCounterSnapshot>? SlotCounters = null,
    double? ContextCapacityTokens = null);

public static class RuntimeDashboardService
{
    private static readonly Regex MtpStatisticsPattern = new(
        @"statistics\s+(?:draft-mtp|mtp)\s*:.*?#gen tokens\s*=\s*(?<generated>[\d,]+).*?#acc tokens\s*=\s*(?<accepted>[\d,]+)(?:.*?dur\(b,g,a\)\s*=\s*(?<batchMs>[-+0-9.,eE]+)\s*,\s*(?<generatedMs>[-+0-9.,eE]+)\s*,\s*(?<acceptedMs>[-+0-9.,eE]+)\s*ms)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DraftAcceptancePattern = new(
        @"draft acceptance(?:\s+rate)?\s*=\s*[-+0-9.eE]+\s*\(\s*(?<accepted>[\d,]+)\s+accepted\s*/\s*(?<generated>[\d,]+)\s+generated\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static RuntimeSlotSnapshot? ParseSlotSnapshot(string raw)
    {
        var node = JsonNode.Parse(raw);
        if (node is not JsonArray slots) return null;

        double promptProcessed = 0;
        double generated = 0;
        double? promptTokens = null;
        double? contextTokens = null;
        double? contextSize = null;
        double? contextCapacityTokens = null;
        double? mtpGeneratedTokens = null;
        double? mtpAcceptedTokens = null;
        var processing = false;
        var slotCounters = new List<RuntimeSlotCounterSnapshot>();
        var slotIndex = 0;

        foreach (var slotNode in slots.OfType<JsonObject>())
        {
            var slotId = SlotId(slotNode, slotIndex);
            var taskId = SlotTaskId(slotNode);
            var slotProcessing = ReadBool(slotNode, "is_processing", "processing", "busy");
            processing |= slotProcessing;

            var slotPromptProcessed = ReadDouble(slotNode, "n_prompt_tokens_processed", "prompt_tokens_processed", "n_prompt_tokens_processed_total") ?? 0;
            var slotPromptTokens = ReadDouble(slotNode, "n_prompt_tokens", "prompt_tokens");
            var slotPromptCacheTokens = ReadDouble(slotNode, "n_prompt_tokens_cache", "prompt_tokens_cache", "n_cached_tokens", "cached_tokens");
            var slotGenerated = ReadDouble(slotNode, "n_decoded", "tokens_predicted", "n_tokens_predicted", "n_tokens_predicted_total");
            if (slotGenerated is null && slotNode["next_token"] is JsonArray nextTokens)
            {
                slotGenerated = nextTokens.OfType<JsonObject>()
                    .Select(next => ReadDouble(next, "n_decoded", "tokens_predicted", "n_tokens_predicted"))
                    .Where(value => value is not null)
                    .Sum(value => value!.Value);
                processing |= nextTokens.OfType<JsonObject>().Any(next => ReadBool(next, "has_next_token"));
                slotProcessing |= nextTokens.OfType<JsonObject>().Any(next => ReadBool(next, "has_next_token"));
            }
            else if (slotGenerated is null && slotNode["next_token"] is JsonObject nextToken)
            {
                // Newer llama.cpp format: next_token is a single object, not an array
                slotGenerated = ReadDouble(nextToken, "n_decoded", "tokens_predicted", "n_tokens_predicted", "n_tokens_decoded", "decoded");
                processing |= ReadBool(nextToken, "has_next_token");
                slotProcessing |= ReadBool(nextToken, "has_next_token");
            }

            promptProcessed += slotPromptProcessed;
            generated += slotGenerated ?? 0;

            promptTokens = SumNullable(promptTokens, slotPromptTokens);
            var slotContextTokens = SlotContextTokens(
                slotPromptProcessed,
                slotGenerated ?? 0,
                slotPromptTokens,
                slotPromptCacheTokens);
            contextTokens = SumNullable(contextTokens, slotContextTokens > 0 ? slotContextTokens : null);
            var slotContextSize = ReadDouble(slotNode, "n_ctx", "context_size", "ctx_size");
            var slotMtpGeneratedTokens = ReadMtpGeneratedTokens(slotNode);
            var slotMtpAcceptedTokens = ReadMtpAcceptedTokens(slotNode);
            contextSize = MaxNullable(contextSize, slotContextSize);
            contextCapacityTokens = SumNullable(contextCapacityTokens, slotContextSize);
            mtpGeneratedTokens = SumNullable(mtpGeneratedTokens, slotMtpGeneratedTokens);
            mtpAcceptedTokens = SumNullable(mtpAcceptedTokens, slotMtpAcceptedTokens);
            slotCounters.Add(new RuntimeSlotCounterSnapshot(
                slotId,
                taskId,
                slotPromptProcessed,
                slotGenerated ?? 0,
                slotProcessing,
                slotMtpGeneratedTokens,
                slotMtpAcceptedTokens));
            slotIndex++;
        }

        return new RuntimeSlotSnapshot(
            promptProcessed,
            generated,
            processing,
            promptTokens,
            contextTokens,
            contextSize,
            mtpGeneratedTokens,
            mtpAcceptedTokens,
            slotCounters,
            contextCapacityTokens);
    }

    public static RuntimeMtpTokenSnapshot? ParseMtpTokenStats(string raw)
        => SnapshotFromMatches(MtpStatisticsPattern.Matches(raw))
           ?? SnapshotFromMatches(DraftAcceptancePattern.Matches(raw));

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
    {
        foreach (var key in keys)
        {
            if (obj[key] is null) continue;
            if (obj[key] is JsonValue value && value.TryGetValue<double>(out var number)) return number;
            if (double.TryParse(obj[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        return null;
    }

    public static bool ReadBool(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj[key] is null) continue;
            if (obj[key] is JsonValue value && value.TryGetValue<bool>(out var boolean)) return boolean;
            if (bool.TryParse(obj[key]?.ToString(), out var parsed)) return parsed;
        }
        return false;
    }

    private static string SlotId(JsonObject obj, int index)
        => FirstJsonText(obj, "id", "slot_id", "slot") ?? index.ToString(CultureInfo.InvariantCulture);

    private static string SlotTaskId(JsonObject obj)
        => FirstJsonText(obj, "id_task", "task_id", "task") ?? "";

    private static string? FirstJsonText(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj[key] is null) continue;
            var value = obj[key]?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static double SlotContextTokens(
        double promptTokensProcessed,
        double generatedTokens,
        double? promptTokens,
        double? promptCacheTokens)
    {
        var promptSide = promptTokensProcessed;
        if (promptTokens is not null) promptSide = Math.Max(promptSide, promptTokens.Value);
        if (promptCacheTokens is not null) promptSide = Math.Max(promptSide, promptCacheTokens.Value);
        return promptSide + generatedTokens;
    }

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

    private static double? ReadMtpGeneratedTokens(JsonObject obj)
        => ReadDouble(
            obj,
            "mtp_tokens_generated",
            "n_mtp_tokens_generated",
            "draft_tokens_generated",
            "n_draft_tokens_generated",
            "speculative_tokens_generated",
            "n_speculative_tokens_generated",
            "spec_tokens_generated",
            "n_spec_tokens_generated",
            "n_draft_tokens",
            "draft_tokens",
            "n_speculative_tokens",
            "speculative_tokens");

    private static double? ReadMtpAcceptedTokens(JsonObject obj)
        => ReadDouble(
            obj,
            "mtp_tokens_accepted",
            "n_mtp_tokens_accepted",
            "accepted_mtp_tokens",
            "n_accepted_mtp_tokens",
            "draft_tokens_accepted",
            "n_draft_tokens_accepted",
            "speculative_tokens_accepted",
            "n_speculative_tokens_accepted",
            "spec_tokens_accepted",
            "n_spec_tokens_accepted",
            "accepted_tokens",
            "n_accepted_tokens",
            "acc_tokens",
            "n_acc_tokens");

    private static RuntimeMtpTokenSnapshot? SnapshotFromMatches(MatchCollection matches)
    {
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var generated = ParseCounter(match.Groups["generated"].Value);
            var accepted = ParseCounter(match.Groups["accepted"].Value);
            var generatedSeconds = ParseMilliseconds(match.Groups["generatedMs"].Value);
            var acceptedSeconds = generatedSeconds;
            if (generated is not null || accepted is not null)
                return new RuntimeMtpTokenSnapshot(generated, accepted, generatedSeconds, acceptedSeconds);
        }

        return null;
    }

    private static double? ParseCounter(string raw)
    {
        var normalized = raw.Replace(",", "", StringComparison.Ordinal).Trim();
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double? ParseMilliseconds(string raw)
    {
        var value = ParseCounter(raw);
        return value is > 0 ? value.Value / 1000 : null;
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
