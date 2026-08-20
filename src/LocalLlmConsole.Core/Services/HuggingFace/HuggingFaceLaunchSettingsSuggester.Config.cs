namespace LocalLlmConsole.Services;

public static partial class HuggingFaceLaunchSettingsSuggester
{
    private static void ApplyGenerationConfig(string json, ref ModelLaunchSettings settings, ref bool changed)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return;
            if (TryDouble(node, out var temperature, "temperature"))
                Set(ref settings, ref changed, settings with { Temperature = temperature });
            if (TryInt(node, out var topK, "top_k", "topK"))
                Set(ref settings, ref changed, settings with { TopK = topK });
            if (TryDouble(node, out var topP, "top_p", "topP"))
                Set(ref settings, ref changed, settings with { TopP = topP });
            if (TryDouble(node, out var minP, "min_p", "minP"))
                Set(ref settings, ref changed, settings with { MinP = minP });
            if (TryDouble(node, out var repetitionPenalty, "repetition_penalty", "repeat_penalty"))
                Set(ref settings, ref changed, settings with { RepeatPenalty = repetitionPenalty });
            if (TryInt(node, out var maxNewTokens, "max_new_tokens", "max_tokens"))
                Set(ref settings, ref changed, settings with { MaxTokens = maxNewTokens });
        }
        catch
        {
        }
    }

    private static void ApplyModelConfig(string json, ref ModelLaunchSettings settings, ref bool changed)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return;
            if (TryInt(node, out var context, "max_position_embeddings", "seq_length")
                && context >= 512
                && context <= 262_144
                && settings.ContextSize == AppSettings.DefaultContextSize)
                Set(ref settings, ref changed, settings with { ContextSize = context });
        }
        catch
        {
        }
    }
}
