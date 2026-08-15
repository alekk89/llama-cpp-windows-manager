
namespace LocalLlmConsole.Services;

public static partial class HuggingFaceLaunchSettingsSuggester
{
    public static ModelLaunchSettings? TryCreate(AppSettings defaults, string modelCardMarkdown, string generationConfigJson = "", string configJson = "")
    {
        var settings = ModelLaunchSettings.FromAppSettings(defaults);
        var changed = false;

        if (!string.IsNullOrWhiteSpace(generationConfigJson))
            ApplyGenerationConfig(generationConfigJson, ref settings, ref changed);

        if (!string.IsNullOrWhiteSpace(configJson))
            ApplyModelConfig(configJson, ref settings, ref changed);

        var command = ExtractPreferredLlamaCommand(modelCardMarkdown);
        if (!string.IsNullOrWhiteSpace(command))
            ApplyLlamaCommand(command, ref settings, ref changed);

        return changed ? settings : null;
    }

    private static void ApplyLlamaCommand(string command, ref ModelLaunchSettings settings, ref bool changed)
    {
        var current = settings;
        var anyChanged = changed;
        var tokens = TokenizeShell(command).ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            var raw = tokens[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var option = raw;
            string? inlineValue = null;
            var equals = raw.IndexOf('=');
            if (equals > 0)
            {
                option = raw[..equals];
                inlineValue = raw[(equals + 1)..];
            }

            string? Value()
            {
                if (inlineValue is not null) return inlineValue;
                return i + 1 < tokens.Length ? tokens[++i] : null;
            }

            void SetInt(Func<int, ModelLaunchSettings> setter)
            {
                if (TryInt(Value(), out var value))
                    Set(ref current, ref anyChanged, setter(value));
            }

            void SetDouble(Func<double, ModelLaunchSettings> setter)
            {
                if (TryDouble(Value(), out var value))
                    Set(ref current, ref anyChanged, setter(value));
            }

            void SetString(Func<string, ModelLaunchSettings> setter)
            {
                var value = Value();
                if (!string.IsNullOrWhiteSpace(value))
                    Set(ref current, ref anyChanged, setter(value.Trim()));
            }

            switch (option)
            {
                case "-c":
                case "--ctx-size":
                case "--context-size":
                    SetInt(value => current with { ContextSize = value });
                    break;
                case "-ngl":
                case "--n-gpu-layers":
                case "--gpu-layers":
                    SetInt(value => current with { GpuLayers = value });
                    break;
                case "-np":
                case "--parallel":
                    SetInt(value => current with { ParallelSlots = value });
                    break;
                case "-b":
                case "--batch-size":
                    SetInt(value => current with { BatchSize = value });
                    break;
                case "-ub":
                case "--ubatch-size":
                    SetInt(value => current with { MicroBatchSize = value });
                    break;
                case "--temp":
                case "--temperature":
                    SetDouble(value => current with { Temperature = value });
                    break;
                case "--top-k":
                    SetInt(value => current with { TopK = value });
                    break;
                case "--top-p":
                    SetDouble(value => current with { TopP = value });
                    break;
                case "--min-p":
                    SetDouble(value => current with { MinP = value });
                    break;
                case "-n":
                case "--predict":
                case "--n-predict":
                    SetInt(value => current with { MaxTokens = value });
                    break;
                case "-s":
                case "--seed":
                    SetInt(value => current with { Seed = value });
                    break;
                case "--repeat-last-n":
                    SetInt(value => current with { RepeatLastN = value });
                    break;
                case "--repeat-penalty":
                    SetDouble(value => current with { RepeatPenalty = value });
                    break;
                case "--presence-penalty":
                    SetDouble(value => current with { PresencePenalty = value });
                    break;
                case "--frequency-penalty":
                    SetDouble(value => current with { FrequencyPenalty = value });
                    break;
                case "--image-min-tokens":
                    SetInt(value => current with { VisionImageMinTokens = value });
                    break;
                case "--image-max-tokens":
                    SetInt(value => current with { VisionImageMaxTokens = value });
                    break;
                case "-fa":
                case "--flash-attn":
                    SetString(value => current with { FlashAttention = NormalizeMode(value, "auto", "on", "off") });
                    break;
                case "-ctk":
                case "--cache-type-k":
                    SetString(value => current with { CacheTypeK = value });
                    break;
                case "-ctv":
                case "--cache-type-v":
                    SetString(value => current with { CacheTypeV = value });
                    break;
                case "--rope-scaling":
                    SetString(value => current with { RopeScaling = NormalizeMode(value, "auto", "none", "linear", "yarn") });
                    break;
                case "--rope-scale":
                    SetDouble(value => current with { RopeScale = value });
                    break;
                case "--rope-freq-base":
                    SetDouble(value => current with { RopeFreqBase = value });
                    break;
                case "--rope-freq-scale":
                    SetDouble(value => current with { RopeFreqScale = value });
                    break;
                case "--spec-type":
                    SetString(value => current with { SpeculativeType = NormalizeSpeculativeType(value) });
                    break;
                case "--spec-draft-model":
                case "--model-draft":
                case "-md":
                    SetString(value => current with { SpecDraftModelPath = value });
                    break;
                case "--mtp-head":
                    SetString(value => current with { MtpHeadPath = value, SpeculativeType = LaunchSettingMetadataService.AtomicMtpSpeculativeType });
                    break;
                case "--spec-draft-ngl":
                case "--gpu-layers-draft":
                case "--n-gpu-layers-draft":
                case "-ngld":
                    SetInt(value => current with { SpecDraftGpuLayers = value });
                    break;
                case "--spec-draft-n-min":
                    SetInt(value => current with { SpecDraftMinTokens = value });
                    break;
                case "--spec-draft-n-max":
                    SetInt(value => current with { SpecDraftMaxTokens = value });
                    break;
                case "--spec-draft-p-split":
                case "--draft-p-split":
                    SetDouble(value => current with { SpecDraftPSplit = value });
                    break;
                case "--spec-draft-p-min":
                case "--draft-p-min":
                    SetDouble(value => current with { SpecDraftPMin = value });
                    break;
                case "--spec-draft-type-k":
                case "--cache-type-k-draft":
                case "-ctkd":
                    SetString(value => current with { SpecDraftCacheTypeK = value });
                    break;
                case "--spec-draft-type-v":
                case "--cache-type-v-draft":
                case "-ctvd":
                    SetString(value => current with { SpecDraftCacheTypeV = value });
                    break;
            }
        }
        settings = current;
        changed = anyChanged;
    }

