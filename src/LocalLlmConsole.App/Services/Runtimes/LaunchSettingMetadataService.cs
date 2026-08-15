
namespace LocalLlmConsole.Services;

public static class LaunchSettingMetadataService
{
    public const string AtomicMtpSpeculativeType = "atomic-mtp";

    public static readonly IReadOnlyList<string> AutoOnOffOptions = ["auto", "on", "off"];
    public static readonly IReadOnlyList<string> OnOffOptions = ["on", "off"];
    public static readonly IReadOnlyList<string> OffOnOptions = ["off", "on"];
    public static readonly IReadOnlyList<string> CacheTypeOptions = ["f16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1", "f32", "bf16"];
    public static readonly IReadOnlyList<string> SpeculativeTypeOptions = ["none", AtomicMtpSpeculativeType, "draft-mtp", "draft-simple", "draft-eagle3", "draft-dflash", "draft-dspark", "ngram-simple", "ngram-map-k", "ngram-map-k4v", "ngram-mod", "ngram-cache"];
    public static readonly IReadOnlyList<string> ReasoningFormatOptions = ["auto", "none", "deepseek", "deepseek-legacy"];
    public static readonly IReadOnlyList<string> ReasoningEffortOptions = ["default", "minimal", "low", "medium", "high", "xhigh", "max"];
    public static readonly IReadOnlyList<string> RopeScalingOptions = ["auto", "none", "linear", "yarn"];
    public static readonly IReadOnlyList<string> GpuModeOptions = ["auto", "single", "layer", "row", "tensor"];

    public static string NormalizeSpeculativeType(string value)
    {
        var normalized = (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');
        return normalized == "mtp" ? AtomicMtpSpeculativeType : normalized;
    }

    public static bool IsAtomicMtpSpeculativeType(string value)
        => NormalizeSpeculativeType(value).Equals(AtomicMtpSpeculativeType, StringComparison.OrdinalIgnoreCase);

    public static string LlamaSpeculativeTypeArgument(string value)
        => IsAtomicMtpSpeculativeType(value) ? "mtp" : NormalizeSpeculativeType(value);

    public static string NormalizeGpuMode(string value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "" => AppSettings.DefaultGpuMode,
            "none" => "single",
            _ => normalized
        };
    }

    public static string LlamaSplitModeArgument(string value)
        => NormalizeGpuMode(value) == "single" ? "none" : NormalizeGpuMode(value);

    public static IReadOnlyList<string> ValidateGpuSettings(string mode, string devices, string split)
    {
        var errors = new List<string>();
        var normalizedMode = NormalizeGpuMode(mode);
        if (!GpuModeOptions.Contains(normalizedMode, StringComparer.OrdinalIgnoreCase))
            errors.Add($"GPU mode must be one of: {string.Join(", ", GpuModeOptions)}.");

        var deviceItems = CsvItems(devices, "GPU devices", errors);
        var splitItems = CsvItems(split, "GPU split", errors);
        if (deviceItems.Count > 128)
            errors.Add("GPU devices cannot contain more than 128 entries.");
        if (splitItems.Count > 128)
            errors.Add("GPU split cannot contain more than 128 entries.");

        foreach (var device in deviceItems)
        {
            if (device.Any(char.IsWhiteSpace) || device.StartsWith('-') || device.Any(char.IsControl))
            {
                errors.Add($"GPU device '{device}' is invalid. Use llama.cpp device IDs such as CUDA0 or Vulkan0.");
                break;
            }
        }

        var hasPositiveSplit = false;
        foreach (var item in splitItems)
        {
            if (!double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out var proportion)
                || !double.IsFinite(proportion)
                || proportion < 0)
            {
                errors.Add($"GPU split value '{item}' must be a non-negative number.");
                continue;
            }
            hasPositiveSplit |= proportion > 0;
        }

        if (splitItems.Count > 0 && !hasPositiveSplit)
            errors.Add("GPU split must assign a positive proportion to at least one GPU.");
        if (deviceItems.Count > 0 && splitItems.Count > 0 && deviceItems.Count != splitItems.Count)
            errors.Add("GPU devices and GPU split must contain the same number of entries.");
        if (normalizedMode == "single" && deviceItems.Count > 1)
            errors.Add("Single GPU mode accepts at most one GPU device.");
        if (normalizedMode == "single" && splitItems.Count > 0)
            errors.Add("GPU split must be empty in single GPU mode.");

