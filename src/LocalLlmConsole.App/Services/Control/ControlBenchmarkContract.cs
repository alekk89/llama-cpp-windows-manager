using System.Reflection;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

internal static class ControlBenchmarkContract
{
    public static object CapabilitySummary => new
    {
        schemaVersion = BenchmarkPlan.CurrentSchemaVersion,
        discovery = "/api/v1/benchmarks/schema",
        presets = "/api/v1/benchmarks/presets",
        runtimeCapabilities = "/api/v1/benchmarks/capabilities?runtime=...&wslDistro=...",
        lifecycle = new[] { "validate", "run", "inspect", "wait", "pause", "resume", "cancel", "delete" },
        artifacts = new[] { "plan", "results", "export", "compare", "log" }
    };

    public static object Schema() => new
    {
        schemaVersion = BenchmarkPlan.CurrentSchemaVersion,
        plan = Fields<BenchmarkPlan>(),
        nested = new
        {
            scopeSelection = Fields<BenchmarkScopeSelection>(),
            promptGenerationPair = Fields<BenchmarkPromptGenerationPair>(),
            gpuConfiguration = Fields<BenchmarkGpuConfiguration>(),
            speculativeConfiguration = Fields<BenchmarkSpeculativeConfiguration>(),
            options = Fields<BenchmarkOptionSet>(),
            serving = Fields<BenchmarkServingOptions>()
        },
        enums = new
        {
            executionMode = Enum.GetNames<BenchmarkExecutionMode>(),
            failurePolicy = Enum.GetNames<BenchmarkFailurePolicy>(),
            resultClassification = Enum.GetNames<BenchmarkResultClassification>(),
            gpuMode = new[] { "single", "layer", "row", "tensor" },
            speculativeHead = new[] { "profile", "auto" },
            speculativeCompanionMode = new[] { "profile", "auto" }
        },
        semantics = new
        {
            scope = "Use scopeSelections for exact model/profile/runtime tuples. The legacy modelIds/profileIds/runtimeIds selectors remain supported.",
            profileServingWorkloads = "When promptGenerationPairs is non-empty it replaces the promptSizes × generationSizes cross-product for ProfileServing.",
            directWorkloads = "For LlamaBench, promptSizes, generationSizes, and promptGenerationPairs are independent PP, TG, and PG test families.",
            gpuConfigurations = "Each mode/split pair is one configuration. Single cannot have a split. Other modes accept automatic (empty) or comma-separated non-negative weights with at least one positive value.",
            speculativeConfigurations = "Each type/head pair is one ProfileServing configuration. Head is profile to reuse the compatible saved companion, or auto to resolve a compatible exact-folder companion or embedded draft-MTP tensors.",
            confirmation = "Starting and deleting runs require explicit confirmation. Active runs must be cancelled before deletion."
        },
        limits = new
        {
            repetitions = new { minimum = 1, maximum = 50 },
            concurrency = new { minimum = 1, maximum = 64 },
            readyTimeoutSeconds = new { minimum = 10, maximum = 3600 },
            requestTimeoutSeconds = new { minimum = 10, maximum = 3600 },
            maximumExpandedResultRows = BenchmarkPlanService.MaximumResultRows
        }
    };

    public static object Presets() => new
    {
        ok = true,
        presets = BenchmarkWorkloadPresetCatalog.All.Select(preset => new
        {
            preset.Name,
            preset.Description,
            preset.PromptGenerationPairs,
            preset.ContextSize,
            preset.Repetitions,
            preset.ReadyTimeoutSeconds,
            preset.RequestTimeoutSeconds,
            planTemplate = new BenchmarkPlan
            {
                Name = $"{preset.Name} context benchmark",
                ExecutionMode = BenchmarkExecutionMode.ProfileServing,
                PromptSizes = [],
                GenerationSizes = [],
                PromptGenerationPairs = preset.PromptGenerationPairs,
                Repetitions = preset.Repetitions,
                Serving = new BenchmarkServingOptions
                {
                    ContextSizes = [preset.ContextSize],
                    Concurrencies = [1],
                    ReadyTimeoutSeconds = preset.ReadyTimeoutSeconds,
                    RequestTimeoutSeconds = preset.RequestTimeoutSeconds
                }
            }
        }).ToArray(),
        scopeRequired = "Add scopeSelections, or supported legacy model/profile/runtime selectors, before validation."
    };

    public static object RuntimeCapability(BenchmarkRuntimeCapability capability)
    {
        var deviceCount = capability.AvailableDevices.Count;
        var equalSplit = deviceCount > 1 ? string.Join(',', Enumerable.Repeat("1", deviceCount)) : "";
        return new
        {
            capability.RuntimeId,
            capability.IsAvailable,
            capability.BenchmarkExecutablePath,
            supportedOptions = capability.SupportedOptions.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            capability.AvailableDevices,
            detectedDeviceCount = deviceCount,
            supportedGpuModes = new[] { "single", "layer", "row", "tensor" },
            suggestedGpuConfigurations = deviceCount switch
            {
                <= 0 => Array.Empty<BenchmarkGpuConfiguration>(),
                1 => [new BenchmarkGpuConfiguration("single")],
                _ => new[]
                {
                    new BenchmarkGpuConfiguration("single"),
                    new BenchmarkGpuConfiguration("layer", equalSplit),
                    new BenchmarkGpuConfiguration("row", equalSplit),
                    new BenchmarkGpuConfiguration("tensor", equalSplit)
                }
            },
            capability.HelpFingerprint,
            capability.DeviceProbeWarning,
            capability.Error
        };
    }

    public static object Comparison(BenchmarkRunComparison comparison) => new
    {
        ok = true,
        comparison.BaselineRunId,
        comparison.BaselineName,
        comparison.CandidateRunId,
        comparison.CandidateName,
        comparison.Summary,
        rows = comparison.Rows.Select(row => new
        {
            row.WorkloadSignature,
            classification = row.Classification.ToString(),
            row.PromptTokens,
            row.GenerationTokens,
            row.ContextSize,
            row.BatchSize,
            row.Depth,
            row.BaselineTokensPerSecond,
            row.CandidateTokensPerSecond,
            row.PercentChange,
            row.EnvironmentMatches
        }).ToArray()
    };

    private static IReadOnlyList<object> Fields<T>()
        => typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => (object)new
            {
                name = JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                type = TypeName(property.PropertyType)
            })
            .ToArray();

    private static string TypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            return $"array<{TypeName(underlying.GetGenericArguments()[0])}>";
        if (underlying == typeof(string)) return "string";
        if (underlying == typeof(bool)) return "boolean";
        if (underlying == typeof(int) || underlying == typeof(long)) return "integer";
        if (underlying == typeof(double) || underlying == typeof(decimal)) return "number";
        return underlying.IsEnum ? $"enum:{underlying.Name}" : underlying.Name;
    }
}
