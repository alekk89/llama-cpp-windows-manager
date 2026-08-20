namespace LocalLlmConsole.Services;

public enum RuntimeBuildDeletionApplicationOutcome
{
    Blocked,
    Cancelled,
    Deleted
}

public enum RuntimeBuildDeletionConfirmationKind
{
    RuntimeRegistration,
    RuntimeFiles,
    RuntimeSource,
    CustomRepository,
    PresetBuilds
}

public sealed record RuntimeBuildDeletionConfirmation(
    RuntimeBuildDeletionConfirmationKind Kind,
    string Title,
    string Message);

public sealed record RuntimeBuildDeletionApplicationActions(
    Func<RuntimeBuildDeletionConfirmation, bool> Confirm,
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<Task> RefreshRuntimesAsync,
    Func<Task> RefreshOverviewAsync,
    Action<string> SetStatus);

public sealed class RuntimeBuildDeletionApplicationService
{
    private readonly RuntimeDeletionPlanner _deletionPlanner;
    private readonly RuntimeDeletionExecutorService _deletionExecutor;
    private readonly RuntimeCatalogDataService _catalogData;

    public RuntimeBuildDeletionApplicationService(
        RuntimeDeletionPlanner deletionPlanner,
        RuntimeDeletionExecutorService deletionExecutor,
        RuntimeCatalogDataService catalogData)
    {
        _deletionPlanner = deletionPlanner ?? throw new ArgumentNullException(nameof(deletionPlanner));
        _deletionExecutor = deletionExecutor ?? throw new ArgumentNullException(nameof(deletionExecutor));
        _catalogData = catalogData ?? throw new ArgumentNullException(nameof(catalogData));
    }

    public async Task<RuntimeBuildDeletionApplicationOutcome> DeleteRuntimeAsync(
        RuntimeRecord runtime,
        AppSettings settings,
        RuntimeBuildDeletionApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Validate(settings, actions);

        var plan = await _deletionPlanner.PlanRuntimeDeletionAsync(runtime, settings.RuntimeRoot);
        if (!plan.CanDelete)
            return Blocked(plan.StatusMessage, actions);

        var folder = plan.Folders.FirstOrDefault() ?? "";
        var reassignmentMessage = RuntimeReassignmentMessage(plan);
        if (plan.Kind == RuntimeDeletionPlanKind.RegistrationOnly)
        {
            if (!actions.Confirm(new RuntimeBuildDeletionConfirmation(
                    RuntimeBuildDeletionConfirmationKind.RuntimeRegistration,
                    "Delete runtime",
                    $"Delete this runtime registration?{Environment.NewLine}{Environment.NewLine}{runtime.Name}{Environment.NewLine}{runtime.ExecutablePath}{Environment.NewLine}{Environment.NewLine}{plan.Reason}{reassignmentMessage}")))
                return RuntimeBuildDeletionApplicationOutcome.Cancelled;

            await _deletionExecutor.DeleteRuntimeAsync(plan, settings.RuntimeRoot);
            await actions.RefreshRuntimesAsync();
            await actions.RefreshOverviewAsync();
            actions.SetStatus($"Deleted runtime registration for {runtime.Name}. Runtime files were not deleted.");
            return RuntimeBuildDeletionApplicationOutcome.Deleted;
        }

        if (!actions.Confirm(new RuntimeBuildDeletionConfirmation(
                RuntimeBuildDeletionConfirmationKind.RuntimeFiles,
                "Delete runtime",
                $"Delete runtime files and remove this runtime?{Environment.NewLine}{Environment.NewLine}{runtime.Name}{Environment.NewLine}{folder}{reassignmentMessage}")))
            return RuntimeBuildDeletionApplicationOutcome.Cancelled;

        await actions.RunBusyAsync("Deleting runtime build...", async () =>
        {
            await _deletionExecutor.DeleteRuntimeAsync(plan, settings.RuntimeRoot);
            await actions.RefreshRuntimesAsync();
            await actions.RefreshOverviewAsync();
        });
        return RuntimeBuildDeletionApplicationOutcome.Deleted;
    }

    private static string RuntimeReassignmentMessage(RuntimeDeletionPlan plan)
    {
        if (plan.Reassignments.Count == 0) return "";

        var modelList = string.Join(Environment.NewLine, plan.Reassignments.Select(reassignment => $"- {reassignment.ModelName}"));
        var replacement = plan.Reassignments
            .Select(reassignment => reassignment.ReplacementRuntimeName)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "another runtime";
        return $"{Environment.NewLine}{Environment.NewLine}Saved model launch settings that use this runtime will be moved to:{Environment.NewLine}{replacement}{Environment.NewLine}{Environment.NewLine}Affected models:{Environment.NewLine}{modelList}";
    }

