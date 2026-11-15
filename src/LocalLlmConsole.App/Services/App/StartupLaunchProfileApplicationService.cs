namespace LocalLlmConsole.Services;

public sealed record StartupLaunchProfileChoice(
    ModelRecord Model,
    NamedModelLaunchProfile Profile)
{
    public string ProfileId => Profile.Id;
    public string ModelName => Model.Name;
    public string ProfileName => Profile.Name;
    public string Port => Profile.Settings.Port.ToString(CultureInfo.InvariantCulture);
    public string DisplayName => $"{Model.Name} — {Profile.Name} (port {Port})";
}

public sealed record StartupLaunchProfileSettingsSnapshot(
    IReadOnlyList<StartupLaunchProfileChoice> Available,
    IReadOnlyList<StartupLaunchProfileChoice> Selected)
{
    public static StartupLaunchProfileSettingsSnapshot Empty { get; } = new([], []);
}

public sealed record StartupLaunchProfileLoadFailure(
    StartupLaunchProfileChoice Choice,
    Exception Exception);

public enum StartupLaunchProfileLoadState
{
    Pending,
    Loading,
    Loaded,
    AlreadyRunning,
    Failed,
    Skipped
}

public sealed record StartupLaunchProfileLoadProgress(
    StartupLaunchProfileChoice Choice,
    int Index,
    int Total,
    StartupLaunchProfileLoadState State,
    Exception? Error = null);

public sealed record StartupLaunchProfileLoadResult(
    int ConfiguredCount,
    int LoadedCount,
    int AlreadyRunningCount,
    IReadOnlyList<StartupLaunchProfileLoadFailure> Failures,
    int SkippedCount = 0)
{
    public bool Cancelled => SkippedCount > 0;

    public string StatusMessage
    {
        get
        {
            if (ConfiguredCount == 0) return "";
            if (Cancelled)
                return Loc.T("StartupProfiles.Status.Cancelled", LoadedCount, ConfiguredCount);
            if (Failures.Count == 0)
                return Loc.T("StartupProfiles.Status.Loaded", LoadedCount, ConfiguredCount);
            return Loc.T("StartupProfiles.Status.LoadedWithFailures", LoadedCount, ConfiguredCount, Failures.Count);
        }
    }
}

/// <summary>
/// Callbacks used to load startup profiles. <see cref="ReportProgress"/> receives every
/// profile as <see cref="StartupLaunchProfileLoadState.Pending"/> before the first load,
/// then each state change in order.
/// </summary>
public sealed record StartupLaunchProfileLoadActions(
    Func<ModelRecord, NamedModelLaunchProfile, CancellationToken, Task<LoadedModelSessionSnapshot>> LoadAsync,
    Func<ModelRecord, NamedModelLaunchProfile, bool> IsRunning,
    Action<string> SetStatus,
    Action<StartupLaunchProfileLoadProgress>? ReportProgress = null);

public sealed class StartupLaunchProfileApplicationService
{
    private readonly StateStore _stateStore;

    public StartupLaunchProfileApplicationService(StateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public async Task<StartupLaunchProfileSettingsSnapshot> GetSettingsSnapshotAsync()
    {
        var selectedIds = await _stateStore.ListStartupLaunchProfileIdsAsync();
        var favoriteIds = await _stateStore.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.LaunchProfile);
        var models = (await _stateStore.ListModelsAsync())
            .ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        var profiles = await _stateStore.ListNamedModelLaunchProfilesAsync();
        var choicesById = profiles
            .Where(profile => models.ContainsKey(profile.ModelId))
            .Select(profile => new StartupLaunchProfileChoice(models[profile.ModelId], profile))
            .ToDictionary(choice => choice.ProfileId, StringComparer.OrdinalIgnoreCase);
        var selected = selectedIds
            .Where(choicesById.ContainsKey)
            .Select(profileId => choicesById[profileId])
            .ToArray();
        var selectedSet = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var available = choicesById.Values
            .Where(choice => !selectedSet.Contains(choice.ProfileId))
            .OrderByDescending(choice => favoriteIds.Contains(choice.ProfileId))
            .ThenBy(choice => choice.ModelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new StartupLaunchProfileSettingsSnapshot(available, selected);
    }

    public Task SetLoadOnStartupAsync(string profileId, bool loadOnStartup)
        => _stateStore.SetStartupLaunchProfileAsync(profileId, loadOnStartup);

    public async Task<IReadOnlySet<string>> ConfiguredProfileIdsAsync()
        => (await _stateStore.ListStartupLaunchProfileIdsAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> ToggleLoadOnStartupAsync(string profileId)
    {
        var configured = await _stateStore.ListStartupLaunchProfileIdsAsync();
        var loadOnStartup = !configured.Contains(profileId);
        await _stateStore.SetStartupLaunchProfileAsync(profileId, loadOnStartup);
        return loadOnStartup;
    }

    /// <summary>
    /// Loads the configured startup profiles one at a time in their saved order. Cancelling
    /// <paramref name="cancellationToken"/> skips every profile that has not started loading;
    /// the token is also passed to the in-flight load, which decides whether to abort.
    /// </summary>
    public async Task<StartupLaunchProfileLoadResult> LoadConfiguredAsync(
        StartupLaunchProfileLoadActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.LoadAsync);
        ArgumentNullException.ThrowIfNull(actions.IsRunning);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);

        var snapshot = await GetSettingsSnapshotAsync();
        var selected = snapshot.Selected;
        var loaded = 0;
        var alreadyRunning = 0;
        var skipped = 0;
        var failures = new List<StartupLaunchProfileLoadFailure>();
        void Report(int index, StartupLaunchProfileLoadState state, Exception? error = null)
            => actions.ReportProgress?.Invoke(new(selected[index], index, selected.Count, state, error));

        for (var index = 0; index < selected.Count; index++)
            Report(index, StartupLaunchProfileLoadState.Pending);

        for (var index = 0; index < selected.Count; index++)
        {
            var choice = selected[index];
            if (cancellationToken.IsCancellationRequested)
            {
                skipped++;
                Report(index, StartupLaunchProfileLoadState.Skipped);
                continue;
            }

            if (actions.IsRunning(choice.Model, choice.Profile))
            {
                alreadyRunning++;
                loaded++;
                Report(index, StartupLaunchProfileLoadState.AlreadyRunning);
                continue;
            }

            actions.SetStatus(Loc.T("StartupProfiles.Status.Loading", choice.DisplayName));
            Report(index, StartupLaunchProfileLoadState.Loading);
            try
            {
                await actions.LoadAsync(choice.Model, choice.Profile, cancellationToken);
                loaded++;
                Report(index, StartupLaunchProfileLoadState.Loaded);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                skipped++;
                Report(index, StartupLaunchProfileLoadState.Skipped);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Startup profile '{choice.DisplayName}' failed to load: {ex}");
                failures.Add(new StartupLaunchProfileLoadFailure(choice, ex));
                Report(index, StartupLaunchProfileLoadState.Failed, ex);
            }
        }

        var result = new StartupLaunchProfileLoadResult(
            selected.Count,
            loaded,
            alreadyRunning,
            failures,
            skipped);
        if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            actions.SetStatus(result.StatusMessage);
        return result;
    }
}