    private static bool TryInt(string? value, out int number)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);

    private static bool TryDouble(string? value, out double number)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static bool TryInt(JsonNode node, out int number, params string[] names)
    {
        foreach (var name in names)
        {
            if (node[name] is null) continue;
            if (int.TryParse(node[name]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return true;
        }
        number = 0;
        return false;
    }

    private static bool TryDouble(JsonNode node, out double number, params string[] names)
    {
        foreach (var name in names)
        {
            if (node[name] is null) continue;
            if (double.TryParse(node[name]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return true;
        }
        number = 0;
        return false;
    }

    private static void Set(ref ModelLaunchSettings settings, ref bool changed, ModelLaunchSettings next)
    {
        if (Equals(settings, next)) return;
        settings = next;
        changed = true;
    }

    private static string NormalizeMode(string value, params string[] allowed)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : allowed[0];
    }

    private static string NormalizeSpeculativeType(string value)
    {
        var normalized = LaunchSettingMetadataService.NormalizeSpeculativeType(value);
        var allowed = new[] { "none", "atomic-mtp", "draft-mtp", "draft-simple", "draft-eagle3", "draft-dflash", "draft-dspark", "ngram-simple", "ngram-map-k", "ngram-map-k4v", "ngram-mod", "ngram-cache" };
        return allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : "none";
    }
}
