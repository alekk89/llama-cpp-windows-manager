namespace LocalLlmConsole.Services;

public enum TrayProfileActionKind
{
    Start,
    Stop,
    Switch,
    Loading,
    Stopping
}

public sealed record TrayProfileMenuEntry(
    ModelRecord Model,
    NamedModelLaunchProfile Profile,
    bool IsFavorite,
    TrayProfileActionKind Action,
    bool CanExecute);

public sealed record TrayProfileMenuModel(
    ModelRecord Model,
    IReadOnlyList<TrayProfileMenuEntry> Profiles);

public sealed record TrayProfileMenuSnapshot(
    IReadOnlyList<TrayProfileMenuEntry> Favorites,
    IReadOnlyList<TrayProfileMenuModel> Models);

public sealed record TrayProfileCommandActions(
    Func<ModelRecord, NamedModelLaunchProfile, Task<ModelRuntimeLoadApplicationOutcome>> LoadAsync,
    Func<ModelRecord, NamedModelLaunchProfile, Task> StopAsync);

public sealed record TrayProfileCommandResult(
    TrayProfileActionKind Action,
    ModelRuntimeLoadApplicationOutcome? LoadOutcome = null,
    bool StopCompleted = false);

public sealed class TrayProfileMenuApplicationService
{
    private readonly StateStore _stateStore;
    private readonly LoadedModelSessionManager _sessions;

    public TrayProfileMenuApplicationService(StateStore stateStore, LoadedModelSessionManager sessions)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async Task<TrayProfileMenuSnapshot> BuildSnapshotAsync()
    {
        var models = await _stateStore.ListModelsAsync();
        var profiles = await _stateStore.ListNamedModelLaunchProfilesAsync();
        var favorites = await _stateStore.ListFavoriteLaunchProfileIdsAsync();
        var sessions = _sessions.Snapshots();
        var modelRows = new List<TrayProfileMenuModel>();

        foreach (var model in models.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase))
        {
            var entries = profiles
                .Where(profile => profile.ModelId.Equals(model.Id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(profile => profile.IsDefault)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .Select(profile => Entry(
                    model,
                    profile,
                    favorites.Contains(profile.Id),
                    sessions.FirstOrDefault(candidate =>
                        candidate.ModelId.Equals(model.Id, StringComparison.OrdinalIgnoreCase)
                        && candidate.LaunchProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))))
                .ToArray();
            if (entries.Length > 0)
                modelRows.Add(new TrayProfileMenuModel(model, entries));
        }

        var favoriteRows = modelRows
            .SelectMany(model => model.Profiles)
            .Where(profile => profile.IsFavorite)
            .OrderBy(profile => profile.Model.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(profile => profile.Profile.IsDefault)
            .ThenBy(profile => profile.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new TrayProfileMenuSnapshot(favoriteRows, modelRows);
    }

    public Task<IReadOnlySet<string>> FavoriteProfileIdsAsync()
        => _stateStore.ListFavoriteLaunchProfileIdsAsync();

    public async Task<bool> ToggleFavoriteAsync(string profileId)
    {
        var favorite = !await _stateStore.IsLaunchProfileFavoriteAsync(profileId);
        await _stateStore.SetLaunchProfileFavoriteAsync(profileId, favorite);
        return favorite;
    }

    public async Task<TrayProfileCommandResult> ExecuteAsync(
        TrayProfileMenuEntry entry,
        TrayProfileCommandActions actions)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.LoadAsync);
        ArgumentNullException.ThrowIfNull(actions.StopAsync);

        var running = _sessions.SessionForProfile(entry.Model.Id, entry.Profile.Id);
        if (running is { IsRunning: true })
        {
            await actions.StopAsync(entry.Model, entry.Profile);
            return new TrayProfileCommandResult(
                TrayProfileActionKind.Stop,
                StopCompleted: _sessions.SessionForProfile(entry.Model.Id, entry.Profile.Id) is not { IsRunning: true });
        }

        return new TrayProfileCommandResult(
            TrayProfileActionKind.Start,
            await actions.LoadAsync(entry.Model, entry.Profile));
    }

    private static TrayProfileMenuEntry Entry(
        ModelRecord model,
        NamedModelLaunchProfile profile,
        bool favorite,
        LoadedModelSessionSnapshot? session)
    {
        if (session?.Status == LoadedModelSessionStatus.Stopping)
            return new TrayProfileMenuEntry(model, profile, favorite, TrayProfileActionKind.Stopping, false);
        if (session?.Status == LoadedModelSessionStatus.Loading)
            return new TrayProfileMenuEntry(model, profile, favorite, TrayProfileActionKind.Loading, false);
        if (session is not { IsRunning: true })
            return new TrayProfileMenuEntry(model, profile, favorite, TrayProfileActionKind.Start, true);
        return new TrayProfileMenuEntry(model, profile, favorite, TrayProfileActionKind.Stop, true);
    }
}
