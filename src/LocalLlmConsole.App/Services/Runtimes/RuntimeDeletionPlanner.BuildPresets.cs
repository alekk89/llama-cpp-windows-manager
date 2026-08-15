namespace LocalLlmConsole.Services;

public sealed partial class RuntimeDeletionPlanner
{
    public async Task<RuntimeBuildPresetDeletionPlan> PlanBuildPresetDeletionAsync(
        RuntimeBuildPreset preset,
        string runtimeRoot,
        IEnumerable<RuntimeSourceEntry> allSources)
    {
        var runtimes = (await _stateStore.ListRuntimesAsync())
            .Where(runtime => RuntimeMetadataService.IsManagedSourceBuild(runtime)
                && string.Equals(RuntimeMetadataService.ManagedPresetId(runtime), preset.Id, StringComparison.OrdinalIgnoreCase))
            .GroupBy(runtime => RuntimeMetadataService.Folder(runtime), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(runtime => runtime.UpdatedAt).First())
            .ToList();
        var sources = allSources
            .Where(source => string.Equals(source.PresetId, preset.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var sourceFolders = new HashSet<string>(
            sources
                .Select(source => source.SourceDir)
                .Where(folder => !string.IsNullOrWhiteSpace(folder) && RuntimeFileService.IsSafeRuntimeFolder(runtimeRoot, folder)),
            StringComparer.OrdinalIgnoreCase);
        var defaultSourceDir = RuntimeBuildCatalogService.SourceDir(runtimeRoot, preset);
        var hasPartialSourceCache = Directory.Exists(defaultSourceDir)
            && RuntimeFileService.IsSafeRuntimeFolder(runtimeRoot, defaultSourceDir)
            && sourceFolders.Add(defaultSourceDir);

        if (runtimes.Count == 0 && sourceFolders.Count == 0)
        {
            if (!preset.Custom)
                return BuildPresetBlocked(preset, "No local builds or downloaded sources for that repository.");

            return new RuntimeBuildPresetDeletionPlan(
                RuntimeBuildPresetDeletionPlanKind.RemoveCustomRepository,
                "",
                preset,
                [],
                [],
                [],
                [],
                false,
                RemoveCustomRepository: true,
                []);
        }

        if (runtimes.Any(IsRuntimeActivelyUsed))
            return BuildPresetBlocked(preset, "Unload the running model before deleting the runtime it is using.");

        var modelsByRuntime = await ModelsByRuntimeAsync();
        var usedByModels = runtimes
            .Where(runtime => modelsByRuntime.TryGetValue(runtime.Id, out var models) && models.Count > 0)
            .SelectMany(runtime => modelsByRuntime[runtime.Id])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (usedByModels.Count > 0)
        {
            return BuildPresetBlocked(
                preset,
                $"Update saved launch settings before deleting this runtime repository. Used by: {string.Join(", ", usedByModels.Take(4))}.",
                usedByModels);
        }

        var runtimeFolders = runtimes
            .Select(runtime => RuntimeFileService.CanDeleteRuntimeFiles(runtime, runtimeRoot, out var folder, out _) ? folder : "")
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RuntimeBuildPresetDeletionPlan(
            RuntimeBuildPresetDeletionPlanKind.DeleteBuildsAndSources,
            "",
            preset,
            runtimes,
            sources,
            runtimeFolders,
            sourceFolders.ToList(),
            hasPartialSourceCache,
            preset.Custom,
            []);
    }

    private static RuntimeBuildPresetDeletionPlan BuildPresetBlocked(
        RuntimeBuildPreset preset,
        string message,
        IReadOnlyList<string>? modelNames = null)
        => new(
            RuntimeBuildPresetDeletionPlanKind.Blocked,
            message,
            preset,
            [],
            [],
            [],
            [],
            false,
            false,
            modelNames ?? []);
}
