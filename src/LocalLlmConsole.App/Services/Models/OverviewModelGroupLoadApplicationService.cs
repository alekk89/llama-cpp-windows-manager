namespace LocalLlmConsole.Services;

public sealed record OverviewModelGroupLoadApplicationActions(
    Func<ModelRecord, CancellationToken, Task> StopModelAsync,
    Func<RuntimeRecord, ModelRecord, AppSettings, string, string, CancellationToken, Task<bool>> StartModelAsync);

public sealed class OverviewModelGroupLoadApplicationService
{
    public async Task<int> ExecuteAsync(
        OverviewModelGroupLoadPlan plan,
        IReadOnlyList<LoadedModelSessionSnapshot> sessions,
        IReadOnlyList<ModelRecord> models,
        IReadOnlyList<RuntimeRecord> runtimes,
        OverviewModelGroupLoadApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(runtimes);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.StopModelAsync);
        ArgumentNullException.ThrowIfNull(actions.StartModelAsync);
        if (!plan.CanLoad)
            throw new InvalidOperationException("A model group cannot be executed until its preflight succeeds.");

        var pending = plan.Targets.Where(target => !target.AlreadyLoaded).ToArray();
        var originals = pending
            .Select(target => sessions.FirstOrDefault(session => session.IsRunning
                && session.ModelId.Equals(target.Model.Id, StringComparison.OrdinalIgnoreCase)))
            .Where(session => session is not null)
            .Cast<LoadedModelSessionSnapshot>()
            .ToArray();
        var modelsById = models.ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        var runtimesById = runtimes.ToDictionary(runtime => runtime.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var original in originals)
        {
            if (!modelsById.ContainsKey(original.ModelId))
                throw new InvalidOperationException($"Cannot preserve the running model '{original.ModelName}' because its registration is missing.");
            if (!runtimesById.ContainsKey(original.RuntimeId))
                throw new InvalidOperationException($"Cannot preserve the running model '{original.ModelName}' because runtime '{original.RuntimeName}' is missing.");
        }

        var stoppedOriginals = new List<LoadedModelSessionSnapshot>();
        var startedTargets = new List<OverviewModelGroupLoadTarget>();
        try
        {
            // Release every replacement port before starting anything. This makes
            // cross-profile port swaps deterministic instead of order-dependent.
            foreach (var original in originals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await actions.StopModelAsync(modelsById[original.ModelId], cancellationToken);
                stoppedOriginals.Add(original);
            }

            foreach (var target in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var launched = await actions.StartModelAsync(
                    target.Runtime,
                    target.Model,
                    target.LaunchSettings,
                    target.Profile.Id,
                    target.Profile.Name,
                    cancellationToken);
                if (!launched)
                    throw new InvalidOperationException($"{target.Model.Name} did not start.");
                startedTargets.Add(target);
            }

            return pending.Length;
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<string>();
            foreach (var target in startedTargets.AsEnumerable().Reverse())
            {
                try
                {
                    await actions.StopModelAsync(target.Model, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    rollbackErrors.Add($"could not stop {target.Model.Name}: {ex.Message}");
                }
            }

            foreach (var original in stoppedOriginals)
            {
                try
                {
                    var restored = await actions.StartModelAsync(
                        runtimesById[original.RuntimeId],
                        modelsById[original.ModelId],
                        original.LaunchSettings,
                        original.LaunchProfileId,
                        original.LaunchProfileName,
                        CancellationToken.None);
                    if (!restored)
                        rollbackErrors.Add($"could not restore {original.ModelName}");
                }
                catch (Exception ex)
                {
                    rollbackErrors.Add($"could not restore {original.ModelName}: {ex.Message}");
                }
            }

            var rollback = rollbackErrors.Count == 0
                ? "The previous running profiles were restored."
                : $"Rollback was incomplete: {string.Join("; ", rollbackErrors)}.";
            throw new InvalidOperationException(
                $"Group '{plan.Group.Name}' was not loaded: {failure.Message} {rollback}",
                failure);
        }
    }
}
