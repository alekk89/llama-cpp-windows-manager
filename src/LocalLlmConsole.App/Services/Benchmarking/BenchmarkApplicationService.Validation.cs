using System.Globalization;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed partial class BenchmarkApplicationService
{
    public async Task<BenchmarkPlanPreview> ValidateAsync(BenchmarkPlan plan, CancellationToken cancellationToken = default)
    {
        var modelsTask = _store.ListModelsAsync();
        var profilesTask = _store.ListNamedModelLaunchProfilesAsync();
        var runtimesTask = _store.ListRuntimesAsync();
        await Task.WhenAll(modelsTask, profilesTask, runtimesTask);
        var preview = _planner.Preview(plan, await modelsTask, await profilesTask, await runtimesTask);
        if (!preview.IsValid) return preview;

        var errors = preview.Errors.ToList();
        var warnings = preview.Warnings.ToList();
        var modelsById = (await modelsTask).ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var item in preview.WorkItems.GroupBy(item => item.RuntimeId, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
        {
            var runtime = (await runtimesTask).First(runtime => runtime.Id.Equals(item.RuntimeId, StringComparison.OrdinalIgnoreCase));
            if (item.ExecutionMode == BenchmarkExecutionMode.ProfileServing)
            {
                if (item.LaunchSettings is null)
                    errors.Add($"{item.ProfileNames.FirstOrDefault()}: the launch-profile snapshot is missing.");
                else if (plan.Serving.Concurrencies.DefaultIfEmpty(1).Max() > Math.Max(item.LaunchSettings.ParallelSlots, 1))
                    errors.Add($"{item.ProfileNames.FirstOrDefault()}: maximum benchmark concurrency {plan.Serving.Concurrencies.Max()} exceeds the profile's {item.LaunchSettings.ParallelSlots} parallel slot(s). Save a profile with enough slots so the benchmark measures the requested configuration without changing it.");
                continue;
            }
            var capability = await _capabilities.ProbeAsync(runtime, item.WslDistro, cancellationToken);
            if (!capability.IsAvailable)
            {
                errors.Add($"{runtime.Name}: {capability.Error}");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(capability.DeviceProbeWarning))
                warnings.Add($"{runtime.Name}: {capability.DeviceProbeWarning}");
            else if (capability.AvailableDevices.Count > 0)
                warnings.Add($"{runtime.Name} devices: {string.Join(", ", capability.AvailableDevices)}.");
            try
            {
                ValidateExpertOptions(item.Options.AdditionalArguments, capability, errors);
                ValidateCommandOptions(plan, item, runtime, capability, errors);
                ValidateDevices(item.Options.Devices, runtime, capability, errors);
            }
            catch (InvalidOperationException ex) { errors.Add(ex.Message); }
        }
        foreach (var item in preview.WorkItems
                     .GroupBy(item => $"{item.RuntimeId}|{item.ModelPath}", StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var runtime = (await runtimesTask).First(runtime => runtime.Id.Equals(item.RuntimeId, StringComparison.OrdinalIgnoreCase));
            var pathError = await _capabilities.ValidateModelPathAsync(runtime, item.WslDistro, item.ModelPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(pathError)) errors.Add($"{runtime.Name}: {pathError}");
        }
        foreach (var item in preview.WorkItems.Where(item => item.ExecutionMode == BenchmarkExecutionMode.ProfileServing))
            if (item.LaunchSettings is not null && modelsById.TryGetValue(item.ModelId, out var model))
                ValidateServingSpeculativeCompanion(item, model, errors);
        return preview with { IsValid = errors.Count == 0, Errors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), Warnings = warnings };
    }

    private static void ValidateServingSpeculativeCompanion(
        BenchmarkWorkItem item,
        ModelRecord model,
        ICollection<string> errors)
    {
        var settings = item.LaunchSettings!;
        var type = SpeculativeTypePolicy.Normalize(settings.SpeculativeType);
        if (type is "" or "none" || type.StartsWith("ngram-", StringComparison.OrdinalIgnoreCase)) return;

        if (SpeculativeTypePolicy.IsAtomicMtp(type))
        {
            var head = ModelCatalogService.ResolveMtpHeadPath(model.ModelPath, settings.MtpHeadPath, type);
            if (string.IsNullOrWhiteSpace(head) || !File.Exists(Path.GetFullPath(head)))
                errors.Add($"{item.ProfileNames.FirstOrDefault()}: no compatible atomic-MTP head was found for the '{type}' benchmark variant.");
            return;
        }

        if (!type.StartsWith("draft-", StringComparison.OrdinalIgnoreCase)) return;
        var draft = ModelCatalogService.ResolveDraftModelPath(model.ModelPath, settings.SpecDraftModelPath, type);
        var embeddedMtp = type.Equals("draft-mtp", StringComparison.OrdinalIgnoreCase)
                          && string.IsNullOrWhiteSpace(settings.SpecDraftModelPath)
                          && ModelCatalogService.HasEmbeddedDraftMtp(model.ModelPath);
        if (!embeddedMtp && (string.IsNullOrWhiteSpace(draft) || !File.Exists(Path.GetFullPath(draft))))
            errors.Add($"{item.ProfileNames.FirstOrDefault()}: no compatible companion was found for the '{type}' benchmark variant. Place it beside the main GGUF or benchmark a profile that explicitly selects it.");
    }

    private static void ValidateExpertOptions(IReadOnlyList<string> arguments, BenchmarkRuntimeCapability capability, ICollection<string> errors)
    {
        BenchmarkCommandBuilder.ValidateAdditionalArguments(arguments);
        foreach (var token in arguments.Where(token => token.StartsWith("-", StringComparison.Ordinal) && !LooksNumeric(token)))
        {
            var option = token.Split('=', 2)[0];
            if (!capability.SupportedOptions.Contains(option))
                errors.Add($"{option} is not advertised by the selected llama-bench binary.");
        }
    }

    private static void ValidateCommandOptions(
        BenchmarkPlan plan,
        BenchmarkWorkItem item,
        RuntimeRecord runtime,
        BenchmarkRuntimeCapability capability,
        ICollection<string> errors)
    {
        var visibleModelPath = BenchmarkRuntimeToolAdapter.RuntimeVisiblePath(runtime.Mode, item.ModelPath);
        var arguments = BenchmarkCommandBuilder.Build(plan, item, visibleModelPath);
        foreach (var option in arguments
                     .Where(token => token.StartsWith("-", StringComparison.Ordinal) && !LooksNumeric(token))
                     .Select(token => token.Split('=', 2)[0])
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!capability.SupportedOptions.Contains(option))
                errors.Add($"{runtime.Name}: required option {option} is not advertised by its llama-bench binary.");
        }
    }

    private static bool LooksNumeric(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static void ValidateDevices(
        IReadOnlyList<string> requested,
        RuntimeRecord runtime,
        BenchmarkRuntimeCapability capability,
        ICollection<string> errors)
    {
        if (capability.AvailableDevices.Count == 0) return;
        var available = capability.AvailableDevices.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var device in requested.SelectMany(value => value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            if (!device.Equals("none", StringComparison.OrdinalIgnoreCase) && !available.Contains(device))
                errors.Add($"{runtime.Name}: device '{device}' was not reported by llama-bench --list-devices.");
    }
}
