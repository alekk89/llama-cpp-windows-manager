namespace LocalLlmConsole.Services;

public sealed class ModelLaunchProfileService
{
    public const string DefaultProfileName = "Default";

    private readonly StateStore _stateStore;
    private readonly LoadedModelSessionManager _sessions;

    public ModelLaunchProfileService(StateStore stateStore, LoadedModelSessionManager sessions)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async Task<ModelLaunchSettings?> ReadAsync(ModelRecord model, string profileId = "")
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return (await ListNamedAsync(model)).FirstOrDefault(profile => profile.IsDefault)?.Settings;

        var profile = await _stateStore.GetNamedModelLaunchProfileAsync(profileId);
        return profile is not null && string.Equals(profile.ModelId, model.Id, StringComparison.OrdinalIgnoreCase)
            ? profile.Settings
            : null;
    }

    public Task<IReadOnlyList<NamedModelLaunchProfile>> ListNamedAsync(ModelRecord model)
        => _stateStore.ListNamedModelLaunchProfilesAsync(model.Id);

    public Task SaveNamedAsync(NamedModelLaunchProfile profile)
        => _stateStore.SaveNamedModelLaunchProfileAsync(profile);

    public async Task<NamedModelLaunchProfile?> DeleteNamedAsync(string profileId)
    {
        var profile = await _stateStore.GetNamedModelLaunchProfileAsync(profileId);
        if (profile is null)
            throw new InvalidOperationException("The selected launch profile no longer exists.");

        var modelProfiles = await _stateStore.ListNamedModelLaunchProfilesAsync(profile.ModelId);
        if (modelProfiles.Count <= 1)
            throw new InvalidOperationException("A model must keep at least one launch profile. Add another profile before removing this one.");

        await _stateStore.DeleteNamedModelLaunchProfileAsync(profileId);
        if (!profile.IsDefault) return null;

        var promoted = modelProfiles
            .Where(candidate => !string.Equals(candidate.Id, profileId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.UpdatedAt)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .First();
        if (!promoted.IsDefault)
        {
            promoted = promoted with { IsDefault = true, UpdatedAt = DateTimeOffset.UtcNow };
            await SaveNamedAsync(promoted);
        }
        return promoted;
    }

    public async Task<NamedModelLaunchProfile> EnsureDefaultAsync(ModelRecord model, AppSettings defaults)
        => (await EnsureDefaultsAsync([model], defaults))[0];

    public async Task<IReadOnlyList<NamedModelLaunchProfile>> EnsureDefaultsAsync(
        IReadOnlyList<ModelRecord> models,
        AppSettings defaults)
    {
        ArgumentNullException.ThrowIfNull(models);
        var allProfiles = (await _stateStore.ListNamedModelLaunchProfilesAsync()).ToList();
        var profilesByModel = allProfiles
            .GroupBy(profile => profile.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var usedPorts = allProfiles.Select(profile => profile.Settings.Port)
            .Concat(_sessions.Snapshots().Select(session => session.LaunchSettings.Port))
            .Where(RuntimePortAllocator.IsValidPort)
            .ToHashSet();
        if (defaults.AutoLoadGatewayEnabled)
            usedPorts.Add(defaults.AutoLoadGatewayPort);

        var ensured = new List<NamedModelLaunchProfile>(models.Count);
        foreach (var model in models)
        {
            var modelProfiles = profilesByModel.GetValueOrDefault(model.Id) ?? [];
            var existing = modelProfiles.FirstOrDefault(profile => profile.IsDefault)
                ?? modelProfiles.FirstOrDefault(profile =>
                    string.Equals(profile.Name, DefaultProfileName, StringComparison.OrdinalIgnoreCase))
                ?? modelProfiles
                    .OrderByDescending(profile => profile.UpdatedAt)
                    .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            if (existing is not null)
            {
                if (!existing.IsDefault)
                {
                    existing = existing with { IsDefault = true, UpdatedAt = DateTimeOffset.UtcNow };
                    await SaveNamedAsync(existing);
                }
                ensured.Add(existing);
                continue;
            }

            var id = $"default:{model.Id}";
            var port = ModelPortAllocator.NextAvailable(defaults.Port, usedPorts);
            usedPorts.Add(port);
            var created = new NamedModelLaunchProfile(
                id,
                model.Id,
                DefaultProfileName,
                ModelLaunchSettings.FromAppSettings(defaults) with { Port = port },
                DateTimeOffset.UtcNow,
                IsDefault: true);
            await SaveNamedAsync(created);
            allProfiles.Add(created);
            profilesByModel[model.Id] = [created];
            ensured.Add(created);
        }

        return ensured;
    }

    public async Task<ModelLaunchSettings> DraftAsync(ModelRecord model, AppSettings defaults, string profileId = "")
    {
        var profile = await ReadAsync(model, profileId);
        if (profile is not null) return profile;

        var port = await NextAvailablePortAsync(model.Id, defaults, profileId);
        return ModelLaunchSettings.FromAppSettings(defaults) with { Port = port };
    }

    public async Task<ModelLaunchSettings?> EnsureAsync(ModelRecord model, AppSettings defaults)
    {
        var defaultProfile = await EnsureDefaultAsync(model, defaults);
        var profile = defaultProfile.Settings;
        if (profile.Port is >= 1 and <= 65535
            && await IsPortAvailableAsync(model.Id, profile.Port, defaults, defaultProfile.Id))
            return defaultProfile.Settings;

        var next = profile with { Port = await NextAvailablePortAsync(model.Id, defaults, defaultProfile.Id) };
        await SaveNamedAsync(defaultProfile with { Settings = next, UpdatedAt = DateTimeOffset.UtcNow });
        return next;
    }

    public async Task SaveAsync(ModelRecord model, ModelLaunchSettings settings)
    {
        var profile = (await ListNamedAsync(model)).FirstOrDefault(item => item.IsDefault);
        var saved = profile is null
            ? new NamedModelLaunchProfile($"default:{model.Id}", model.Id, DefaultProfileName, settings, DateTimeOffset.UtcNow, true)
            : profile with { Settings = settings, UpdatedAt = DateTimeOffset.UtcNow };
        await SaveNamedAsync(saved);
    }

    public async Task<bool> IsPortAvailableAsync(string modelId, int port, AppSettings settings, string currentProfileId = "")
    {
        if (port is < 1 or > 65535) return false;
        if (settings.AutoLoadGatewayEnabled && port == settings.AutoLoadGatewayPort) return false;

        foreach (var session in _sessions.Snapshots())
        {
            if (string.Equals(session.ModelId, modelId, StringComparison.OrdinalIgnoreCase)) continue;
            if (session.LaunchSettings.Port == port) return false;
        }

        foreach (var profile in await _stateStore.ListNamedModelLaunchProfilesAsync())
        {
            if (string.Equals(profile.Id, currentProfileId, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(currentProfileId)
                && profile.IsDefault
                && string.Equals(profile.ModelId, modelId, StringComparison.OrdinalIgnoreCase)) continue;
            if (profile.Settings.Port == port) return false;
        }

        return true;
    }

    public async Task<int> NextAvailablePortAsync(string modelId, AppSettings settings, string currentProfileId = "")
    {
        var used = new List<int>();
        if (settings.AutoLoadGatewayEnabled)
            used.Add(settings.AutoLoadGatewayPort);

        foreach (var session in _sessions.Snapshots())
        {
            if (!string.Equals(session.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
                used.Add(session.LaunchSettings.Port);
        }

        foreach (var profile in await _stateStore.ListNamedModelLaunchProfilesAsync())
        {
            if (!string.Equals(profile.Id, currentProfileId, StringComparison.OrdinalIgnoreCase))
                used.Add(profile.Settings.Port);
        }

        return ModelPortAllocator.NextAvailable(settings.Port, used);
    }
}
