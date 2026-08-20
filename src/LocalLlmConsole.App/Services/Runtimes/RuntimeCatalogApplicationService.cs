namespace LocalLlmConsole.Services;

public enum RuntimeCatalogApplicationOutcome
{
    NoSelection,
    Cancelled,
    Applied
}

public sealed record RuntimeCatalogRefreshApplicationRequest(
    AppSettings Settings,
    IReadOnlyList<LoadedModelSessionSnapshot> Sessions,
    IReadOnlyDictionary<string, RuntimeUpdateState> RuntimeUpdateStates,
    IReadOnlyDictionary<string, RuntimePackageUpdateState> RuntimePackageUpdateStates);

public sealed record RuntimeCatalogRefreshApplicationResult(
    IReadOnlyList<RuntimeRecord> Runtimes,
    RuntimeCatalogViewRows Rows);

public sealed record RuntimeCatalogScanApplicationActions(
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<Task> RefreshRuntimesAsync,
    Func<Task> RefreshJobsAsync,
    Func<Task> RefreshOverviewAsync);

public sealed record RuntimeCatalogDeleteRegistrationActions(
    Func<RuntimeRecord, bool> Confirm,
    Func<Task> RefreshRuntimesAsync,
    Func<Task> RefreshOverviewAsync);

public sealed class RuntimeCatalogApplicationService
{
    private readonly StateStore _stateStore;
    private readonly RuntimeRegistryService _registry;
    private readonly RuntimeDeletionPlanner _deletion;
    private readonly RuntimeCatalogDataService _data;
    private readonly RuntimeCatalogViewService _view;

    public RuntimeCatalogApplicationService(
        StateStore stateStore,
        RuntimeRegistryService registry,
        RuntimeDeletionPlanner deletion,
        RuntimeCatalogDataService data,
        RuntimeCatalogViewService view)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _deletion = deletion ?? throw new ArgumentNullException(nameof(deletion));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public async Task DetectAndRefreshAsync(
        AppSettings settings,
        RuntimeCatalogSessionState sessionState,
        RuntimeCatalogScanApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sessionState);
        Validate(actions);

        await actions.RunBusyAsync("Detecting installed runtimes...", async () =>
        {
            await ScanAndMarkRuntimeRootAsync(settings.RuntimeRoot, sessionState);
            await actions.RefreshRuntimesAsync();
            await actions.RefreshJobsAsync();
            await actions.RefreshOverviewAsync();
        });
    }

    public async Task EnsureRuntimeRootScannedAsync(
        AppSettings settings,
        RuntimeCatalogSessionState sessionState)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sessionState);

        if (sessionState.TryMarkRuntimeRootScanned(settings.RuntimeRoot, out var root))
            await _registry.ScanAsync(root);
    }

    public async Task ScanAndMarkRuntimeRootAsync(
        string runtimeRoot,
        RuntimeCatalogSessionState sessionState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        ArgumentNullException.ThrowIfNull(sessionState);

        var root = Path.GetFullPath(runtimeRoot);
        await _registry.ScanAsync(root);
        sessionState.MarkRuntimeRootScanned(root);
    }

    public async Task<RuntimeCatalogRefreshApplicationResult> RefreshAsync(
        RuntimeCatalogRefreshApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);
        ArgumentNullException.ThrowIfNull(request.Sessions);
        ArgumentNullException.ThrowIfNull(request.RuntimeUpdateStates);
        ArgumentNullException.ThrowIfNull(request.RuntimePackageUpdateStates);

        var runtimes = await _stateStore.ListRuntimesAsync();
        if (await RuntimeEquivalenceService.ReconcileOfficialRuntimeEquivalenceAsync(_stateStore, runtimes, cancellationToken))
            runtimes = await _stateStore.ListRuntimesAsync();

        var sources = await _data.LoadSourcesAsync(request.Settings.RuntimeRoot, cancellationToken);
        var modelsByRuntime = await _deletion.ModelsByRuntimeAsync();
        var rows = await Task.Run(() => _view.BuildRows(_data.BuildViewRequest(new RuntimeCatalogDataRequest(
                request.Settings.RuntimeRoot,
                runtimes,
                sources,
                modelsByRuntime,
                request.Sessions,
                request.RuntimeUpdateStates,
                request.RuntimePackageUpdateStates))),
            cancellationToken);
        return new RuntimeCatalogRefreshApplicationResult(runtimes, rows);
    }

    public async Task<RuntimeCatalogApplicationOutcome> DeleteRegistrationAsync(
        RuntimeRecord? runtime,
        RuntimeCatalogDeleteRegistrationActions actions)
    {
        Validate(actions);

        if (runtime is null)
            return RuntimeCatalogApplicationOutcome.NoSelection;

        if (!actions.Confirm(runtime))
            return RuntimeCatalogApplicationOutcome.Cancelled;

        await _stateStore.DeleteRuntimeAsync(runtime.Id);
        await actions.RefreshRuntimesAsync();
        await actions.RefreshOverviewAsync();
        return RuntimeCatalogApplicationOutcome.Applied;
    }

    private static void Validate(RuntimeCatalogScanApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshRuntimesAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshJobsAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshOverviewAsync);
    }

    private static void Validate(RuntimeCatalogDeleteRegistrationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.Confirm);
        ArgumentNullException.ThrowIfNull(actions.RefreshRuntimesAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshOverviewAsync);
    }
}