    public async Task<RuntimeBuildDeletionApplicationOutcome> DeleteSourceAsync(
        RuntimeSourceEntry source,
        AppSettings settings,
        RuntimeBuildDeletionApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(source);
        Validate(settings, actions);

        var plan = await _deletionPlanner.PlanRuntimeSourceDeletionAsync(source, settings.RuntimeRoot);
        if (!plan.CanDelete)
            return Blocked(plan.StatusMessage, actions);

        if (!actions.Confirm(new RuntimeBuildDeletionConfirmation(
                RuntimeBuildDeletionConfirmationKind.RuntimeSource,
                "Delete downloaded source",
                $"Delete downloaded source?{Environment.NewLine}{Environment.NewLine}{source.Label}{Environment.NewLine}{source.SourceDir}")))
            return RuntimeBuildDeletionApplicationOutcome.Cancelled;

        await actions.RunBusyAsync("Deleting downloaded source...", async () =>
        {
            await _deletionExecutor.DeleteRuntimeSourceAsync(plan, settings.RuntimeRoot);
            await actions.RefreshRuntimesAsync();
            await actions.RefreshOverviewAsync();
        });
        return RuntimeBuildDeletionApplicationOutcome.Deleted;
    }

    public async Task<RuntimeBuildDeletionApplicationOutcome> DeletePresetBuildsAsync(
        RuntimeBuildPreset preset,
        AppSettings settings,
        RuntimeBuildDeletionApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(preset);
        Validate(settings, actions);

        var plan = await _deletionPlanner.PlanBuildPresetDeletionAsync(preset, settings.RuntimeRoot, _catalogData.Sources(settings.RuntimeRoot));
        if (!plan.CanDelete)
            return Blocked(plan.StatusMessage, actions);

        if (plan.Kind == RuntimeBuildPresetDeletionPlanKind.RemoveCustomRepository)
        {
            if (!actions.Confirm(new RuntimeBuildDeletionConfirmation(
                    RuntimeBuildDeletionConfirmationKind.CustomRepository,
                    "Remove custom repository",
                    $"Remove this custom repository from the list?{Environment.NewLine}{Environment.NewLine}{preset.Label}{Environment.NewLine}{preset.RepoUrl}")))
                return RuntimeBuildDeletionApplicationOutcome.Cancelled;

            await _deletionExecutor.DeleteBuildPresetAsync(plan, settings.RuntimeRoot);
            await actions.RefreshRuntimesAsync();
            return RuntimeBuildDeletionApplicationOutcome.Deleted;
        }

        var customNote = preset.Custom ? $"{Environment.NewLine}{Environment.NewLine}This will also remove the custom repository from the list." : "";
        var partialNote = plan.HasPartialSourceCache ? $"{Environment.NewLine}Partial source cache: 1" : "";
        var message = $"Delete all local builds and downloaded sources from this repository?{Environment.NewLine}{Environment.NewLine}{preset.Label}{Environment.NewLine}{Environment.NewLine}Built runtimes: {plan.Runtimes.Count}{Environment.NewLine}Downloaded sources: {plan.Sources.Count}{partialNote}{customNote}";
        if (!actions.Confirm(new RuntimeBuildDeletionConfirmation(
                RuntimeBuildDeletionConfirmationKind.PresetBuilds,
                "Delete all runtime builds",
                message)))
            return RuntimeBuildDeletionApplicationOutcome.Cancelled;

        await actions.RunBusyAsync("Deleting repository builds...", async () =>
        {
            await _deletionExecutor.DeleteBuildPresetAsync(plan, settings.RuntimeRoot);
            await actions.RefreshRuntimesAsync();
            await actions.RefreshOverviewAsync();
        });
        return RuntimeBuildDeletionApplicationOutcome.Deleted;
    }

    private static RuntimeBuildDeletionApplicationOutcome Blocked(
        string statusMessage,
        RuntimeBuildDeletionApplicationActions actions)
    {
        actions.SetStatus(statusMessage);
        return RuntimeBuildDeletionApplicationOutcome.Blocked;
    }

    private static void Validate(
        AppSettings settings,
        RuntimeBuildDeletionApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.Confirm);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshRuntimesAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshOverviewAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
