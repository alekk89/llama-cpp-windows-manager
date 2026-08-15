namespace LocalLlmConsole.Services;

public sealed record RuntimeCatalogDataRequest(
    string RuntimeRoot,
    IReadOnlyList<RuntimeRecord> Runtimes,
    IReadOnlyList<RuntimeSourceEntry> Sources,
    IReadOnlyDictionary<string, List<string>> ModelsByRuntime,
    IReadOnlyList<LoadedModelSessionSnapshot> Sessions,
    IReadOnlyDictionary<string, RuntimeUpdateState> RuntimeUpdateStates,
    IReadOnlyDictionary<string, RuntimePackageUpdateState> RuntimePackageUpdateStates);

public sealed record RuntimeBuildPresetLocalState(
    IReadOnlyList<RuntimeSourceEntry> DownloadedSources,
    IReadOnlyList<RuntimeRecord> InstalledRuntimes,
    string LocalCommit,
    RuntimeUpdateState? UpdateState)
{
    public int LocalCount => DownloadedSources.Count + InstalledRuntimes.Count;
    public bool CommitUnavailable => LocalCount > 0 && string.IsNullOrWhiteSpace(LocalCommit);
    public bool CanDownload => RuntimeBuildCatalogService.CanDownloadPreset(InstalledRuntimes, DownloadedSources, UpdateState);
    public string DownloadAction => RuntimeBuildCatalogService.DownloadButtonLabel(DownloadedSources, InstalledRuntimes, UpdateState);
}

public sealed class RuntimeCatalogDataService
{
    public IReadOnlyList<RuntimePackagePreset> PackagePresets()
        => RuntimePackageSourceCatalog.PresetRows();

    public IReadOnlyList<RuntimeBuildPreset> BuildPresets(string runtimeRoot)
        => RuntimeBuildCatalogService.PresetRows(runtimeRoot);

    public IEnumerable<RuntimeSourceEntry> Sources(string runtimeRoot)
        => RuntimeBuildCatalogService.Sources(runtimeRoot);

    public async Task<IReadOnlyList<RuntimeSourceEntry>> LoadSourcesAsync(string runtimeRoot, CancellationToken cancellationToken = default)
        => await Task.Run(() => Sources(runtimeRoot).ToList(), cancellationToken);

    public string SourceDir(string runtimeRoot, RuntimeBuildPreset preset)
        => RuntimeBuildCatalogService.SourceDir(runtimeRoot, preset);

    public RuntimeCatalogViewRequest BuildViewRequest(RuntimeCatalogDataRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(
            request.Runtimes,
            request.Sources,
            BuildPresets(request.RuntimeRoot),
            PackagePresets(),
            request.ModelsByRuntime,
            ActiveRuntimeIds(request.Sessions),
            request.RuntimeUpdateStates,
            request.RuntimePackageUpdateStates);
    }

    public static RuntimeBuildPresetLocalState BuildPresetLocalState(
        RuntimeBuildPreset preset,
        IReadOnlyList<RuntimeRecord> runtimes,
        IEnumerable<RuntimeSourceEntry> sources,
        IReadOnlyDictionary<string, RuntimeUpdateState> updateStates)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(runtimes);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(updateStates);

        var downloaded = DownloadedSourcesForPreset(sources, preset.Id);
        var installed = InstalledRuntimesForPreset(runtimes, preset.Id);
        var localCommit = RuntimeBuildCatalogService.LatestLocalCommitValue(downloaded, installed);
        return new RuntimeBuildPresetLocalState(
            downloaded,
            installed,
            localCommit,
            CurrentRuntimeUpdateState(updateStates, preset.Id, localCommit));
    }

    public static IReadOnlyList<RuntimeSourceEntry> DownloadedSourcesForPreset(
        IEnumerable<RuntimeSourceEntry> sources,
        string presetId)
        => sources
            .Where(source => string.Equals(source.PresetId, presetId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(source => source.DownloadedAt)
            .ToList();

    public static IReadOnlyList<RuntimeRecord> InstalledRuntimesForPreset(
        IReadOnlyList<RuntimeRecord> runtimes,
        string presetId)
        => runtimes
            .Where(runtime => RuntimeAvailabilityService.IsAvailable(runtime)
                && RuntimeMetadataService.IsManagedSourceBuild(runtime)
                && string.Equals(RuntimeMetadataService.ManagedPresetId(runtime), presetId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(runtime => RuntimeMetadataService.Folder(runtime), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(runtime => runtime.UpdatedAt).First())
            .OrderByDescending(runtime => runtime.UpdatedAt)
            .ToList();

    public static RuntimeUpdateState? CurrentRuntimeUpdateState(
        IReadOnlyDictionary<string, RuntimeUpdateState> updateStates,
        string presetId,
        string localCommit)
    {
        if (!updateStates.TryGetValue(presetId, out var state)) return null;
        if (string.IsNullOrWhiteSpace(state.LocalCommit) && string.IsNullOrWhiteSpace(localCommit))
            return state;
        return RuntimeMetadataService.CommitsMatch(state.LocalCommit, localCommit) ? state : null;
    }

    private static IReadOnlySet<string> ActiveRuntimeIds(IEnumerable<LoadedModelSessionSnapshot> sessions)
        => sessions
            .Where(session => session.IsRunning)
            .Select(session => session.RuntimeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
