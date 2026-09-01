namespace LocalLlmConsole.Services;

public sealed record OverviewModelGroupLoadApplicationActions(
    Func<string, CancellationToken, Task> StopSessionAsync,
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
        ArgumentNullException.ThrowIfNull(actions.StopSessionAsync);
        ArgumentNullException.ThrowIfNull(actions.StartModelAsync);
        if (!plan.CanLoad)
            throw new InvalidOperationException("A model group cannot be executed until its preflight succeeds.");

        var pending = plan.Targets.Where(target => !target.AlreadyLoaded).ToArray();
        var startedTargets = new List<OverviewModelGroupLoadTarget>();
        try
        {
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
                    await actions.StopSessionAsync(
                        LoadedModelSessionManager.SessionIdFor(target.Model.Id, target.Profile.Id),
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    rollbackErrors.Add($"could not stop {target.Model.Name}: {ex.Message}");
                }
            }

            var rollback = rollbackErrors.Count == 0
                ? "Profiles started by this group load were stopped."
                : $"Rollback was incomplete: {string.Join("; ", rollbackErrors)}.";
            throw new InvalidOperationException(
                $"Group '{plan.Group.Name}' was not loaded: {failure.Message} {rollback}",
                failure);
        }
    }
}
