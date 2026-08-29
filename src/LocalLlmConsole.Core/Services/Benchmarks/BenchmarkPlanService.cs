using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed partial class BenchmarkPlanService
{
    public const int MaximumWorkItems = 500;
    public const int MaximumResultRows = 5_000;
    public const int MaximumTimedRepetitions = 50_000;

    public BenchmarkPlanPreview Preview(
        BenchmarkPlan plan,
        IReadOnlyList<ModelRecord> models,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        IReadOnlyList<RuntimeRecord> runtimes)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var errors = ValidatePlan(plan).ToList();
        var warnings = new List<string>();
        var selectedModels = SelectModels(plan, models, errors);
        var selectedProfiles = SelectProfiles(plan, selectedModels, profiles, errors);
        var workItems = new List<BenchmarkWorkItem>();

        foreach (var model in selectedModels)
        {
            var modelProfiles = selectedProfiles.Where(profile => Same(profile.ModelId, model.Id)).ToArray();
            foreach (var profile in modelProfiles)
            {
                var selectedRuntimes = SelectRuntimes(plan, profile, runtimes, errors);
                foreach (var runtime in selectedRuntimes)
                {
                    foreach (var launchSettings in LaunchVariants(plan, profile.Settings))
                    {
                        if (plan.ExecutionMode == BenchmarkExecutionMode.ProfileServing
                            && launchSettings.MicroBatchSize > launchSettings.BatchSize)
                        {
                            errors.Add($"{profile.Name}: micro-batch size {launchSettings.MicroBatchSize} exceeds tested batch size {launchSettings.BatchSize}.");
                        }
                        if (plan.ExecutionMode == BenchmarkExecutionMode.ProfileServing)
                            ValidateGpuSplitDeviceCount(profile.Name, [launchSettings.GpuDevices], [launchSettings.GpuSplit], errors);
                        foreach (var options in EffectiveOptionVariants(plan, launchSettings))
                        {
                            if (plan.ExecutionMode == BenchmarkExecutionMode.LlamaBench)
                                ValidateGpuSplitDeviceCount(profile.Name, options.Devices, options.TensorSplits, errors);
                            var commandSignature = plan.ExecutionMode == BenchmarkExecutionMode.ProfileServing
                                ? ServingSignature(plan, launchSettings)
                                : BenchmarkCommandBuilder.EffectiveSignature(plan, options);
                            workItems.Add(new BenchmarkWorkItem(
                                Key: StableHash($"{runtime.Id}|{model.Id}|{profile.Id}|{commandSignature}")[..24],
                                ModelId: model.Id,
                                ModelName: model.Name,
                                ModelPath: model.ModelPath,
                                ModelFingerprint: ModelFingerprint(model),
                                ProfileIds: [profile.Id],
                                ProfileNames: [profile.Name],
                                RuntimeId: runtime.Id,
                                RuntimeName: runtime.Name,
                                RuntimeMode: runtime.Mode,
                                RuntimeBackend: runtime.Backend,
                                RuntimeExecutablePath: runtime.ExecutablePath,
                                WslDistro: string.IsNullOrWhiteSpace(plan.WslDistro) ? RuntimeWslDistro(runtime) : plan.WslDistro,
                                Options: options,
                                EffectiveCommandSignature: commandSignature,
                                ExpectedResultRows: ResultRows(plan, options),
                                ExecutionMode: plan.ExecutionMode,
                                LaunchSettings: launchSettings));
                        }
                    }
                }
            }
        }

        var beforeDeduplication = workItems.Count;
        if (!plan.RepeatEquivalentProfiles && plan.ExecutionMode == BenchmarkExecutionMode.LlamaBench)
            workItems = Deduplicate(workItems);

        var expectedRows = SaturatingSum(workItems.Select(item => item.ExpectedResultRows), MaximumResultRows + 1);
        var timedRepetitions = SaturatingMultiply(expectedRows, Math.Max(plan.Repetitions, 0), MaximumTimedRepetitions + 1);
        if (workItems.Count > MaximumWorkItems)
            errors.Add($"The plan expands to {workItems.Count} work items; the maximum is {MaximumWorkItems}.");
        if (expectedRows > MaximumResultRows)
            errors.Add($"The plan expands to {expectedRows} result rows; the maximum is {MaximumResultRows}.");
        if (timedRepetitions > MaximumTimedRepetitions)
            errors.Add($"The plan expands to {timedRepetitions} timed repetitions; the maximum is {MaximumTimedRepetitions}.");
        if (beforeDeduplication != workItems.Count)
            warnings.Add($"Collapsed {beforeDeduplication - workItems.Count} equivalent profile work item(s).");

        return new BenchmarkPlanPreview(
            errors.Count == 0,
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings,
            workItems,
            expectedRows,
            timedRepetitions,
            beforeDeduplication - workItems.Count);
    }

    public static IReadOnlyList<string> ValidatePlan(BenchmarkPlan plan)
    {
        var errors = new List<string>();
        if (plan.SchemaVersion != BenchmarkPlan.CurrentSchemaVersion)
            errors.Add($"Unsupported benchmark plan schema version {plan.SchemaVersion}.");
        if (plan.Repetitions is < 1 or > 50) errors.Add("Repetitions must be between 1 and 50.");
        if (plan.DelaySeconds is < 0 or > 600) errors.Add("Delay must be between 0 and 600 seconds.");
        if (plan.CooldownSeconds is < 0 or > 3600) errors.Add("Cooldown must be between 0 and 3600 seconds.");
        if (plan.ExecutionMode == BenchmarkExecutionMode.ProfileServing)
        {
            if (ServingWorkloads(plan).Count == 0)
                errors.Add("Profile-serving benchmarks require at least one prompt and generation workload.");
            ValidatePositive(plan.Serving.ContextSizes, "Context size", allowZero: false, errors);
            ValidateSpeculativeConfigurations(plan.Serving.SpeculativeConfigurations, errors);
            if (plan.Serving.SpeculativeConfigurations.Count > 0
                && (plan.Serving.SpeculativeTypes.Count > 0 || plan.Serving.SpeculativeCompanionModes.Count > 0))
                errors.Add("Paired speculative configurations cannot be combined with legacy speculative-type or companion-mode lists.");
            ValidateAllowed(plan.Serving.SpeculativeTypes, "Speculative type", SpeculativeTypePolicy.SupportedTypes, errors);
            ValidateDimension(plan.Serving.SpeculativeTypes, "speculative-type", errors);
            ValidateAllowed(plan.Serving.SpeculativeCompanionModes, "Speculative companion mode", ["profile", "auto"], errors);
            ValidateDimension(plan.Serving.SpeculativeCompanionModes, "speculative-companion", errors);
            ValidateDimension(plan.Serving.SpeculativeConfigurations.Select(SpeculativeConfigurationKey).ToArray(), "speculative configuration", errors);
            ValidatePositive(plan.Serving.Concurrencies, "concurrency", allowZero: false, errors);
            if (plan.Serving.Concurrencies.Any(value => value > 64)) errors.Add("Concurrency must be 64 or less.");
            if (plan.Serving.ReadyTimeoutSeconds is < 10 or > 3600) errors.Add("Server ready timeout must be between 10 and 3600 seconds.");
            if (plan.Serving.RequestTimeoutSeconds is < 10 or > 3600) errors.Add("Request timeout must be between 10 and 3600 seconds.");
            if (!double.IsFinite(plan.Serving.Temperature) || plan.Serving.Temperature is < 0 or > 2)
                errors.Add("Serving temperature must be between 0 and 2.");
            if (plan.PromptGenerationPairs.Any(pair => pair.PromptTokens <= 0 || pair.GenerationTokens <= 0))
                errors.Add("Profile-serving prompt/generation pairs must contain positive prompt and generation values.");
        }
        else if (plan.PromptSizes.Count + plan.GenerationSizes.Count + plan.PromptGenerationPairs.Count == 0)
            errors.Add("Select at least one PP, TG, or PG test.");
        ValidatePositive(plan.PromptSizes, "PP", allowZero: false, errors);
        ValidatePositive(plan.GenerationSizes, "TG", allowZero: false, errors);
        ValidatePositive(plan.Depths, "Depth", allowZero: true, errors);
        foreach (var pair in plan.PromptGenerationPairs)
            if (pair.PromptTokens < 0 || pair.GenerationTokens < 0 || pair.PromptTokens + pair.GenerationTokens == 0)
                errors.Add("PG pairs must contain non-negative values and at least one non-zero value.");
        if (plan.Options.BatchSizes.Any(value => value < 1)) errors.Add("Batch sizes must be positive.");
        if (plan.Options.MicroBatchSizes.Any(value => value < 1)) errors.Add("Micro-batch sizes must be positive.");
        if (plan.Options.Threads.Any(value => value < 1)) errors.Add("Explicit thread counts must be positive.");
        if (plan.Options.GpuLayers.Any(value => value < -1)) errors.Add("GPU layers must be -1 or greater.");
        if (plan.Options.CpuMoeLayers.Any(value => value < 0)) errors.Add("CPU MoE layers must be non-negative.");
        if (plan.Options.MainGpus.Any(value => value < 0)) errors.Add("Main GPU indexes must be non-negative.");
        if (plan.Options.FitTargetsMiB.Any(value => value < 0)) errors.Add("Fit targets must be non-negative.");
        if (plan.Options.FitContexts.Any(value => value < 0)) errors.Add("Fit contexts must be non-negative.");
        if (plan.Options.Priorities.Any(value => value is < -1 or > 3)) errors.Add("Priority must be between -1 and 3.");
        if (plan.Options.PollValues.Any(value => value is < 0 or > 100)) errors.Add("Poll values must be between 0 and 100.");
        if (plan.Options.NumaModes.Count > 1) errors.Add("NUMA accepts one value per benchmark plan.");
        if (plan.Options.Priorities.Count > 1) errors.Add("Priority accepts one value per benchmark plan.");
        if (plan.Options.NumaModes.Any(value => value is not ("distribute" or "isolate" or "numactl")))
            errors.Add("NUMA must be distribute, isolate, or numactl.");
        ValidateBoolean(plan.Options.CpuStrict, "CPU strict", errors);
        ValidateBoolean(plan.Options.Embeddings, "Embeddings", errors);
        ValidateBoolean(plan.Options.NoOpOffload, "No-op offload", errors);
        ValidateBoolean(plan.Options.NoHost, "No-host", errors);
        ValidateAllowed(plan.Options.FlashAttention, "Flash attention", ["auto", "on", "off"], errors);
        ValidateAllowed(plan.Options.KvOffload, "KV offload", ["on", "off"], errors);
        ValidateGpuConfigurations(plan.Options.GpuConfigurations, errors);
        if (plan.Options.GpuConfigurations.Count > 0
            && (plan.Options.SplitModes.Count > 0 || plan.Options.TensorSplits.Count > 0))
            errors.Add("Paired GPU configurations cannot be combined with legacy split-mode or tensor-split lists.");
        ValidateAllowed(plan.Options.SplitModes, "Split mode", ["none", "layer", "row", "tensor"], errors);
        ValidateAllowed(plan.Options.LoadModes, "Load mode", ["none", "mmap", "mlock", "mmap+mlock", "dio"], errors);
        ValidateAllowed(plan.Options.CacheTypesK, "K cache type", ["f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1"], errors);
        ValidateAllowed(plan.Options.CacheTypesV, "V cache type", ["f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1"], errors);
        ValidateAllowed(plan.Options.CacheTypesKv, "K/V cache type", ["f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1"], errors);
        if (plan.Options.CacheTypesKv.Count > 0 && (plan.Options.CacheTypesK.Count > 0 || plan.Options.CacheTypesV.Count > 0))
            errors.Add("Matched K/V cache types cannot be combined with independent K or V cache lists.");
        if (plan.Options.AdditionalArguments.Count > 256) errors.Add("At most 256 expert argument tokens are allowed.");
        if (plan.Options.AdditionalArguments.Any(value => value.Contains('\0'))) errors.Add("Expert arguments cannot contain null bytes.");
        ValidateDimension(plan.Options.Threads, "thread", errors);
        ValidateDimension(plan.Options.BatchSizes, "batch-size", errors);
        ValidateDimension(plan.Options.MicroBatchSizes, "micro-batch-size", errors);
        ValidateDimension(plan.Options.GpuLayers, "GPU-layer", errors);
        ValidateDimension(plan.Options.CpuMoeLayers, "CPU-MoE-layer", errors);
        ValidateDimension(plan.Options.FlashAttention, "flash-attention", errors);
        ValidateDimension(plan.Options.CacheTypesK, "K-cache", errors);
        ValidateDimension(plan.Options.CacheTypesV, "V-cache", errors);
        ValidateDimension(plan.Options.CacheTypesKv, "K/V-cache", errors);
        ValidateDimension(plan.Options.KvOffload, "KV-offload", errors);
        ValidateDimension(plan.Options.GpuConfigurations.Select(GpuConfigurationKey).ToArray(), "GPU configuration", errors);
        ValidateDimension(plan.Options.SplitModes, "split-mode", errors);
        ValidateDimension(plan.Options.MainGpus, "main-GPU", errors);
        ValidateDimension(plan.Options.Devices, "device", errors);
        ValidateDimension(plan.Options.TensorSplits, "tensor-split", errors);
        ValidateDimension(plan.Options.LoadModes, "load-mode", errors);
        ValidateDimension(plan.Options.FitTargetsMiB, "fit-target", errors);
        ValidateDimension(plan.Options.FitContexts, "fit-context", errors);
        ValidateDimension(plan.Options.NumaModes, "NUMA", errors);
        ValidateDimension(plan.Options.Priorities, "priority", errors);
        ValidateDimension(plan.Options.CpuMasks, "CPU-mask", errors);
        ValidateDimension(plan.Options.CpuStrict, "CPU-strict", errors);
        ValidateDimension(plan.Options.PollValues, "poll", errors);
        ValidateDimension(plan.Options.Embeddings, "embeddings", errors);
        ValidateDimension(plan.Options.NoOpOffload, "no-op-offload", errors);
        ValidateDimension(plan.Options.NoHost, "no-host", errors);
        ValidateDimension(plan.Options.TensorOverrides, "tensor-override", errors);
        return errors;
    }

    private static IReadOnlyList<ModelRecord> SelectModels(BenchmarkPlan plan, IReadOnlyList<ModelRecord> models, List<string> errors)
    {
        var scopeModelIds = plan.ScopeSelections.Select(selection => selection.ModelId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = scopeModelIds.Count > 0
            ? models.Where(model => scopeModelIds.Contains(model.Id)).ToArray()
            : plan.AllModels
            ? models
            : models.Where(model => plan.ModelIds.Any(id => Same(id, model.Id) || Same(id, model.Name))).ToArray();
        if (selected.Count == 0) errors.Add("No benchmark models were selected or found.");
        return selected;
    }

    private static IReadOnlyList<NamedModelLaunchProfile> SelectProfiles(
        BenchmarkPlan plan,
        IReadOnlyList<ModelRecord> models,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        List<string> errors)
    {
        var modelIds = models.Select(model => model.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = profiles.Where(profile => modelIds.Contains(profile.ModelId));
        var scopeProfileIds = plan.ScopeSelections.Select(selection => selection.ProfileId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = scopeProfileIds.Count > 0
            ? candidates.Where(profile => scopeProfileIds.Contains(profile.Id)).ToArray()
            : plan.AllProfiles
            ? candidates.ToArray()
            : plan.ProfileIds.Count > 0
                ? candidates.Where(profile => plan.ProfileIds.Any(id => Same(id, profile.Id) || Same(id, profile.Name))).ToArray()
                : candidates.Where(profile => profile.IsDefault).ToArray();
        foreach (var model in models.Where(model => selected.All(profile => !Same(profile.ModelId, model.Id))))
            errors.Add($"No selected launch profile was found for {model.Name}.");
        return selected;
    }

    private static IReadOnlyList<RuntimeRecord> SelectRuntimes(
        BenchmarkPlan plan,
        NamedModelLaunchProfile profile,
        IReadOnlyList<RuntimeRecord> runtimes,
        List<string> errors)
    {
        IReadOnlyList<RuntimeRecord> selected;
        var exactSelections = plan.ScopeSelections
            .Where(selection => Same(selection.ProfileId, profile.Id))
            .ToArray();
        if (exactSelections.Length > 0)
        {
            var runtimeIds = exactSelections
                .Select(selection => string.IsNullOrWhiteSpace(selection.RuntimeId) ? profile.Settings.RuntimeId : selection.RuntimeId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            selected = runtimes.Where(runtime => runtimeIds.Contains(runtime.Id)).ToArray();
        }
        else if (plan.AllRuntimes)
            selected = runtimes;
        else if (plan.UseProfileRuntime && !string.IsNullOrWhiteSpace(profile.Settings.RuntimeId))
            selected = runtimes.Where(runtime => Same(runtime.Id, profile.Settings.RuntimeId)).ToArray();
        else if (plan.RuntimeIds.Count > 0)
            selected = runtimes.Where(runtime => plan.RuntimeIds.Any(id => Same(id, runtime.Id) || Same(id, runtime.Name))).ToArray();
        else
            selected = [];
        if (selected.Count == 0)
            errors.Add($"No runtime was resolved for profile {profile.Name}.");
        return selected;
    }

    private static BenchmarkEffectiveOptions EffectiveOptions(BenchmarkOptionSet requested, ModelLaunchSettings profile)
        => new(
            requested.Threads.Count > 0 ? requested.Threads : profile.Threads > 0 ? [profile.Threads] : [],
            requested.BatchSizes.Count > 0 ? requested.BatchSizes : [profile.BatchSize],
            requested.MicroBatchSizes.Count > 0 ? requested.MicroBatchSizes : [profile.MicroBatchSize],
            requested.GpuLayers.Count > 0 ? requested.GpuLayers : [profile.GpuLayers >= 999 ? -1 : profile.GpuLayers],
            requested.CpuMoeLayers,
            requested.FlashAttention.Count > 0 ? Lower(requested.FlashAttention) : NormalizeAuto(profile.FlashAttention),
            requested.CacheTypesK.Count > 0 ? Lower(requested.CacheTypesK) : NonBlank(profile.CacheTypeK),
            requested.CacheTypesV.Count > 0 ? Lower(requested.CacheTypesV) : NonBlank(profile.CacheTypeV),
            requested.KvOffload.Count > 0 ? Lower(requested.KvOffload) : NormalizeAuto(profile.KvOffload),
            requested.GpuConfigurations.Count > 0
                ? []
                : requested.SplitModes.Count > 0 ? Lower(requested.SplitModes) : GpuModes(profile.GpuMode),
            requested.MainGpus,
            requested.Devices.Count > 0 ? requested.Devices : NonBlank(profile.GpuDevices),
            requested.GpuConfigurations.Count > 0
                ? []
                : requested.TensorSplits.Count > 0 ? requested.TensorSplits : NonBlank(profile.GpuSplit),
            requested.LoadModes.Count > 0 ? Lower(requested.LoadModes) : LoadModes(profile.MmapMode),
            requested.FitTargetsMiB,
            requested.FitContexts,
            Lower(requested.NumaModes),
            requested.Priorities,
            requested.CpuMasks,
            requested.CpuStrict,
            requested.PollValues,
            requested.Embeddings,
            requested.NoOpOffload,
            requested.NoHost,
            requested.TensorOverrides,
            requested.AdditionalArguments);

    private static int ResultRows(BenchmarkPlan plan, BenchmarkEffectiveOptions options)
    {
        if (plan.ExecutionMode == BenchmarkExecutionMode.ProfileServing)
            return SaturatingMultiply(ServingWorkloads(plan).Count, plan.Serving.Concurrencies.Count, MaximumResultRows + 1);
        var families = plan.PromptSizes.Count + plan.GenerationSizes.Count + plan.PromptGenerationPairs.Count;
        var dimensions = new[]
        {
            Count(options.Threads), Count(options.BatchSizes), Count(options.MicroBatchSizes), Count(options.GpuLayers),
            Count(options.CpuMoeLayers),
            Count(options.FlashAttention), Count(options.CacheTypesK), Count(options.CacheTypesV), Count(options.KvOffload),
            Count(options.SplitModes), Count(options.MainGpus), Count(options.Devices), Count(options.TensorSplits), Count(options.LoadModes),
            Count(options.FitTargetsMiB), Count(options.FitContexts), Count(options.CpuMasks), Count(options.CpuStrict),
            Count(options.PollValues), Count(options.Embeddings), Count(options.NoOpOffload), Count(options.NoHost), Count(options.TensorOverrides)
        };
        var result = SaturatingMultiply(families, Math.Max(plan.Depths.Count, 1), MaximumResultRows + 1);
        foreach (var dimension in dimensions)
            result = SaturatingMultiply(result, dimension, MaximumResultRows + 1);
        return result;
    }

    private static List<BenchmarkWorkItem> Deduplicate(IEnumerable<BenchmarkWorkItem> items)
        => items.GroupBy(item => $"{item.RuntimeId}|{item.ModelId}|{item.EffectiveCommandSignature}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return first with
                {
                    ProfileIds = group.SelectMany(item => item.ProfileIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    ProfileNames = group.SelectMany(item => item.ProfileNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                };
            })
            .OrderBy(item => item.RuntimeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ModelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ProfileNames.FirstOrDefault(), StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string ModelFingerprint(ModelRecord model)
    {
        try
        {
            var metadata = JsonNode.Parse(model.MetadataJson) as JsonObject;
            var sha = metadata?["sha256"]?.ToString() ?? metadata?["verifiedSha256"]?.ToString();
            if (!string.IsNullOrWhiteSpace(sha)) return $"sha256:{sha.ToLowerInvariant()}";
        }
        catch { }
        try
        {
            var info = new FileInfo(model.ModelPath);
            return $"file:{Path.GetFullPath(model.ModelPath).ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return $"path:{model.ModelPath.ToLowerInvariant()}";
        }
    }

    private static string RuntimeWslDistro(RuntimeRecord runtime)
    {
        if (runtime.Mode != RuntimeMode.Wsl) return "";
        try
        {
            var metadata = JsonNode.Parse(runtime.MetadataJson) as JsonObject;
            return metadata?["wslDistro"]?.ToString() ?? metadata?["distro"]?.ToString() ?? "";
        }
        catch { return ""; }
    }

    internal static string StableHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static IReadOnlyList<string> NormalizeAuto(string value)
        => string.IsNullOrWhiteSpace(value) || Same(value, "auto") ? [] : [value.ToLowerInvariant()];
    private static IReadOnlyList<string> NonBlank(string value) => string.IsNullOrWhiteSpace(value) ? [] : [value];
    private static IReadOnlyList<string> Lower(IReadOnlyList<string> values)
        => values.Select(value => value.ToLowerInvariant()).ToArray();
    private static IReadOnlyList<string> GpuModes(string value)
        => string.IsNullOrWhiteSpace(value) || Same(value, "auto")
            ? []
            : [Same(value, "single") ? "none" : value.ToLowerInvariant()];
    private static IReadOnlyList<string> LoadModes(string value) => value.ToLowerInvariant() switch
    {
        "on" => ["mmap"],
        "off" => ["none"],
        _ => []
    };
    private static int Count<T>(IReadOnlyList<T> values) => Math.Max(values.Count, 1);
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static void ValidatePositive(IReadOnlyList<int> values, string name, bool allowZero, ICollection<string> errors)
    {
        if (values.Any(value => value < (allowZero ? 0 : 1)))
            errors.Add($"{name} values must be {(allowZero ? "non-negative" : "positive")}.");
        if (values.Count > 64) errors.Add($"At most 64 {name} values are allowed.");
    }

    private static void ValidateDimension<T>(IReadOnlyList<T> values, string name, ICollection<string> errors)
    {
        if (values.Count > 64) errors.Add($"At most 64 {name} values are allowed.");
        if (values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            errors.Add($"Duplicate {name} values are not allowed.");
    }

    private static void ValidateAllowed(
        IReadOnlyList<string> values,
        string name,
        IReadOnlyList<string> allowed,
        ICollection<string> errors)
    {
        if (values.Any(value => !allowed.Contains(value, StringComparer.OrdinalIgnoreCase)))
            errors.Add($"{name} must be one of: {string.Join(", ", allowed)}.");
    }

    private static int SaturatingSum(IEnumerable<int> values, int ceiling)
    {
        long total = 0;
        foreach (var value in values)
        {
            total += value;
            if (total >= ceiling) return ceiling;
        }
        return (int)total;
    }

    private static int SaturatingMultiply(int left, int right, int ceiling)
    {
        if (left <= 0 || right <= 0) return 0;
        var product = (long)left * right;
        return product >= ceiling ? ceiling : (int)product;
    }
}
