namespace LocalLlmConsole.Services;

public sealed partial class RuntimeDeletionPlanner
{
    public async Task<RuntimeDeletionPlan> PlanPackageDeletionAsync(RuntimePackagePreset preset, string runtimeRoot)
    {
        var allRuntimes = await _stateStore.ListRuntimesAsync();
        var runtimes = allRuntimes
            .Where(runtime => string.Equals(RuntimeMetadataService.ManagedPackageId(runtime), preset.Id, StringComparison.OrdinalIgnoreCase)
                || RuntimeMetadataService.EquivalentPackageIds(runtime).Contains(preset.Id, StringComparer.OrdinalIgnoreCase)
                || string.Equals(RuntimeMetadataService.ManagedPresetId(runtime), preset.SourcePresetId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (runtimes.Count == 0)
            return Blocked($"No local installs for that {RuntimePackageSourceCatalog.PackageRuntimeLabel(preset)}.");

        if (runtimes.Any(IsRuntimeActivelyUsed))
            return Blocked("Unload the running model before deleting the runtime it is using.");

        var modelsByRuntime = await ModelsByRuntimeAsync();
        var usedByModels = runtimes
            .Where(runtime => modelsByRuntime.TryGetValue(runtime.Id, out var models) && models.Count > 0)
            .SelectMany(runtime => modelsByRuntime[runtime.Id])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (usedByModels.Count > 0)
        {
            return Blocked(
                $"Update saved launch settings before deleting this runtime package. Used by: {string.Join(", ", usedByModels.Take(4))}.",
                usedByModels);
        }

        var folders = runtimes
            .Select(RuntimeMetadataService.Folder)
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RuntimeDeletionPlan(
            RuntimeDeletionPlanKind.DeleteFiles,
            "",
            runtimes,
            folders,
            "",
            []);
    }
}