        return errors;
    }

    public static string NormalizeGpuCsv(string value)
        => string.Join(',', (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string RuntimeOptionLabel(string optionName)
    {
        var words = (optionName ?? "").Trim().TrimStart('-')
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "Runtime option";

        return string.Join(' ', words.Select(word => word.ToLowerInvariant() switch
        {
            "api" => "API",
            "cpu" => "CPU",
            "gpu" => "GPU",
            "http" => "HTTP",
            "https" => "HTTPS",
            "id" => "ID",
            "io" => "I/O",
            "ip" => "IP",
            "json" => "JSON",
            "kv" => "KV",
            "lora" => "LoRA",
            "mmproj" => "MMProj",
            "mtp" => "MTP",
            "numa" => "NUMA",
            "rpc" => "RPC",
            "rope" => "RoPE",
            "ssl" => "SSL",
            "tcp" => "TCP",
            "tls" => "TLS",
            "url" => "URL",
            "vram" => "VRAM",
            "wsl" => "WSL",
            _ when word.Length == 1 => word.ToUpperInvariant(),
            _ => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
        }));
    }

    private static IReadOnlyList<string> CsvItems(string value, string label, List<string> errors)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return [];
        var items = text.Split(',', StringSplitOptions.TrimEntries);
        if (items.Any(string.IsNullOrWhiteSpace))
            errors.Add($"{label} cannot contain empty entries.");
        return items.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
    }

    public static string Tooltip(string label) => label switch
    {
        "Context size" => Loc.T("Tooltip.Field.ContextSize"),
        "Parallel slots" => Loc.T("Tooltip.Field.ParallelSlots"),
        "Batch size" => Loc.T("Tooltip.Field.BatchSize"),
        "Micro batch" => Loc.T("Tooltip.Field.MicroBatch"),
        "Threads" => Loc.T("Tooltip.Field.Threads"),
        "GPU layers" => Loc.T("Tooltip.Field.GpuLayers"),
        "GPU mode" => Loc.T("Tooltip.Field.GpuMode"),
        "GPU devices" => Loc.T("Tooltip.Field.GpuDevices"),
        "GPU split" => Loc.T("Tooltip.Field.GpuSplit"),
        "Reasoning" => Loc.T("Tooltip.Field.Reasoning"),
        "Reason format" => Loc.T("Tooltip.Field.ReasonFormat"),
        "Reasoning effort" => Loc.T("Tooltip.Field.ReasoningEffort"),
        "Reason budget" => Loc.T("Tooltip.Field.ReasonBudget"),
        "Budget message" => Loc.T("Tooltip.Field.ReasonBudgetMessage"),
        "Preserve reasoning" => Loc.T("Tooltip.Field.ReasonPreserve"),
        "Jinja chat" => Loc.T("Tooltip.Field.JinjaChat"),
        "Vision" => Loc.T("Tooltip.Current.Vision"),
        "Vision head" => Loc.T("Tooltip.Current.VisionHead"),
        "Image min" => Loc.T("Tooltip.Field.ImageMin"),
        "Image max" => Loc.T("Tooltip.Field.ImageMax"),
        "Flash attention" => Loc.T("Tooltip.Field.FlashAttention"),
        "K cache" => Loc.T("Tooltip.Field.KCache"),
        "V cache" => Loc.T("Tooltip.Field.VCache"),
        "KV offload" => Loc.T("Tooltip.Field.KvOffload"),
        "Unified KV" => Loc.T("Tooltip.Field.UnifiedKv"),
        "Prompt cache" => Loc.T("Tooltip.Field.PromptCache"),
        "Prompt cache MB" => Loc.T("Tooltip.Field.PromptCacheMb"),
        "Checkpoints" => Loc.T("Tooltip.Field.Checkpoints"),
        "Checkpoint count" => Loc.T("Tooltip.Field.CheckpointCount"),
        "Checkpoint spacing" => Loc.T("Tooltip.Field.CheckpointSpacing"),
        "Continuous batch" => Loc.T("Tooltip.Field.ContinuousBatch"),
        "Memory map" => Loc.T("Tooltip.Field.MemoryMap"),
        "Memory lock" => Loc.T("Tooltip.Field.MemoryLock"),
        "Metrics" => Loc.T("Tooltip.Field.Metrics"),
        "Custom params" => Loc.T("Tooltip.Field.CustomParams"),
        "Temperature" => Loc.T("Tooltip.Field.Temperature"),
        "Top K" => Loc.T("Tooltip.Field.TopK"),
        "Top P" => Loc.T("Tooltip.Field.TopP"),
        "Min P" => Loc.T("Tooltip.Field.MinP"),
        "Max tokens" => Loc.T("Tooltip.Field.MaxTokens"),
        "Seed" => Loc.T("Tooltip.Field.Seed"),
        "Repeat window" => Loc.T("Tooltip.Field.RepeatWindow"),
        "Repeat pen" => Loc.T("Tooltip.Field.RepeatPen"),
        "Presence" => Loc.T("Tooltip.Field.Presence"),
        "Frequency" => Loc.T("Tooltip.Field.Frequency"),
        "RoPE scaling" => Loc.T("Tooltip.Field.RopeScaling"),
        "RoPE scale" => Loc.T("Tooltip.Field.RopeScale"),
        "RoPE base" => Loc.T("Tooltip.Field.RopeBase"),
        "RoPE freq" => Loc.T("Tooltip.Field.RopeFreq"),
        "Spec type" => Loc.T("Tooltip.Current.SpecType"),
        "Draft model" => Loc.T("Tooltip.Current.DraftModel"),
        "MTP head" => Loc.T("Tooltip.Current.MtpHead"),
        "Draft GPU" => Loc.T("Tooltip.Field.DraftGpu"),
        "Draft K cache" => Loc.T("Tooltip.Field.DraftKCache"),
        "Draft V cache" => Loc.T("Tooltip.Field.DraftVCache"),
        "Draft max" => Loc.T("Tooltip.Field.DraftMax"),
        "Draft min" => Loc.T("Tooltip.Field.DraftMin"),
        "Split prob" => Loc.T("Tooltip.Field.SplitProb"),
        "Min prob" => Loc.T("Tooltip.Field.MinProb"),
        _ => Loc.T("Tooltip.Field.Default")
    };

    public static string ContextSizeTooltip(string text)
    {
        var tooltip = Tooltip("Context size");
        if (!LaunchSettingParser.TryNormalizeContextSize(text, out var value) || value <= 0)
            return tooltip;

        var normalized = value.ToString(CultureInfo.InvariantCulture);
        var compactText = (text ?? "")
            .Replace(",", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal);
        return string.Equals(compactText, normalized, StringComparison.OrdinalIgnoreCase)
            ? tooltip
            : Loc.T("Tooltip.ContextSizeSuggestion", tooltip, value.ToString("N0", CultureInfo.InvariantCulture));
    }
}
