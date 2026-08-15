namespace LocalLlmConsole.Services;

public sealed partial class ModelCatalogService
{
    private static ModelLaunchSettings? TryReadLegacyLaunchSettings(string modelPath)
    {
        var modelJson = FindLegacyModelJson(modelPath);
        if (modelJson is null) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(modelJson));
            var root = document.RootElement;
            var hasSettings = root.TryGetProperty("settings", out var settings) && settings.ValueKind == JsonValueKind.Object;
            var defaults = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetPathRoot(modelPath) ?? ""));

            bool TryElement(string name, out JsonElement value)
            {
                if (hasSettings && settings.TryGetProperty(name, out value)) return true;
                return root.TryGetProperty(name, out value);
            }

            int ReadInt(int fallback, params string[] names)
            {
                foreach (var name in names)
                {
                    if (!TryElement(name, out var value)) continue;
                    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
                    if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
                }
                return fallback;
            }

            double ReadDouble(double fallback, params string[] names)
            {
                foreach (var name in names)
                {
                    if (!TryElement(name, out var value)) continue;
                    if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
                    if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number)) return number;
                }
                return fallback;
            }

            string ReadString(string fallback, params string[] names)
            {
                foreach (var name in names)
                {
                    if (!TryElement(name, out var value)) continue;
                    if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? fallback;
                    if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean() ? "on" : "off";
                    if (value.ValueKind == JsonValueKind.Number) return value.ToString();
                }
                return fallback;
            }

            bool TryReadBool(out bool result, params string[] names)
            {
                foreach (var name in names)
                {
                    if (!TryElement(name, out var value)) continue;
                    if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        result = value.GetBoolean();
                        return true;
                    }
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        var text = (value.GetString() ?? "").Trim();
                        if (bool.TryParse(text, out result)) return true;
                        if (string.Equals(text, "on", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "enabled", StringComparison.OrdinalIgnoreCase))
                        {
                            result = true;
                            return true;
                        }
                        if (string.Equals(text, "off", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            result = false;
                            return true;
                        }
                    }
                }
                result = false;
                return false;
            }

            string ReadMode(string fallback, params string[] names)
            {
                var value = ReadString(fallback, names).Trim().ToLowerInvariant();
                return value switch
                {
                    "true" => "on",
                    "false" => "off",
                    "enabled" => "on",
                    "disabled" => "off",
                    "" => fallback,
                    _ => value
                };
            }

            var mmapMode = defaults.MmapMode;
            if (TryReadBool(out var noMmap, "noMmap")) mmapMode = noMmap ? "off" : "on";

            var visionMode = defaults.VisionMode;
            if (TryReadBool(out var vision, "mmprojOffload", "vision")) visionMode = vision ? "on" : "off";

            return defaults with
            {
                ContextSize = ReadInt(defaults.ContextSize, "ctxSize", "contextSize", "contextWindow"),
                GpuLayers = ReadInt(defaults.GpuLayers, "nGpuLayers", "gpuLayers"),
                EnableMetrics = TryReadBool(out var metrics, "metrics", "enableMetrics") ? metrics : defaults.EnableMetrics,
                ReasoningMode = ReadMode(defaults.ReasoningMode, "reasoning"),
                ReasoningFormat = ReadMode(defaults.ReasoningFormat, "reasoningFormat"),
                ReasoningEffort = ReadMode(defaults.ReasoningEffort, "reasoningEffort"),
                ReasoningBudget = ReadInt(defaults.ReasoningBudget, "reasoningBudget"),
                ReasoningBudgetMessage = ReadString(defaults.ReasoningBudgetMessage, "reasoningBudgetMessage"),
                ReasoningPreserve = ReadMode(defaults.ReasoningPreserve, "reasoningPreserve"),
                VisionMode = visionMode,
                FlashAttention = ReadMode(defaults.FlashAttention, "flashAttn", "flashAttention"),
                CacheTypeK = ReadMode(defaults.CacheTypeK, "cacheTypeK"),
                CacheTypeV = ReadMode(defaults.CacheTypeV, "cacheTypeV"),
                KvOffload = ReadMode(defaults.KvOffload, "kvOffload", "cacheOffload"),
                KvUnified = ReadMode(defaults.KvUnified, "kvUnified"),
                ContinuousBatching = ReadMode(defaults.ContinuousBatching, "continuousBatching", "contBatching"),
                JinjaMode = ReadMode(defaults.JinjaMode, "jinja"),
                ParallelSlots = ReadInt(defaults.ParallelSlots, "parallel", "parallelSlots"),
                BatchSize = ReadInt(defaults.BatchSize, "batchSize"),
                MicroBatchSize = ReadInt(defaults.MicroBatchSize, "ubatchSize", "microBatchSize"),
                Threads = ReadInt(defaults.Threads, "threads"),
                MmapMode = mmapMode,
                MlockMode = ReadMode(defaults.MlockMode, "mlock"),
                Temperature = ReadDouble(defaults.Temperature, "temperature", "temp"),
                TopK = ReadInt(defaults.TopK, "topK"),
                TopP = ReadDouble(defaults.TopP, "topP"),
                MinP = ReadDouble(defaults.MinP, "minP"),
                RuntimeId = ReadString(defaults.RuntimeId, "llamaCppRuntimeId", "runtimeId")
            };
        }
        catch
        {
            return null;
        }
    }

    public static (string Repo, string Path)? TryReadLegacySourceReference(string modelPath)
    {
        var modelJson = FindLegacyModelJson(modelPath);
        if (modelJson is null) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(modelJson));
            var root = document.RootElement;
            string StringValue(params string[] names)
            {
                foreach (var name in names)
                {
                    if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                        return value.GetString() ?? "";
                }
                return "";
            }

            var repo = StringValue("sourceRepo", "repo");
            var file = StringValue("sourceFile", "servedModelName", "modelFile");
            return string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(file)
                ? null
                : (repo, file.Replace('\\', '/'));
        }
        catch
        {
            return null;
        }
    }
}
