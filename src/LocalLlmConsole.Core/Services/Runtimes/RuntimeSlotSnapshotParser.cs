namespace LocalLlmConsole.Services;

public static class RuntimeSlotSnapshotParser
{
    public static RuntimeSlotSnapshot? Parse(string raw)
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
            var slotId = FirstJsonText(slotNode, "id", "slot_id", "slot") ?? slotIndex.ToString(CultureInfo.InvariantCulture);
            var taskId = FirstJsonText(slotNode, "id_task", "task_id", "task") ?? "";
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
                slotGenerated = ReadDouble(nextToken, "n_decoded", "tokens_predicted", "n_tokens_predicted", "n_tokens_decoded", "decoded");
                processing |= ReadBool(nextToken, "has_next_token");
                slotProcessing |= ReadBool(nextToken, "has_next_token");
            }

            promptProcessed += slotPromptProcessed;
            generated += slotGenerated ?? 0;
            promptTokens = SumNullable(promptTokens, slotPromptTokens);
            var slotContextTokens = SlotContextTokens(slotPromptProcessed, slotGenerated ?? 0, slotPromptTokens, slotPromptCacheTokens);
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

    private static double SlotContextTokens(double processed, double generated, double? prompt, double? cached)
    {
        var promptSide = processed;
        if (prompt is not null) promptSide = Math.Max(promptSide, prompt.Value);
        if (cached is not null) promptSide = Math.Max(promptSide, cached.Value);
        return promptSide + generated;
    }

    private static double? SumNullable(double? current, double? next)
        => current is null ? next : next is null ? current : current.Value + next.Value;

    private static double? MaxNullable(double? current, double? next)
        => current is null ? next : next is null ? current : Math.Max(current.Value, next.Value);

    private static double? ReadMtpGeneratedTokens(JsonObject obj)
        => ReadDouble(obj, "mtp_tokens_generated", "n_mtp_tokens_generated", "draft_tokens_generated", "n_draft_tokens_generated",
            "speculative_tokens_generated", "n_speculative_tokens_generated", "spec_tokens_generated", "n_spec_tokens_generated",
            "n_draft_tokens", "draft_tokens", "n_speculative_tokens", "speculative_tokens");

    private static double? ReadMtpAcceptedTokens(JsonObject obj)
        => ReadDouble(obj, "mtp_tokens_accepted", "n_mtp_tokens_accepted", "accepted_mtp_tokens", "n_accepted_mtp_tokens",
            "draft_tokens_accepted", "n_draft_tokens_accepted", "speculative_tokens_accepted", "n_speculative_tokens_accepted",
            "spec_tokens_accepted", "n_spec_tokens_accepted", "accepted_tokens", "n_accepted_tokens", "acc_tokens", "n_acc_tokens");
}
