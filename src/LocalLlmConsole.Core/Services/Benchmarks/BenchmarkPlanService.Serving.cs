using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed partial class BenchmarkPlanService
{
    private static IReadOnlyList<ModelLaunchSettings> LaunchVariants(BenchmarkPlan plan, ModelLaunchSettings profile)
    {
        if (plan.ExecutionMode != BenchmarkExecutionMode.ProfileServing) return [profile];
        IEnumerable<ModelLaunchSettings> variants = [profile];
        variants = Expand(variants, plan.Serving.ContextSizes, profile.ContextSize,
            (settings, value) => settings with { ContextSize = value });
        variants = Expand(variants, plan.Options.BatchSizes, profile.BatchSize,
            (settings, value) => settings with { BatchSize = value });
        variants = Expand(variants, plan.Options.MicroBatchSizes, profile.MicroBatchSize,
            (settings, value) => settings with { MicroBatchSize = value });
        variants = Expand(variants, plan.Options.Threads, profile.Threads,
            (settings, value) => settings with { Threads = value });
        variants = Expand(variants, plan.Options.GpuLayers, profile.GpuLayers,
            (settings, value) => settings with { GpuLayers = value < 0 ? 999 : value });
        variants = Expand(variants, plan.Options.FlashAttention, profile.FlashAttention,
            (settings, value) => settings with { FlashAttention = value.ToLowerInvariant() });
        if (plan.Options.CacheTypesKv.Count > 0)
            variants = Expand(variants, plan.Options.CacheTypesKv, profile.CacheTypeK,
                (settings, value) => settings with { CacheTypeK = value.ToLowerInvariant(), CacheTypeV = value.ToLowerInvariant() });
        else
        {
            variants = Expand(variants, plan.Options.CacheTypesK, profile.CacheTypeK,
                (settings, value) => settings with { CacheTypeK = value.ToLowerInvariant() });
            variants = Expand(variants, plan.Options.CacheTypesV, profile.CacheTypeV,
                (settings, value) => settings with { CacheTypeV = value.ToLowerInvariant() });
        }
        variants = Expand(variants, plan.Options.KvOffload, profile.KvOffload,
            (settings, value) => settings with { KvOffload = value.ToLowerInvariant() });
        if (plan.Options.GpuConfigurations.Count > 0)
            variants = Expand(variants, plan.Options.GpuConfigurations,
                new BenchmarkGpuConfiguration(profile.GpuMode, profile.GpuSplit), ApplyGpuConfiguration);
        else
        {
            variants = Expand(variants, plan.Options.SplitModes, profile.GpuMode,
                (settings, value) => settings with { GpuMode = ServingGpuMode(value) });
            variants = Expand(variants, plan.Options.TensorSplits, profile.GpuSplit,
                (settings, value) => settings with { GpuSplit = value });
        }
        if (plan.Serving.SpeculativeConfigurations.Count > 0)
            variants = Expand(variants, plan.Serving.SpeculativeConfigurations,
                new BenchmarkSpeculativeConfiguration(profile.SpeculativeType, "profile"),
                (settings, value) => ApplySpeculativeConfiguration(settings, value, profile));
        else
        {
            variants = Expand(variants, plan.Serving.SpeculativeTypes, profile.SpeculativeType,
                ApplySpeculativeType);
            variants = Expand(variants, plan.Serving.SpeculativeCompanionModes, "profile",
                (settings, value) => ApplySpeculativeCompanionMode(settings, value, profile));
        }
        return variants
            .Select(NormalizeServingGpuSettings)
            .Distinct()
            .ToArray();
    }

    private static IEnumerable<ModelLaunchSettings> Expand<T>(
        IEnumerable<ModelLaunchSettings> variants,
        IReadOnlyList<T> requested,
        T inherited,
        Func<ModelLaunchSettings, T, ModelLaunchSettings> apply)
    {
        var values = requested.Count > 0 ? requested.Distinct().ToArray() : [inherited];
        return variants.SelectMany(settings => values.Select(value => apply(settings, value)));
    }

    private static ModelLaunchSettings ApplySpeculativeType(ModelLaunchSettings settings, string value)
    {
        var requested = SpeculativeTypePolicy.Normalize(value);
        var saved = SpeculativeTypePolicy.Normalize(settings.SpeculativeType);
        if (requested.Equals(saved, StringComparison.OrdinalIgnoreCase))
            return settings with { SpeculativeType = requested };

        // A path saved for one draft architecture must never leak into another
        // benchmark variant. Empty paths deliberately invoke the Manager's
        // exact-folder companion discovery (or embedded draft-mtp detection).
        return settings with
        {
            SpeculativeType = requested,
            SpecDraftModelPath = "",
            MtpHeadPath = ""
        };
    }

    private static ModelLaunchSettings ApplySpeculativeCompanionMode(
        ModelLaunchSettings settings,
        string value,
        ModelLaunchSettings profile)
    {
        if (value.Equals("profile", StringComparison.OrdinalIgnoreCase)
            && SpeculativeTypePolicy.Normalize(settings.SpeculativeType)
                .Equals(SpeculativeTypePolicy.Normalize(profile.SpeculativeType), StringComparison.OrdinalIgnoreCase))
            return settings with { SpecDraftModelPath = profile.SpecDraftModelPath, MtpHeadPath = profile.MtpHeadPath };
        return settings with { SpecDraftModelPath = "", MtpHeadPath = "" };
    }

    private static ModelLaunchSettings ApplySpeculativeConfiguration(
        ModelLaunchSettings settings,
        BenchmarkSpeculativeConfiguration configuration,
        ModelLaunchSettings profile)
    {
        var withType = ApplySpeculativeType(settings, configuration.Type);
        return ApplySpeculativeCompanionMode(withType, configuration.Head, profile);
    }

    private static string ServingGpuMode(string value)
        => value.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? "single"
            : value.ToLowerInvariant();

    private static ModelLaunchSettings ApplyGpuConfiguration(
        ModelLaunchSettings settings,
        BenchmarkGpuConfiguration configuration)
    {
        var mode = ServingGpuMode(configuration.Mode);
        return settings with
        {
            GpuMode = mode,
            GpuSplit = mode.Equals("single", StringComparison.OrdinalIgnoreCase)
                ? ""
                : NormalizeGpuSplit(configuration.Split)
        };
    }

    private static string NormalizeGpuSplit(string value)
        => string.Join(',', (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static ModelLaunchSettings NormalizeServingGpuSettings(ModelLaunchSettings settings)
    {
        if (!settings.GpuMode.Equals("single", StringComparison.OrdinalIgnoreCase)) return settings;
        var firstDevice = (settings.GpuDevices ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        return settings with { GpuDevices = firstDevice, GpuSplit = "" };
    }

    public static IReadOnlyList<BenchmarkPromptGenerationPair> ServingWorkloads(BenchmarkPlan plan)
    {
        if (plan.PromptGenerationPairs.Count > 0)
            return plan.PromptGenerationPairs
                .Where(pair => pair.PromptTokens > 0 && pair.GenerationTokens > 0)
                .Distinct()
                .ToArray();
        return plan.PromptSizes
            .Where(prompt => prompt > 0)
            .SelectMany(prompt => plan.GenerationSizes.Where(generation => generation > 0),
                (prompt, generation) => new BenchmarkPromptGenerationPair(prompt, generation))
            .Distinct()
            .ToArray();
    }

    private static string ServingSignature(BenchmarkPlan plan, ModelLaunchSettings settings)
        => StableHash(JsonSerializer.Serialize(new
        {
            mode = plan.ExecutionMode,
            settings,
            workloads = ServingWorkloads(plan),
            plan.Serving,
            plan.Repetitions,
            plan.Warmup
        }));
}
