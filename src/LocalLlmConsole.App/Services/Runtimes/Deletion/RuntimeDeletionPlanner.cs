namespace LocalLlmConsole.Services;

public enum RuntimeDeletionPlanKind
{
    Blocked,
    RegistrationOnly,
    DeleteFiles
}

public sealed record RuntimeDeletionPlan(
    RuntimeDeletionPlanKind Kind,
    string StatusMessage,
    IReadOnlyList<RuntimeRecord> Runtimes,
    IReadOnlyList<string> Folders,
    string Reason,
    IReadOnlyList<string> BlockingModelNames,
    IReadOnlyList<RuntimeProfileReassignment>? ProfileReassignments = null)
{
    public bool CanDelete => Kind != RuntimeDeletionPlanKind.Blocked;
    public IReadOnlyList<RuntimeProfileReassignment> Reassignments => ProfileReassignments ?? [];
}

public sealed record RuntimeProfileReassignment(
    string ProfileId,
    string ModelId,
    string ModelName,
    string OldRuntimeId,
    string ReplacementRuntimeId,
    string ReplacementRuntimeName);

public enum RuntimeSourceDeletionPlanKind
{
    Blocked,
    DeleteSourceFolder
}

public sealed record RuntimeSourceDeletionPlan(
    RuntimeSourceDeletionPlanKind Kind,
    string StatusMessage,
    RuntimeSourceEntry Source,
    string SourceDir)
{
    public bool CanDelete => Kind != RuntimeSourceDeletionPlanKind.Blocked;
}

public enum RuntimeBuildPresetDeletionPlanKind
{
    Blocked,
    RemoveCustomRepository,
    DeleteBuildsAndSources
}

public sealed record RuntimeBuildPresetDeletionPlan(
    RuntimeBuildPresetDeletionPlanKind Kind,
    string StatusMessage,
    RuntimeBuildPreset Preset,
    IReadOnlyList<RuntimeRecord> Runtimes,
    IReadOnlyList<RuntimeSourceEntry> Sources,
    IReadOnlyList<string> RuntimeFolders,
    IReadOnlyList<string> SourceFolders,
    bool HasPartialSourceCache,
    bool RemoveCustomRepository,
    IReadOnlyList<string> BlockingModelNames)
{
    public bool CanDelete => Kind != RuntimeBuildPresetDeletionPlanKind.Blocked;
}

public sealed partial class RuntimeDeletionPlanner
{
    private sealed record RuntimeProfileReference(ModelRecord Model, NamedModelLaunchProfile Profile);

    private readonly StateStore _stateStore;
    private readonly ModelLaunchProfileService _launchProfiles;
    private readonly LoadedModelSessionManager _sessions;

    public RuntimeDeletionPlanner(
        StateStore stateStore,
        ModelLaunchProfileService launchProfiles,
        LoadedModelSessionManager sessions)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _launchProfiles = launchProfiles ?? throw new ArgumentNullException(nameof(launchProfiles));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public bool IsRuntimeActivelyUsed(RuntimeRecord runtime)
        => _sessions.Snapshots().Any(session => session.IsRunning
            && string.Equals(session.RuntimeId, runtime.Id, StringComparison.OrdinalIgnoreCase));

    public async Task<Dictionary<string, List<string>>> ModelsByRuntimeAsync()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in await _stateStore.ListModelsAsync())
        {
            foreach (var profile in await _launchProfiles.ListNamedAsync(model))
            {
                if (string.IsNullOrWhiteSpace(profile.Settings.RuntimeId)) continue;
                if (!map.TryGetValue(profile.Settings.RuntimeId, out var models))
                {
                    models = [];
                    map[profile.Settings.RuntimeId] = models;
                }
                if (!models.Contains(model.Name, StringComparer.OrdinalIgnoreCase))
                    models.Add(model.Name);
            }
        }

        foreach (var models in map.Values)
            models.Sort(StringComparer.OrdinalIgnoreCase);

        return map;
    }

    public async Task<RuntimeDeletionPlan> PlanRuntimeDeletionAsync(RuntimeRecord runtime, string runtimeRoot)
    {
        if (IsRuntimeActivelyUsed(runtime))
            return Blocked("Unload the model before deleting the runtime it is using.");

        var profileReferences = await ProfileReferencesForRuntimeAsync(runtime.Id);
        var reassignments = new List<RuntimeProfileReassignment>();
        if (profileReferences.Count > 0)
        {
            var replacement = await ReplacementRuntimeAsync(runtime);
            if (replacement is null)
            {
                var modelNames = profileReferences
                    .Select(reference => reference.Model.Name)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return Blocked(
                    $"Register another runtime before deleting this one. It is used by: {string.Join(", ", modelNames)}.",
                    modelNames);
            }

            reassignments = profileReferences
                .OrderBy(reference => reference.Model.Name, StringComparer.OrdinalIgnoreCase)
                .Select(reference => new RuntimeProfileReassignment(
                    reference.Profile.Id,
                    reference.Model.Id,
                    reference.Model.Name,
                    runtime.Id,
                    replacement.Id,
                    replacement.Name))
                .ToList();
        }

        var filePlan = await Task.Run(() =>
        {
            var canDelete = RuntimeFileService.CanDeleteRuntimeFiles(runtime, runtimeRoot, out var folder, out var reason);
            return (CanDelete: canDelete, Folder: folder, Reason: reason);
        });
        if (!filePlan.CanDelete)
        {
            return new RuntimeDeletionPlan(
                RuntimeDeletionPlanKind.RegistrationOnly,
                "",
                [runtime],
                string.IsNullOrWhiteSpace(filePlan.Folder) ? [] : [filePlan.Folder],
                filePlan.Reason,
                [],
                reassignments);
        }

        return new RuntimeDeletionPlan(
            RuntimeDeletionPlanKind.DeleteFiles,
            "",
            [runtime],
            [filePlan.Folder],
            "",
            [],
            reassignments);
    }

    private static RuntimeDeletionPlan Blocked(string message, IReadOnlyList<string>? modelNames = null)
        => new(
            RuntimeDeletionPlanKind.Blocked,
            message,
            [],
            [],
            "",
            modelNames ?? []);

    private async Task<IReadOnlyList<RuntimeProfileReference>> ProfileReferencesForRuntimeAsync(string runtimeId)
    {
        var references = new List<RuntimeProfileReference>();
        foreach (var model in await _stateStore.ListModelsAsync())
        {
            foreach (var profile in await _launchProfiles.ListNamedAsync(model))
            {
                if (string.Equals(profile.Settings.RuntimeId, runtimeId, StringComparison.OrdinalIgnoreCase))
                    references.Add(new RuntimeProfileReference(model, profile));
            }
        }

        return references;
    }

    private async Task<RuntimeRecord?> ReplacementRuntimeAsync(RuntimeRecord deletedRuntime)
    {
        var runtimes = (await _stateStore.ListRuntimesAsync())
            .Where(runtime => !string.Equals(runtime.Id, deletedRuntime.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return runtimes
            .OrderBy(runtime => ReplacementScore(deletedRuntime, runtime))
            .ThenBy(runtime => runtime.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int ReplacementScore(RuntimeRecord deletedRuntime, RuntimeRecord candidate)
    {
        if (candidate.Mode == deletedRuntime.Mode && candidate.Backend == deletedRuntime.Backend) return 0;
        if (candidate.Backend == deletedRuntime.Backend) return 1;
        if (candidate.Mode == deletedRuntime.Mode) return 2;
        return 3;
    }
}
