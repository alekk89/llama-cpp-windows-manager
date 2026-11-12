namespace LocalLlmConsole.Services;

public sealed class ModelReattachBlockedException(ModelRecord blockingModel, bool isSource)
    : InvalidOperationException($"Unload '{blockingModel.Name}' before reattaching or consolidating its model registration.")
{
    public ModelRecord BlockingModel { get; } = blockingModel;
    public bool IsSource { get; } = isSource;
}

/// <summary>
/// Applies a reviewed reattach inside the session lifecycle gate, so no load can start for the
/// source or a consolidated duplicate between the final loaded-state check and the database update.
/// </summary>
public sealed class ModelReattachWorkflowService
{
    private readonly ModelCatalogService _catalog;
    private readonly LoadedModelSessionManager _sessions;

    public ModelReattachWorkflowService(ModelCatalogService catalog, LoadedModelSessionManager sessions)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public Task<ModelReattachResult> ReattachAsync(
        ModelRecord model,
        ModelReattachInspection inspection,
        bool confirmRole,
        bool confirmIdentityMismatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(inspection);
        return _sessions.ExecuteLifecycleAsync(async () =>
        {
            var duplicates = await _catalog.ListReattachDuplicatesAsync(model, inspection.Path);
            if (!SameIds(duplicates, inspection.DuplicateModels))
                throw new InvalidOperationException("The registrations for the selected GGUF changed during review. Choose the file again.");

            ThrowIfLoaded(model, inspection.DuplicateModels);
            return await _catalog.ReattachFileAsync(model, inspection, confirmRole, confirmIdentityMismatch);
        }, cancellationToken);
    }

    public void ThrowIfLoaded(ModelRecord model, IEnumerable<ModelRecord> duplicates)
    {
        if (IsLoaded(model)) throw new ModelReattachBlockedException(model, isSource: true);
        var loadedDuplicate = duplicates.FirstOrDefault(IsLoaded);
        if (loadedDuplicate is not null) throw new ModelReattachBlockedException(loadedDuplicate, isSource: false);
    }

    private bool IsLoaded(ModelRecord model)
        => _sessions.SessionsForModel(model.Id).Any(session => session.IsRunning);

    private static bool SameIds(IEnumerable<ModelRecord> left, IEnumerable<ModelRecord> right)
        => left.Select(model => model.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(right.Select(model => model.Id));
}
