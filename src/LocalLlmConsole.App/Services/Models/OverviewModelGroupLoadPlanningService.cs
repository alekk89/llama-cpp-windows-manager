namespace LocalLlmConsole.Services;

public sealed record OverviewModelGroupLoadTarget(
    ModelRecord Model,
    NamedModelLaunchProfile Profile,
    RuntimeRecord Runtime,
    AppSettings LaunchSettings,
    bool AlreadyLoaded);

public sealed record OverviewModelGroupLoadPlan(
    ModelGroupRecord Group,
    IReadOnlyList<OverviewModelGroupLoadTarget> Targets,
    IReadOnlyList<string> Errors,
    double EstimatedRequiredGiB,
    double AvailableGiB)
{
    public bool CanLoad => Errors.Count == 0;
}

public sealed class OverviewModelGroupLoadPlanningService
{
    private const double SafetyReserveGiB = 1.0;

    public OverviewModelGroupLoadPlan Plan(
        ModelGroupRecord group,
        ModelGroupSnapshot groupSnapshot,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        IReadOnlyList<ModelRecord> models,
        IReadOnlyList<RuntimeRecord> runtimes,
        IReadOnlyList<LoadedModelSessionSnapshot> sessions,
        AppSettings defaults,
        VramMemorySnapshot? memory)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(groupSnapshot);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(runtimes);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(defaults);

        var errors = new List<string>();
        var assignedProfileIds = groupSnapshot.Assignments.Values
            .Where(assignment => assignment.GroupId.Equals(group.Id, StringComparison.OrdinalIgnoreCase))
            .Select(assignment => assignment.LaunchProfileId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedProfiles = profiles
            .Where(profile => assignedProfileIds.Contains(profile.Id))
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selectedProfiles.Length == 0)
            errors.Add($"Group '{group.Name}' has no launch profiles. Add profiles to the group before loading it.");

        var targets = new List<OverviewModelGroupLoadTarget>();
        foreach (var profile in selectedProfiles)
        {
            var model = models.FirstOrDefault(candidate => candidate.Id.Equals(profile.ModelId, StringComparison.OrdinalIgnoreCase));
            if (model is null)
            {
                errors.Add($"Launch profile '{profile.Name}' refers to a model that is no longer registered.");
                continue;
            }

            var runtime = ResolveRuntime(runtimes, profile.Settings.RuntimeId);
            if (runtime is null)
            {
                errors.Add(string.IsNullOrWhiteSpace(profile.Settings.RuntimeId)
                    ? $"No available runtime can load '{profile.Name}'."
                    : $"The saved runtime for '{profile.Name}' is missing or unavailable.");
                continue;
            }

            var launchSettings = profile.Settings.ApplyTo(defaults);
            var existing = sessions.FirstOrDefault(session =>
                session.IsRunning
                && session.ModelId.Equals(model.Id, StringComparison.OrdinalIgnoreCase)
                && session.LaunchProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
            targets.Add(new OverviewModelGroupLoadTarget(
                model,
                profile,
                runtime,
                launchSettings,
                existing is not null && existing.LaunchProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)));
        }

        AddPortErrors(group, targets, sessions, errors);

        var pendingGpuTargets = targets.Where(target => !target.AlreadyLoaded && IsGpuLaunch(target.Runtime, target.LaunchSettings)).ToArray();
        var estimatedRequired = pendingGpuTargets.Sum(target => VramAdmissionService.EstimateRequiredGiB(target.Model, target.LaunchSettings));
        var available = memory is null ? 0 : Math.Max(0, memory.FreeGiB - SafetyReserveGiB);

        if (pendingGpuTargets.Length > 0 && memory is null)
            errors.Add($"Available VRAM could not be measured, so group '{group.Name}' was not started. Load the profiles individually or restore GPU telemetry.");
        else if (estimatedRequired > available)
            errors.Add($"Not enough VRAM is available to load all models in group '{group.Name}'. About {estimatedRequired:0.0} GiB is required and {available:0.0} GiB is available after the safety reserve.");

        return new OverviewModelGroupLoadPlan(group, targets, errors, estimatedRequired, available);
    }

    private static RuntimeRecord? ResolveRuntime(IReadOnlyList<RuntimeRecord> runtimes, string runtimeId)
    {
        if (!string.IsNullOrWhiteSpace(runtimeId))
            return runtimes.FirstOrDefault(runtime => runtime.Id.Equals(runtimeId, StringComparison.OrdinalIgnoreCase)
                && RuntimeAvailabilityService.IsAvailable(runtime));
        return runtimes.FirstOrDefault(RuntimeAvailabilityService.IsAvailable);
    }

    private static void AddPortErrors(
        ModelGroupRecord group,
        IReadOnlyList<OverviewModelGroupLoadTarget> targets,
        IReadOnlyList<LoadedModelSessionSnapshot> sessions,
        ICollection<string> errors)
    {
        foreach (var duplicate in targets.GroupBy(target => target.LaunchSettings.Port).Where(items => items.Count() > 1))
            errors.Add($"Group '{group.Name}' assigns port {duplicate.Key} to more than one launch profile.");

        foreach (var target in targets.Where(target => !target.AlreadyLoaded))
        {
            var conflict = sessions.FirstOrDefault(session => session.IsRunning
                && session.LaunchSettings.Port == target.LaunchSettings.Port);
            if (conflict is not null)
                errors.Add($"Port {target.LaunchSettings.Port} for '{target.Profile.Name}' is already used by {conflict.ModelName}.");
        }
    }

    private static bool IsGpuLaunch(RuntimeRecord runtime, AppSettings settings)
        => IsGpuLaunch(runtime.Backend, settings);

    private static bool IsGpuLaunch(RuntimeBackend backend, AppSettings settings)
        => backend is RuntimeBackend.Cuda or RuntimeBackend.Vulkan or RuntimeBackend.Sycl or RuntimeBackend.Rocm
           && settings.GpuLayers != 0;
}
