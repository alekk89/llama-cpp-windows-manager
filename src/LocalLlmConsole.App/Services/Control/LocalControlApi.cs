using System.Collections.Concurrent;

namespace LocalLlmConsole.Services;

public sealed class LocalControlApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LocalControlDependencies _deps;
    private readonly ModelGroupService _modelGroups;
    private readonly ControlRequestAdmissionService _admission;
    private readonly ControlAppSettingsMutationService _settingsMutations = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _modelOperationGates = new(StringComparer.OrdinalIgnoreCase);

    public LocalControlApi(LocalControlDependencies dependencies)
    {
        _deps = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        ArgumentNullException.ThrowIfNull(dependencies.Actions);
        _modelGroups = dependencies.ModelGroups ?? new ModelGroupService(dependencies.StateStore);
        _admission = new ControlRequestAdmissionService(dependencies);
    }

    public async Task<LocalControlApiResponse> HandleAsync(
        LocalControlRequest request,
        CancellationToken cancellationToken = default)
        => await HandleAsync(request, ControlAdmissionContext.ExternalClient, cancellationToken);

    public async Task<LocalControlApiResponse> HandleAsync(
        LocalControlRequest request,
        ControlAdmissionContext admissionContext,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var response = await HandleCoreAsync(request, admissionContext, cancellationToken);
        if (_deps.AuditLog is not null)
        {
            try
            {
                await _deps.AuditLog.WriteAsync(request, response, Stopwatch.GetElapsedTime(started));
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Control API audit logging failed: {ex.Message}");
            }
        }
        return response;
    }

    private async Task<LocalControlApiResponse> HandleCoreAsync(
        LocalControlRequest request,
        ControlAdmissionContext admissionContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await _admission.EnsureAllowedAsync(request, admissionContext, cancellationToken);
            var response = await RouteAsync(request, cancellationToken);
            var settings = _deps.Actions.GetSettings();
            return response with
            {
                Body = ControlJsonPatch.RedactSensitiveData(
                    response.Body,
                    settings.ModelApiKey,
                    settings.ModelApiKeyBackup)
            };
        }
        catch (KeyNotFoundException ex)
        {
            return Error(404, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Error(400, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Error(400, ex.Message);
        }
        catch (JsonException ex)
        {
            return Error(400, $"Invalid JSON payload: {ex.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error(499, "The control request was cancelled.");
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Unexpected local control API failure: {ex}");
            return Error(500, "Internal server error.");
        }
    }

    private async Task<LocalControlApiResponse> RouteAsync(LocalControlRequest request, CancellationToken cancellationToken)
    {
        var method = request.Method.ToUpperInvariant();
        var segments = request.Path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        if (segments.Length < 3 || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("v1", StringComparison.OrdinalIgnoreCase))
            return Error(404, "Not found.");

        var route = segments[2].ToLowerInvariant();
        return route switch
        {
            "status" when method == "GET" => Ok(Status()),
            "capabilities" when method == "GET" => Ok(Capabilities()),
            "self" when method == "GET" => await IdentifySelfAsync(request, cancellationToken),
            "models" => await ModelsAsync(method, segments, request, cancellationToken),
            "model-groups" => await ModelGroupsAsync(method, segments, request, cancellationToken),
            "runtimes" => await RuntimesAsync(method, segments, request, cancellationToken),
            "sessions" => await SessionsAsync(method, segments, request, cancellationToken),
            "gateway" => await GatewayAsync(method, segments, cancellationToken),
            "settings" => await SettingsAsync(method, segments, request, cancellationToken),
            "logs" => await LogsAsync(method, segments, request, cancellationToken),
            "metrics" when method == "GET" => await AllMetricsAsync(cancellationToken),
            "jobs" => await JobsAsync(method, segments, cancellationToken),
            "huggingface" => await HuggingFaceAsync(method, segments, request, cancellationToken),
            "operations" => await OperationsAsync(method, segments, request, cancellationToken),
            _ => Error(404, "Not found.")
        };
    }

    private object Status()
    {
        var sessions = _deps.Sessions.Snapshots();
        return new
        {
            ok = true,
            apiVersion = "v1",
            app = "llama.cpp Windows Manager",
            processId = Environment.ProcessId,
            workspaceRoot = _deps.WorkspaceRoot,
            selectedSessionId = _deps.Sessions.SelectedSnapshot()?.SessionId ?? "",
            runningSessionCount = sessions.Count(session => session.IsRunning),
            sessions = sessions.Select(SessionView).ToArray(),
            time = DateTimeOffset.UtcNow
        };
    }

    private object Capabilities()
        => new
        {
            ok = true,
            apiVersion = "v1",
            features = new[]
            {
                "self-identification", "model-catalog", "model-scan", "model-import", "model-delete", "launch-profile-groups",
                "model-load", "model-restart", "model-unload", "session-status", "endpoint-inspection", "live-metrics",
                "runtime-and-app-logs", "profile-crud", "one-shot-setting-overrides", "vision-heads",
                "draft-and-mtp-heads", "app-settings", "runtime-scan", "runtime-register",
                "huggingface-search", "huggingface-download", "download-pause-resume-cancel", "jobs",
                "runtime-packages-and-builds", "windows-and-wsl-management", "maintenance",
                "lifetime-metrics", "gateway-control", "app-updates-and-lifecycle", "dry-run-operations"
            },
            routes = new[]
            {
                "GET /api/v1/status", "GET /api/v1/capabilities", "GET /api/v1/self",
                "GET /api/v1/models", "POST /api/v1/models/scan", "POST /api/v1/models/import",
                "GET /api/v1/models/{model}/companions", "POST /api/v1/models/{model}/load",
                "POST /api/v1/models/{model}/restart", "POST /api/v1/models/{model}/unload",
                "DELETE /api/v1/models/{model}?confirm=true", "GET /api/v1/models/{model}/profiles",
                "POST /api/v1/models/{model}/profiles", "PUT /api/v1/models/{model}/profiles/{profile}",
                "DELETE /api/v1/models/{model}/profiles/{profile}",
                "GET|PUT|DELETE /api/v1/models/{model}/profiles/{profile}/group",
                "GET|POST /api/v1/model-groups", "GET|PATCH|DELETE /api/v1/model-groups/{group}",
                "GET|PATCH /api/v1/settings",
                "POST /api/v1/settings/model-api-key/rotate",
                "GET /api/v1/runtimes", "POST /api/v1/runtimes/scan", "POST /api/v1/runtimes/register",
                "GET /api/v1/sessions", "GET /api/v1/sessions/{session}/logs",
                "GET /api/v1/sessions/{session}/metrics", "GET /api/v1/sessions/{session}/inspect",
                "GET /api/v1/gateway/inspect",
                "GET /api/v1/logs", "GET /api/v1/logs/{file}",
                "GET /api/v1/metrics", "GET /api/v1/jobs", "POST /api/v1/jobs/{job}/pause|resume|cancel",
                "GET /api/v1/huggingface/search?q=...", "POST /api/v1/huggingface/download",
                "GET /api/v1/operations", "POST /api/v1/operations/{operation}"
            },
            operations = ControlOperationCatalog.All,
            modelGroups = new
            {
                retentionModes = new[] { "Inherit", "Pinned", "IdleTimeout" },
                evictionPriorities = new[] { "Low", "Normal", "High" },
                idleMinutes = new { minimum = ModelGroupService.MinimumIdleMinutes, maximum = ModelGroupService.MaximumIdleMinutes },
                priorityMeaning = "Automatic idle eviction order only; active inference request scheduling is unchanged."
            },
            modelLaunchSettings = SettingsSchema<ModelLaunchSettings>(),
            appSettings = SettingsSchema<AppSettings>(),
            selfIdentificationHints = new[] { "sessionId", "model", "endpoint", "port", "processId" }
        };

    private async Task<LocalControlApiResponse> OperationsAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 3 && method == "GET")
            return Ok(new { ok = true, operations = ControlOperationCatalog.All });
        if (segments.Length != 4 || method != "POST") return Error(404, "Not found.");

        var operation = ControlOperationCatalog.Resolve(segments[3]);
        if (_deps.Actions.ExecuteOperationAsync is null)
            return Error(501, "The application operation bridge is not available in this host.");
        var body = request.Body ?? new JsonObject();
        var dryRun = body["dryRun"]?.GetValue<bool>() ?? false;
        var confirmed = body["confirm"]?.GetValue<bool>() ?? false;
        if (operation.RequiresConfirmation && !dryRun && !confirmed)
            throw new InvalidOperationException($"Operation '{operation.Name}' requires confirm=true. Run it with dryRun=true first when consequences are unclear.");

        var result = await _deps.Actions.ExecuteOperationAsync(operation.Name, body, cancellationToken);
        return Ok(new { ok = true, operation = operation.Name, dryRun, result });
    }

    private async Task<LocalControlApiResponse> ModelsAsync(
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
            var count = await _deps.ModelCatalog.ScanAsync(_deps.Actions.GetSettings().ModelsRoot);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new { ok = true, registered = count });
        }

        if (segments.Length == 4 && segments[3].Equals("import", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var folder = RequiredString(request.Body, "folder");
            if (!Directory.Exists(folder))
                throw new InvalidOperationException($"Model folder '{folder}' was not found.");
            var importedModel = await _deps.ModelCatalog.ImportFolderAsync(folder);
            await _deps.LaunchProfiles.EnsureDefaultAsync(importedModel, _deps.Actions.GetSettings());
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new
            {
                ok = true,
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
            return await ProfilesAsync(method, segments, request, model, cancellationToken);

        return Error(404, "Not found.");
    }

    private async Task<LocalControlApiResponse> ModelGroupsAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = await _modelGroups.SnapshotAsync();
        if (segments.Length == 3 && method == "GET")
            return Ok(new
            {
                ok = true,
                groups = snapshot.Groups.Select(group => ModelGroupView(group, snapshot)).ToArray()
            });

        if (segments.Length == 3 && method == "POST")
        {
            var body = Body<LocalControlModelGroupWriteRequest>(request.Body);
            var group = await _modelGroups.CreateAsync(
                body.Name,
                EnumRequest<ModelGroupRetentionMode>(body.RetentionMode, "retentionMode"),
                body.IdleMinutes,
                EnumRequest<ModelGroupEvictionPriority>(body.EvictionPriority, "evictionPriority"));
            await _deps.Actions.RefreshAsync(cancellationToken);
            return new LocalControlApiResponse(201, new { ok = true, group = ModelGroupView(group, await _modelGroups.SnapshotAsync()) });
        }

        if (segments.Length != 4) return Error(404, "Not found.");
        var existing = ModelGroupService.Resolve(snapshot, segments[3]);
        if (method == "GET")
            return Ok(new { ok = true, group = ModelGroupView(existing, snapshot) });
        if (method is "PATCH" or "PUT")
        {
            var body = request.Body ?? new JsonObject();
            var name = body["name"]?.ToString() ?? existing.Name;
            var retentionMode = body["retentionMode"] is null
                ? existing.RetentionMode
                : EnumRequest<ModelGroupRetentionMode>(body["retentionMode"]!.ToString(), "retentionMode");
            var idleMinutes = body["idleMinutes"]?.GetValue<int>() ?? existing.IdleMinutes;
            var priority = body["evictionPriority"] is null
                ? existing.EvictionPriority
                : EnumRequest<ModelGroupEvictionPriority>(body["evictionPriority"]!.ToString(), "evictionPriority");
            var updated = await _modelGroups.UpdateAsync(existing.Id, name, retentionMode, idleMinutes, priority);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new { ok = true, group = ModelGroupView(updated, await _modelGroups.SnapshotAsync()) });
        }
        if (method == "DELETE")
        {
            await _modelGroups.DeleteAsync(existing.Id);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new { ok = true, deleted = existing.Id });
        }
        return Error(404, "Not found.");
    }

    private async Task<LocalControlApiResponse> ProfilesAsync(
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

    private async Task<LocalControlApiResponse> RuntimesAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 3 && method == "GET")
            return Ok(new { ok = true, runtimes = await _deps.StateStore.ListRuntimesAsync() });
        if (segments.Length == 4 && segments[3].Equals("scan", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var count = await _deps.RuntimeRegistry.ScanAsync(_deps.Actions.GetSettings().RuntimeRoot);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new { ok = true, registered = count });
        }
        if (segments.Length == 4 && segments[3].Equals("register", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var folder = RequiredString(request.Body, "folder");
            if (!Directory.Exists(folder))
                throw new InvalidOperationException($"Runtime folder '{folder}' was not found.");
            var runtime = await _deps.RuntimeRegistry.RegisterFolderAsync(folder);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return new LocalControlApiResponse(201, new { ok = true, runtime });
        }
        return Error(404, "Not found.");
    }

    private async Task<LocalControlApiResponse> SessionsAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 3 && method == "GET")
            return Ok(new { ok = true, sessions = _deps.Sessions.Snapshots().Select(SessionView).ToArray() });
        if (segments.Length < 4 || method != "GET") return Error(404, "Not found.");
        var session = ResolveSession(segments[3]);
        if (segments.Length == 4) return Ok(new { ok = true, session = SessionView(session) });
        if (segments.Length == 5 && segments[4].Equals("logs", StringComparison.OrdinalIgnoreCase))
            return SessionLogs(session, IntQuery(request.Query, "tail", 16000, 1000, 250000));
        if (segments.Length == 5 && segments[4].Equals("metrics", StringComparison.OrdinalIgnoreCase))
            return await MetricsAsync([session], cancellationToken);
        if (segments.Length == 5 && segments[4].Equals("inspect", StringComparison.OrdinalIgnoreCase))
        {
            if (_deps.EndpointInspection is null)
                return Error(501, "Endpoint inspection is not available in this Manager build.");
            var report = await _deps.EndpointInspection.InspectDirectAsync(session, cancellationToken);
            return Ok(new { ok = true, report });
        }
        return Error(404, "Not found.");
    }

    private async Task<LocalControlApiResponse> GatewayAsync(
        string method,
        string[] segments,
        CancellationToken cancellationToken)
    {
        if (method != "GET"
            || segments.Length != 4
            || !segments[3].Equals("inspect", StringComparison.OrdinalIgnoreCase))
            return Error(404, "Not found.");
        if (_deps.EndpointInspection is null)
            return Error(501, "Endpoint inspection is not available in this Manager build.");

        var settings = _deps.Actions.GetSettings();
        var report = await _deps.EndpointInspection.InspectGatewayAsync(
            settings,
            AppPreferenceService.GatewaySwapPolicyLabel(settings.AutoLoadGatewayPolicy),
            AppPreferenceService.ModelAccessModeLabel(settings.ModelAccessMode),
            cancellationToken);
        return Ok(new { ok = true, report });
    }

    private async Task<LocalControlApiResponse> SettingsAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 5
            && segments[3].Equals("model-api-key", StringComparison.OrdinalIgnoreCase)
            && segments[4].Equals("rotate", StringComparison.OrdinalIgnoreCase)
            && method == "POST")
        {
            var rotated = _settingsMutations.RotateModelApiKey(_deps.Actions.GetSettings());
            await _deps.Actions.ApplySettingsAsync(rotated, cancellationToken);
            return Ok(new { ok = true, modelApiKey = "[rotated]", requireApiKeyAuth = true });
        }
        if (segments.Length != 3) return Error(404, "Not found.");
        if (method == "GET")
            return Ok(new { ok = true, settings = ControlJsonPatch.RedactedAppSettings(_deps.Actions.GetSettings()) });
        if (method is not ("PATCH" or "PUT")) return Error(404, "Not found.");

        var current = _deps.Actions.GetSettings();
        var updated = _settingsMutations.Patch(current, request.Body, _deps.Sessions.Snapshots());
        updated = await _deps.Actions.ApplySettingsAsync(updated, cancellationToken);
        return Ok(new { ok = true, settings = ControlJsonPatch.RedactedAppSettings(updated) });
    }

    private async Task<LocalControlApiResponse> LogsAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (method != "GET") return Error(404, "Not found.");
        if (segments.Length == 3)
        {
            var data = await _deps.LogWorkflow.LoadAsync(_deps.Sessions.SelectedSnapshot(), cancellationToken);
            return Ok(new
            {
                ok = true,
                logs = data.Files.Select(file => new
                {
                    file = file.Name,
                    path = file.FullName,
                    sizeBytes = file.Length,
                    updatedAt = file.LastWriteTimeUtc,
                    active = _deps.Sessions.Snapshots().Any(session => session.IsRunning &&
                        LogFileService.NormalizePath(session.LogPath).Equals(LogFileService.NormalizePath(file.FullName), StringComparison.OrdinalIgnoreCase))
                }).OrderByDescending(file => file.updatedAt).ToArray()
            });
        }

        if (segments.Length == 4)
        {
            var name = Path.GetFileName(segments[3]);
            if (!name.Equals(segments[3], StringComparison.Ordinal))
                throw new InvalidOperationException("Log identifiers must be file names, not paths.");
            var path = Path.Combine(_deps.LogWorkflow.LogRoot, name);
            if (!LogFileService.TryValidateWorkspaceLogFile(_deps.WorkspaceRoot, path, out var fullPath, out var error))
                throw new KeyNotFoundException(error);
            var text = LogFileService.Tail(fullPath, IntQuery(request.Query, "tail", 80000, 1000, 250000));
            return Ok(new { ok = true, file = name, path = fullPath, text = LogFileService.RedactSensitiveText(text, _deps.Actions.GetSettings().ModelApiKey) });
        }
        return Error(404, "Not found.");
    }

    private LocalControlApiResponse SessionLogs(LoadedModelSessionSnapshot session, int tail)
    {
        if (string.IsNullOrWhiteSpace(session.LogPath) || !File.Exists(session.LogPath))
            return Ok(new { ok = true, session = session.SessionId, active = false, text = "No runtime log is available yet." });
        var text = LogFileService.Tail(session.LogPath, tail);
        return Ok(new
        {
            ok = true,
            session = session.SessionId,
            active = session.IsRunning,
            path = session.LogPath,
            text = LogFileService.RedactSensitiveText(text, session.LaunchSettings.ModelApiKey)
        });
    }

    private Task<LocalControlApiResponse> AllMetricsAsync(CancellationToken cancellationToken)
        => MetricsAsync(_deps.Sessions.Snapshots().Where(session => session.IsRunning).ToArray(), cancellationToken);

    private async Task<LocalControlApiResponse> MetricsAsync(
        IReadOnlyList<LoadedModelSessionSnapshot> sessions,
        CancellationToken cancellationToken)
    {
        var results = await _deps.RuntimeTelemetry.PollSessionsAsync(sessions, cancellationToken);
        return Ok(new
        {
            ok = true,
            capturedAt = DateTimeOffset.UtcNow,
            metrics = results.Select(result => new
            {
                session = SessionView(result.Session),
                result.RuntimeKey,
                result.EndpointResponded,
                result.Error,
                result.SlotSnapshot,
                samples = result.Samples
            }).ToArray()
        });
    }

    private async Task<LocalControlApiResponse> JobsAsync(
        string method,
        string[] segments,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 3 && method == "GET")
            return Ok(new { ok = true, jobs = await _deps.StateStore.ListJobsAsync() });
        if (segments.Length != 5 || method != "POST") return Error(404, "Not found.");
        var job = (await _deps.StateStore.ListJobsAsync()).FirstOrDefault(item =>
            item.Id.Equals(segments[3], StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Job '{segments[3]}' was not found.");
        var action = segments[4].ToLowerInvariant();
        switch (action)
        {
            case "pause":
                await _deps.HuggingFace.PauseDownloadAsync(job);
                break;
            case "resume":
                await _deps.HuggingFace.ResumeDownloadAsync(job, _deps.Actions.GetSettings());
                break;
            case "cancel":
            case "stop":
                await _deps.HuggingFace.StopDownloadAsync(job);
                break;
            default:
                return Error(404, "Not found.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Ok(new { ok = true, job = job.Id, action });
    }

    private async Task<LocalControlApiResponse> HuggingFaceAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 4 && segments[3].Equals("search", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            request.Query.TryGetValue("q", out var query);
            var results = await _deps.HuggingFace.SearchAsync(query ?? "", cancellationToken);
            return Ok(new { ok = true, results });
        }
        if (segments.Length == 4 && segments[3].Equals("download", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var download = Body<LocalControlDownloadRequest>(request.Body);
            var query = download.Query;
            if (string.IsNullOrWhiteSpace(query))
            {
                if (string.IsNullOrWhiteSpace(download.Repo))
                    throw new InvalidOperationException("Provide query or repo for a Hugging Face download.");
                query = string.IsNullOrWhiteSpace(download.Path)
                    ? download.Repo
                    : $"https://huggingface.co/{download.Repo}/resolve/{(string.IsNullOrWhiteSpace(download.Revision) ? "main" : download.Revision)}/{download.Path}";
            }
            var results = await _deps.HuggingFace.SearchAsync(query, cancellationToken);
            var file = results.FirstOrDefault(candidate =>
                    string.IsNullOrWhiteSpace(download.Path)
                    || candidate.Path.Equals(download.Path, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("No matching GGUF file was found on Hugging Face.");
            if (download.DryRun)
                return Ok(new { ok = true, dryRun = true, file, wouldDownload = true });
            var job = await _deps.HuggingFace.StartDownloadAsync(file, _deps.Actions.GetSettings(), cancellationToken);
            return new LocalControlApiResponse(202, new { ok = true, job, file });
        }
        return Error(404, "Not found.");
    }

    private async Task<LocalControlApiResponse> IdentifySelfAsync(
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        var sessions = _deps.Sessions.Snapshots().Where(session => session.IsRunning).ToArray();
        var hints = new List<(string Source, Func<LoadedModelSessionSnapshot, bool> Match)>();
        if (request.Query.TryGetValue("sessionId", out var sessionId) && !string.IsNullOrWhiteSpace(sessionId))
            hints.Add(("sessionId", session => session.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase)));
        if (request.Query.TryGetValue("model", out var model) && !string.IsNullOrWhiteSpace(model))
        {
            var models = await _deps.StateStore.ListModelsAsync();
            var profiles = await _deps.StateStore.ListNamedModelLaunchProfilesAsync();
            hints.Add(("model", session => ControlSelfIdentification.MatchesModelHint(session, models, profiles, model)));
        }
        if (TryQueryInt(request.Query, "port", out var port))
            hints.Add(("port", session => session.LaunchSettings.Port == port));
        if (TryQueryInt(request.Query, "processId", out var processId))
            hints.Add(("processId", session => session.ProcessId == processId));
        if (request.Query.TryGetValue("endpoint", out var endpoint) && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
            hints.Add(("endpoint", session => session.LaunchSettings.Port == endpointUri.Port));

        foreach (var hint in hints)
        {
            var matches = sessions.Where(hint.Match).ToArray();
            if (matches.Length == 1)
                return Ok(new { ok = true, identified = true, confidence = "exact", matchedBy = hint.Source, session = SessionView(matches[0]) });
        }

        if (sessions.Length == 1)
            return Ok(new { ok = true, identified = true, confidence = "inferred-single-running-session", matchedBy = "single-session", session = SessionView(sessions[0]) });

        return Ok(new
        {
            ok = true,
            identified = false,
            confidence = "ambiguous",
            message = sessions.Length == 0
                ? "No managed model session is running."
                : "More than one managed model is running. Supply sessionId, model, endpoint, port, or processId.",
            candidates = sessions.Select(SessionView).ToArray()
        });
    }

    private async Task<ModelRecord> ResolveModelAsync(string identifier)
    {
        var models = await _deps.StateStore.ListModelsAsync();
        return ModelGatewayRequestResolver.ResolveModel(models, identifier)
            ?? throw new KeyNotFoundException($"Model '{identifier}' was not found. Use GET /api/v1/models to list registered identifiers.");
    }

    private LoadedModelSessionSnapshot ResolveSession(string identifier)
        => _deps.Sessions.Snapshots().FirstOrDefault(session =>
               session.SessionId.Equals(identifier, StringComparison.OrdinalIgnoreCase)
               || session.ModelId.Equals(identifier, StringComparison.OrdinalIgnoreCase)
               || session.ModelName.Equals(identifier, StringComparison.OrdinalIgnoreCase))
           ?? throw new KeyNotFoundException($"Session '{identifier}' was not found.");

    private async Task SaveProfileAsync(ModelRecord model, NamedModelLaunchProfile profile)
    {
        var profiles = await _deps.LaunchProfiles.ListNamedAsync(model);
        if (profile.IsDefault)
        {
            foreach (var other in profiles.Where(other => other.IsDefault && !other.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)))
                await _deps.LaunchProfiles.SaveNamedAsync(other with { IsDefault = false, UpdatedAt = DateTimeOffset.UtcNow });
        }
        await _deps.LaunchProfiles.SaveNamedAsync(profile);
    }

    private static NamedModelLaunchProfile? ResolveProfile(
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        string profileId,
        string profileName)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
            return profiles.FirstOrDefault(profile => profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Launch profile '{profileId}' was not found.");
        if (!string.IsNullOrWhiteSpace(profileName))
            return profiles.FirstOrDefault(profile => profile.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Launch profile '{profileName}' was not found.");
        return profiles.FirstOrDefault(profile => profile.IsDefault);
    }

    private static RuntimeRecord? ResolveRuntime(IReadOnlyList<RuntimeRecord> runtimes, string runtimeId)
        => string.IsNullOrWhiteSpace(runtimeId)
            ? runtimes.FirstOrDefault(runtime => RuntimeAvailabilityService.IsAvailable(runtime)) ?? runtimes.FirstOrDefault()
            : runtimes.FirstOrDefault(runtime => runtime.Id.Equals(runtimeId, StringComparison.OrdinalIgnoreCase)
                || runtime.Name.Equals(runtimeId, StringComparison.OrdinalIgnoreCase));

    private static ModelLaunchSettings ProfileSettings(ModelLaunchSettings source, JsonObject? settings, bool replace)
    {
        if (!replace) return ControlJsonPatch.Apply(source, settings);
        if (settings is null) throw new InvalidOperationException("Replacing profile settings requires a complete settings object.");
        try
        {
            return settings.Deserialize<ModelLaunchSettings>(JsonOptions)
                ?? throw new InvalidOperationException("Profile settings were empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid complete profile settings: {ex.Message}", ex);
        }
    }

    private static object ModelView(
        ModelRecord model,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        ModelGroupSnapshot? groupSnapshot = null,
        int globalIdleMinutes = 0)
        => new
        {
            model.Id,
            model.Name,
            model.ModelPath,
            ownership = model.Ownership.ToString(),
            metadata = ParseJson(model.MetadataJson),
            model.UpdatedAt,
            profiles = profiles.Select(profile => ProfileView(profile, groupSnapshot, globalIdleMinutes)).ToArray()
        };

    private static object ProfileView(
        NamedModelLaunchProfile profile,
        ModelGroupSnapshot? snapshot = null,
        int globalIdleMinutes = 0)
        => new
        {
            profile.Id,
            profile.ModelId,
            profile.Name,
            profile.Settings,
            profile.UpdatedAt,
            profile.IsDefault,
            group = ModelGroupDetails(snapshot?.GroupForProfile(profile.Id)),
            effectivePolicy = snapshot is null
                ? null
                : ModelGroupPolicyView(ModelGroupService.EffectivePolicy(snapshot, profile.Id, globalIdleMinutes))
        };

    private static object ModelGroupView(ModelGroupRecord group, ModelGroupSnapshot snapshot)
    {
        var profileIds = snapshot.Assignments.Values
            .Where(assignment => assignment.GroupId.Equals(group.Id, StringComparison.OrdinalIgnoreCase))
            .Select(assignment => assignment.LaunchProfileId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new
        {
            group.Id,
            group.Name,
            retentionMode = group.RetentionMode.ToString(),
            group.IdleMinutes,
            evictionPriority = group.EvictionPriority.ToString(),
            group.UpdatedAt,
            profileCount = profileIds.Length,
            profileIds
        };
    }

    private static object? ModelGroupDetails(ModelGroupRecord? group)
        => group is null ? null : new
        {
            group.Id,
            group.Name,
            retentionMode = group.RetentionMode.ToString(),
            group.IdleMinutes,
            evictionPriority = group.EvictionPriority.ToString(),
            group.UpdatedAt
        };

    private static object ModelGroupPolicyView(EffectiveModelRetentionPolicy policy)
        => new
        {
            policy.AllowsIdleUnload,
            policy.IdleMinutes,
            evictionPriority = policy.EvictionPriority.ToString(),
            policy.GroupId,
            policy.GroupName
        };

    private static object SessionView(LoadedModelSessionSnapshot session)
        => new
        {
            session.SessionId,
            session.ModelId,
            session.ModelName,
            session.RuntimeId,
            session.RuntimeName,
            mode = session.Mode.ToString(),
            backend = session.Backend.ToString(),
            status = session.Status.ToString(),
            session.IsRunning,
            session.IsSelected,
            session.ProcessId,
            session.StartedAt,
            session.StoppedAt,
            session.Endpoint,
            session.EndpointHealth,
            session.StatusReason,
            session.LogPath,
            session.LaunchProfileId,
            session.LaunchProfileName,
            settings = ModelLaunchSettings.FromAppSettings(session.LaunchSettings, session.RuntimeId)
        };

    private static object SettingsSchema<T>()
        => typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => new
            {
                name = JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                clrName = property.Name,
                type = FriendlyType(property.PropertyType)
            }).ToArray();

    private static string FriendlyType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(string)) return "string";
        if (underlying == typeof(bool)) return "boolean";
        if (underlying == typeof(int) || underlying == typeof(long)) return "integer";
        if (underlying == typeof(double) || underlying == typeof(decimal)) return "number";
        return underlying.IsEnum ? $"enum:{string.Join('|', Enum.GetNames(underlying))}" : underlying.Name;
    }

    private static JsonNode? ParseJson(string json)
    {
        try { return JsonNode.Parse(json); }
        catch { return JsonValue.Create(json); }
    }

    private static T Body<T>(JsonObject? body)
        => (body ?? new JsonObject()).Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Request body could not be read as {typeof(T).Name}.");

    private static string RequiredString(JsonObject? body, string name)
    {
        var value = body?[name]?.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"'{name}' is required.");
        return value;
    }

    private static TEnum EnumRequest<TEnum>(string value, string field)
        where TEnum : struct, Enum
    {
        var normalized = (value ?? "").Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).Trim();
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (name.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<TEnum>(name);
        }
        throw new InvalidOperationException($"'{field}' must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
    }

    private static bool BoolQuery(IReadOnlyDictionary<string, string> query, string name)
        => query.TryGetValue(name, out var value) && bool.TryParse(value, out var parsed) && parsed;

    private static int IntQuery(IReadOnlyDictionary<string, string> query, string name, int fallback, int min, int max)
        => TryQueryInt(query, name, out var value) ? Math.Clamp(value, min, max) : fallback;

    private static bool TryQueryInt(IReadOnlyDictionary<string, string> query, string name, out int value)
    {
        value = 0;
        return query.TryGetValue(name, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static LocalControlApiResponse Ok(object body) => new(200, body);
    private static LocalControlApiResponse Error(int status, string error) => new(status, new { ok = false, error });
}
