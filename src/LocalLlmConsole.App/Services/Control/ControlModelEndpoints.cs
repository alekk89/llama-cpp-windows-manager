namespace LocalLlmConsole.Services;

internal sealed class ControlModelEndpoints : ControlEndpointHandler
{
    private readonly ControlProfileEndpoints _profiles;

    public ControlModelEndpoints(ControlEndpointContext context)
        : base(context)
    {
        _profiles = new ControlProfileEndpoints(context);
    }

    internal async Task<LocalControlApiResponse> ModelsAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 3 && method == "GET")
        {
            var models = await _deps.StateStore.ListModelsAsync();
            var profiles = await _deps.StateStore.ListNamedModelLaunchProfilesAsync();
            var groups = await _modelGroups.SnapshotAsync();
            return Ok(new
            {
                ok = true,
                models = models.Select(model => ModelView(
                    model,
                    profiles.Where(profile => profile.ModelId.Equals(model.Id, StringComparison.OrdinalIgnoreCase)).ToArray(),
                    groups,
                    _deps.Actions.GetSettings().AutoUnloadIdleMinutes)).ToArray()
            });
        }

        if (segments.Length == 4 && segments[3].Equals("scan", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var result = await _deps.ModelCatalog.ScanDetailedAsync(_deps.Actions.GetSettings().ModelsRoot);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new
            {
                ok = true,
                registered = result.RegisteredCount,
                discovered = result.DiscoveredCount,
                companions = result.CompanionCount,
                ambiguous = result.AmbiguousCount,
                invalid = result.InvalidCount,
                files = result.Files.Select(ClassificationView).ToArray()
            });
        }

        if (segments.Length == 4 && segments[3].Equals("import", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var file = request.Body?["file"]?.ToString().Trim() ?? "";
            var folder = request.Body?["folder"]?.ToString().Trim() ?? "";
            if (string.IsNullOrWhiteSpace(file) == string.IsNullOrWhiteSpace(folder))
                throw new InvalidOperationException("Specify exactly one of 'file' or 'folder'.");

            GgufFileClassification? classification = null;
            ModelRecord importedModel;
            if (!string.IsNullOrWhiteSpace(file))
            {
                classification = ModelCatalogService.ClassifyGguf(file);
                var confirmRole = request.Body?["confirmRole"]?.GetValue<bool>() ?? false;
                importedModel = await _deps.ModelCatalog.ImportFileAsync(file, confirmRole);
            }
            else
            {
                if (!Directory.Exists(folder))
                    throw new InvalidOperationException($"Model folder '{folder}' was not found.");
                importedModel = await _deps.ModelCatalog.ImportFolderAsync(folder);
            }
            await _deps.LaunchProfiles.EnsureDefaultAsync(importedModel, _deps.Actions.GetSettings());
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new
            {
                ok = true,
                source = classification is null ? "folder" : "file",
                classification = classification is null ? null : ClassificationView(classification),
                model = ModelView(
                    importedModel,
                    await _deps.LaunchProfiles.ListNamedAsync(importedModel),
                    await _modelGroups.SnapshotAsync(),
                    _deps.Actions.GetSettings().AutoUnloadIdleMinutes)
            });
        }

        if (segments.Length < 4) return Error(404, "Not found.");
        var model = await ResolveModelAsync(segments[3]);

        if (segments.Length == 4 && method == "GET")
        {
            var groups = await _modelGroups.SnapshotAsync();
            return Ok(new
            {
                ok = true,
                model = ModelView(
                    model,
                    await _deps.LaunchProfiles.ListNamedAsync(model),
                    groups,
                    _deps.Actions.GetSettings().AutoUnloadIdleMinutes)
            });
        }

        if (segments.Length == 5 && segments[4].Equals("group", StringComparison.OrdinalIgnoreCase))
        {
            var defaultProfile = (await _deps.LaunchProfiles.ListNamedAsync(model)).FirstOrDefault(profile => profile.IsDefault)
                ?? throw new InvalidOperationException($"{model.Name} does not have a default launch profile.");
            if (method == "GET")
            {
                var snapshot = await _modelGroups.SnapshotAsync();
                return Ok(new
                {
                    ok = true,
                    model = model.Id,
                    profile = defaultProfile.Id,
                    group = ModelGroupDetails(snapshot.GroupForProfile(defaultProfile.Id)),
                    effectivePolicy = ModelGroupPolicyView(ModelGroupService.EffectivePolicy(snapshot, defaultProfile.Id, _deps.Actions.GetSettings().AutoUnloadIdleMinutes)),
                    compatibilityRoute = true
                });
            }
            if (method == "PUT")
            {
                var groupIdentifier = RequiredString(request.Body, "group");
                var assignment = await _modelGroups.AssignAsync(defaultProfile.Id, groupIdentifier);
                await _deps.Actions.RefreshAsync(cancellationToken);
                var snapshot = await _modelGroups.SnapshotAsync();
                return Ok(new
                {
                    ok = true,
                    assignment,
                    group = ModelGroupDetails(snapshot.GroupForProfile(defaultProfile.Id)),
                    effectivePolicy = ModelGroupPolicyView(ModelGroupService.EffectivePolicy(snapshot, defaultProfile.Id, _deps.Actions.GetSettings().AutoUnloadIdleMinutes)),
                    compatibilityRoute = true
                });
            }
            if (method == "DELETE")
            {
                await _modelGroups.UnassignAsync(defaultProfile.Id);
                await _deps.Actions.RefreshAsync(cancellationToken);
                return Ok(new { ok = true, model = model.Id, profile = defaultProfile.Id, group = (object?)null, inherited = true, compatibilityRoute = true });
            }
        }

        if (segments.Length == 4 && method == "DELETE")
        {
            if (!BoolQuery(request.Query, "confirm"))
                throw new InvalidOperationException("Model deletion requires '?confirm=true'. App-owned model deletion also removes its managed folder.");
            if (_deps.Sessions.SessionForModel(model.Id) is { IsRunning: true })
                await _deps.Actions.StopModelAsync(model, cancellationToken);
            await _deps.ModelCatalog.DeleteAsync(model, _deps.Actions.GetSettings().ModelsRoot);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new { ok = true, deleted = model.Id, filesDeleted = model.Ownership == OwnershipKind.AppOwned });
        }

        if (segments.Length == 5 && segments[4].Equals("companions", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return Ok(new
            {
                ok = true,
                model = model.Id,
                visionProjectors = ModelCatalogService.FindVisionProjectors(model.ModelPath),
                draftAndMtpHeads = ModelCatalogService.FindDraftModels(model.ModelPath),
                mtpHeads = ModelCatalogService.FindDraftModels(model.ModelPath, "draft-mtp"),
                dflashHeads = ModelCatalogService.FindDraftModels(model.ModelPath, "draft-dflash"),
                dsparkHeads = ModelCatalogService.FindDraftModels(model.ModelPath, "draft-dspark"),
                eagle3Heads = ModelCatalogService.FindDraftModels(model.ModelPath, "draft-eagle3"),
                simpleDraftModels = ModelCatalogService.FindDraftModels(model.ModelPath, "draft-simple"),
                embeddedDraftMtp = ModelCatalogService.HasEmbeddedDraftMtp(model.ModelPath),
                embeddedVisionToken = VisionProjectorSelection.EmbeddedToken,
                autoDiscoveryScope = "model-folder-only",
                draftMtpAutoPrecedence = "embedded-main-gguf-then-matching-mtp-sidecar"
            });

        if (segments.Length == 5 && segments[4].Equals("load", StringComparison.OrdinalIgnoreCase) && method == "POST")
            return await LoadModelAsync(model, Body<LocalControlLoadRequest>(request.Body), forceRestart: false, cancellationToken);

        if (segments.Length == 5 && segments[4].Equals("restart", StringComparison.OrdinalIgnoreCase) && method == "POST")
            return await LoadModelAsync(model, Body<LocalControlLoadRequest>(request.Body), forceRestart: true, cancellationToken);

        if (segments.Length == 5 && segments[4].Equals("unload", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            await _deps.Actions.StopModelAsync(model, cancellationToken);
            return Ok(new { ok = true, model = model.Id, status = "unloaded" });
        }

        if (segments.Length >= 5 && segments[4].Equals("profiles", StringComparison.OrdinalIgnoreCase))
            return await _profiles.HandleAsync(method, segments, request, model, cancellationToken);

        return Error(404, "Not found.");
    }

    private async Task<LocalControlApiResponse> LoadModelAsync(
        ModelRecord model,
        LocalControlLoadRequest request,
        bool forceRestart,
        CancellationToken cancellationToken)
    {
        var gate = _modelOperationGates.GetOrAdd(model.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadModelCoreAsync(model, request, forceRestart, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<LocalControlApiResponse> LoadModelCoreAsync(
        ModelRecord model,
        LocalControlLoadRequest request,
        bool forceRestart,
        CancellationToken cancellationToken)
    {
        if (request.UnloadOthers)
        {
            foreach (var otherSession in _deps.Sessions.Snapshots().Where(candidate =>
                         candidate.IsRunning && !candidate.ModelId.Equals(model.Id, StringComparison.OrdinalIgnoreCase)))
            {
                var other = await ResolveModelAsync(otherSession.ModelId);
                await _deps.Actions.StopModelAsync(other, cancellationToken);
            }
        }

        var profiles = await _deps.LaunchProfiles.ListNamedAsync(model);
        var profile = ResolveProfile(profiles, request.ProfileId, request.ProfileName)
            ?? await _deps.LaunchProfiles.EnsureDefaultAsync(model, _deps.Actions.GetSettings());
        var profileSettings = ControlJsonPatch.Apply(profile.Settings, request.Settings);
        if (!string.IsNullOrWhiteSpace(request.RuntimeId))
            profileSettings = profileSettings with { RuntimeId = request.RuntimeId.Trim() };

        if (request.SaveProfile)
        {
            var saveName = string.IsNullOrWhiteSpace(request.SaveProfileName) ? profile.Name : request.SaveProfileName.Trim();
            var saved = profile with { Name = saveName, Settings = profileSettings, UpdatedAt = DateTimeOffset.UtcNow };
            await SaveProfileAsync(model, saved);
            profile = saved;
        }

        var runtimes = await _deps.StateStore.ListRuntimesAsync();
        var runtime = ResolveRuntime(runtimes, profileSettings.RuntimeId)
            ?? throw new InvalidOperationException(string.IsNullOrWhiteSpace(profileSettings.RuntimeId)
                ? "No registered llama.cpp runtime is available."
                : $"Runtime '{profileSettings.RuntimeId}' is not registered or available.");
        var current = _deps.Sessions.SessionForModel(model.Id);
        var restart = forceRestart || request.Restart;
        if (current is { IsRunning: true } && !restart)
            return Ok(new { ok = true, alreadyRunning = true, session = SessionView(current) });
        if (current is { IsRunning: true })
            await _deps.Actions.StopModelAsync(model, cancellationToken);

        var launchSettings = profileSettings.ApplyTo(_deps.Actions.GetSettings());
        var session = await _deps.Actions.StartModelAsync(
            runtime,
            model,
            launchSettings,
            profile.Id,
            profile.Name,
            cancellationToken);

        if (request.WaitForReady)
            session = await WaitForReadyAsync(model, launchSettings, request.TimeoutSeconds, cancellationToken);

        return Ok(new
        {
            ok = true,
            alreadyRunning = false,
            ready = request.WaitForReady,
            session = SessionView(session),
            effectiveSettings = ModelLaunchSettings.FromAppSettings(session.LaunchSettings, session.RuntimeId)
        });
    }

    private async Task<LoadedModelSessionSnapshot> WaitForReadyAsync(
        ModelRecord model,
        AppSettings settings,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 3600));
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _deps.Sessions.SessionForModel(model.Id)
                ?? throw new InvalidOperationException($"{model.Name} stopped before its endpoint became ready.");
            if (!session.IsRunning)
                throw new InvalidOperationException($"{model.Name} stopped before its endpoint became ready: {session.StatusReason}");
            if (await _deps.RuntimeEndpointProbe.IsAliveAsync(settings, cancellationToken))
            {
                _deps.Sessions.MarkModelLoadedIfRunning(model.Id);
                return _deps.Sessions.SessionForModel(model.Id) ?? session;
            }
            await Task.Delay(500, cancellationToken);
        }
        throw new InvalidOperationException($"Timed out after {timeout.TotalSeconds:N0} seconds waiting for {model.Name} to become ready.");
    }

    private static object ClassificationView(GgufFileClassification classification)
        => new
        {
            path = classification.Path,
            role = classification.Role.ToString(),
            confidence = classification.Confidence.ToString(),
            reason = classification.Reason,
            architecture = classification.Architecture,
            generalType = classification.GeneralType,
            embeddedDraftMtp = classification.EmbeddedDraftMtp
        };

}
