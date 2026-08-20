namespace LocalLlmConsole.Services;

internal sealed class ControlProfileEndpoints : ControlEndpointHandler
{
    public ControlProfileEndpoints(ControlEndpointContext context)
        : base(context)
    {
    }

    public async Task<LocalControlApiResponse> HandleAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        ModelRecord model,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 5 && method == "GET")
        {
            var snapshot = await _modelGroups.SnapshotAsync();
            var profiles = await _deps.LaunchProfiles.ListNamedAsync(model);
            return Ok(new
            {
                ok = true,
                model = model.Id,
                profiles = profiles.Select(profile => ProfileView(profile, snapshot, _deps.Actions.GetSettings().AutoUnloadIdleMinutes)).ToArray()
            });
        }

        if (segments.Length == 5 && method == "POST")
        {
            var write = Body<LocalControlProfileWriteRequest>(request.Body);
            if (string.IsNullOrWhiteSpace(write.Name)) throw new InvalidOperationException("Profile name is required.");
            var profileId = string.IsNullOrWhiteSpace(write.Id) ? $"profile:{model.Id}:{Guid.NewGuid():N}" : write.Id.Trim();
            ControlProfileScope.EnsureCreateIdAvailable(await _deps.StateStore.GetNamedModelLaunchProfileAsync(profileId), model, profileId);
            var defaults = await _deps.LaunchProfiles.EnsureDefaultAsync(model, _deps.Actions.GetSettings());
            var settings = ProfileSettings(defaults.Settings, write.Settings, write.Replace);
            var profile = new NamedModelLaunchProfile(
                profileId,
                model.Id,
                write.Name.Trim(),
                settings,
                DateTimeOffset.UtcNow,
                write.IsDefault);
            await SaveProfileAsync(model, profile);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return new LocalControlApiResponse(201, new
            {
                ok = true,
                profile = ProfileView(
                    profile,
                    await _modelGroups.SnapshotAsync(),
                    _deps.Actions.GetSettings().AutoUnloadIdleMinutes)
            });
        }

        if (segments.Length == 7 && segments[6].Equals("group", StringComparison.OrdinalIgnoreCase))
        {
            var profiles = await _deps.LaunchProfiles.ListNamedAsync(model);
            var profile = profiles.FirstOrDefault(candidate =>
                              candidate.Id.Equals(segments[5], StringComparison.OrdinalIgnoreCase)
                              || candidate.Name.Equals(segments[5], StringComparison.OrdinalIgnoreCase))
                          ?? throw new KeyNotFoundException($"Launch profile '{segments[5]}' was not found for {model.Name}.");
            if (method == "GET")
            {
                var snapshot = await _modelGroups.SnapshotAsync();
                return Ok(new { ok = true, model = model.Id, profile = ProfileView(profile, snapshot, _deps.Actions.GetSettings().AutoUnloadIdleMinutes) });
            }
            if (method == "PUT")
            {
                var groupIdentifier = RequiredString(request.Body, "group");
                await _modelGroups.AssignAsync(profile.Id, groupIdentifier);
                await _deps.Actions.RefreshAsync(cancellationToken);
                var snapshot = await _modelGroups.SnapshotAsync();
                return Ok(new { ok = true, model = model.Id, profile = ProfileView(profile, snapshot, _deps.Actions.GetSettings().AutoUnloadIdleMinutes) });
            }
            if (method == "DELETE")
            {
                await _modelGroups.UnassignAsync(profile.Id);
                await _deps.Actions.RefreshAsync(cancellationToken);
                return Ok(new { ok = true, model = model.Id, profile = profile.Id, group = (object?)null, inherited = true });
            }
        }

        if (segments.Length == 6 && method == "PUT")
        {
            var profileId = segments[5];
            var existing = (await _deps.LaunchProfiles.ListNamedAsync(model)).FirstOrDefault(profile =>
                profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Launch profile '{profileId}' was not found for {model.Name}.");
            var write = Body<LocalControlProfileWriteRequest>(request.Body);
            var updated = existing with
            {
                Name = string.IsNullOrWhiteSpace(write.Name) ? existing.Name : write.Name.Trim(),
                Settings = ProfileSettings(existing.Settings, write.Settings, write.Replace),
                IsDefault = write.IsDefault || existing.IsDefault,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await SaveProfileAsync(model, updated);
            await _deps.Actions.RefreshAsync(cancellationToken);
            var snapshot = await _modelGroups.SnapshotAsync();
            return Ok(new { ok = true, profile = ProfileView(updated, snapshot, _deps.Actions.GetSettings().AutoUnloadIdleMinutes) });
        }

        if (segments.Length == 6 && method == "DELETE")
        {
            var profile = ControlProfileScope.ResolveOwned(await _deps.LaunchProfiles.ListNamedAsync(model), model, segments[5]);
            var deleted = await _deps.LaunchProfiles.DeleteNamedAsync(profile.Id);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new { ok = true, deleted = profile.Id, promotedDefault = deleted });
        }

        return Error(404, "Not found.");
    }

}
