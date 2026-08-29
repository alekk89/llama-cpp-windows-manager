namespace LocalLlmConsole.Services;

public sealed class RuntimeDeletionExecutorService
{
    private readonly StateStore _stateStore;

    public RuntimeDeletionExecutorService(StateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public async Task DeleteRuntimeSourceAsync(
        RuntimeSourceDeletionPlan plan,
        string runtimeRoot,
        CancellationToken cancellationToken = default)
    {
        if (!plan.CanDelete) return;
        if (Directory.Exists(plan.SourceDir))
            await RuntimeFileService.DeleteSafeRuntimeFolderAsync(runtimeRoot, plan.SourceDir, cancellationToken);
    }

    public async Task DeleteRuntimeAsync(
        RuntimeDeletionPlan plan,
        string runtimeRoot,
        CancellationToken cancellationToken = default)
    {
        if (!plan.CanDelete) return;

        await ReassignRuntimeProfilesAsync(plan.Reassignments);

        foreach (var runtime in plan.Runtimes)
            await _stateStore.DeleteRuntimeAsync(runtime.Id);

        if (plan.Kind != RuntimeDeletionPlanKind.DeleteFiles) return;
        foreach (var folder in plan.Folders)
        {
            if (Directory.Exists(folder))
                await RuntimeFileService.DeleteRuntimeFilesAsync(runtimeRoot, folder, cancellationToken);
        }
    }

    public async Task DeletePackageAsync(
        RuntimeDeletionPlan plan,
        string runtimeRoot,
        CancellationToken cancellationToken = default)
    {
        if (!plan.CanDelete) return;

        foreach (var runtime in plan.Runtimes)
            await _stateStore.DeleteRuntimeAsync(runtime.Id);

        foreach (var folder in plan.Folders)
        {
            if (Directory.Exists(folder))
                await RuntimeFileService.DeleteSafeRuntimeFolderAsync(runtimeRoot, folder, cancellationToken);
        }
    }

    public async Task DeleteBuildPresetAsync(
        RuntimeBuildPresetDeletionPlan plan,
        string runtimeRoot,
        CancellationToken cancellationToken = default)
    {
        if (!plan.CanDelete) return;

        foreach (var runtime in plan.Runtimes)
        {
            await _stateStore.DeleteRuntimeAsync(runtime.Id);
            var deletionTarget = await Task.Run(() =>
            {
                var canDelete = RuntimeFileService.CanDeleteRuntimeFiles(runtime, runtimeRoot, out var folder, out _);
                return (CanDelete: canDelete, Folder: folder);
            }, cancellationToken);
            if (deletionTarget.CanDelete)
                await RuntimeFileService.DeleteRuntimeFilesAsync(runtimeRoot, deletionTarget.Folder, cancellationToken);
        }

        foreach (var sourceDir in plan.SourceFolders)
        {
            if (Directory.Exists(sourceDir))
                await RuntimeFileService.DeleteSafeRuntimeFolderAsync(runtimeRoot, sourceDir, cancellationToken);
        }

        if (plan.RemoveCustomRepository)
            await RemoveCustomRuntimeRepositoryAsync(plan.Preset, runtimeRoot);
    }

    private static async Task RemoveCustomRuntimeRepositoryAsync(RuntimeBuildPreset preset, string runtimeRoot)
    {
        var customPresets = RuntimeBuildCatalogService.ReadCustomPresets(runtimeRoot)
            .Where(candidate => !string.Equals(candidate.Id, preset.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await RuntimeBuildCatalogService.SaveCustomPresetsAsync(runtimeRoot, customPresets);
    }

    private async Task ReassignRuntimeProfilesAsync(IReadOnlyList<RuntimeProfileReassignment> reassignments)
    {
        foreach (var reassignment in reassignments)
        {
            var profile = await _stateStore.GetNamedModelLaunchProfileAsync(reassignment.ProfileId);
            if (profile is null) continue;
            if (!string.Equals(profile.Settings.RuntimeId, reassignment.OldRuntimeId, StringComparison.OrdinalIgnoreCase)) continue;
            await _stateStore.SaveNamedModelLaunchProfileAsync(profile with
            {
                Settings = profile.Settings with { RuntimeId = reassignment.ReplacementRuntimeId },
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
    }
}
