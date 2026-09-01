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

public sealed record StartupLaunchProfileLoadResult(
    int ConfiguredCount,
    int LoadedCount,
    int AlreadyRunningCount,
    IReadOnlyList<StartupLaunchProfileLoadFailure> Failures)
{
    public string StatusMessage
    {
        get
        {
            if (ConfiguredCount == 0) return "";
            if (Failures.Count == 0)
                return $"Loaded {LoadedCount.ToString(CultureInfo.InvariantCulture)} startup profile{(LoadedCount == 1 ? "" : "s")}.";
            return $"Loaded {LoadedCount.ToString(CultureInfo.InvariantCulture)} of {ConfiguredCount.ToString(CultureInfo.InvariantCulture)} startup profiles; {Failures.Count.ToString(CultureInfo.InvariantCulture)} failed.";
        }
    }
}

public sealed record StartupLaunchProfileLoadActions(
    Func<ModelRecord, NamedModelLaunchProfile, CancellationToken, Task<LoadedModelSessionSnapshot>> LoadAsync,
    Func<ModelRecord, NamedModelLaunchProfile, bool> IsRunning,
    Action<string> SetStatus);

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
            .OrderBy(choice => choice.ModelName, StringComparer.OrdinalIgnoreCase)
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

    public async Task<StartupLaunchProfileLoadResult> LoadConfiguredAsync(
        StartupLaunchProfileLoadActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.LoadAsync);
        ArgumentNullException.ThrowIfNull(actions.IsRunning);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);

        var snapshot = await GetSettingsSnapshotAsync();
        var loaded = 0;
        var alreadyRunning = 0;
        var failures = new List<StartupLaunchProfileLoadFailure>();
        foreach (var choice in snapshot.Selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (actions.IsRunning(choice.Model, choice.Profile))
            {
                alreadyRunning++;
                loaded++;
                continue;
            }

            actions.SetStatus($"Loading startup profile {choice.DisplayName}...");
            try
            {
                await actions.LoadAsync(choice.Model, choice.Profile, cancellationToken);
                loaded++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Startup profile '{choice.DisplayName}' failed to load: {ex}");
                failures.Add(new StartupLaunchProfileLoadFailure(choice, ex));
            }
        }

        var result = new StartupLaunchProfileLoadResult(
            snapshot.Selected.Count,
            loaded,
            alreadyRunning,
            failures);
        if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            actions.SetStatus(result.StatusMessage);
        return result;
    }
}
