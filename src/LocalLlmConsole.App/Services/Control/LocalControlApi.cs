namespace LocalLlmConsole.Services;

public sealed class LocalControlApi
{
    private readonly LocalControlDependencies _deps;
    private readonly ControlRequestAdmissionService _admission;
    private readonly ControlModelEndpoints _models;
    private readonly ControlModelGroupEndpoints _modelGroups;
    private readonly ControlRuntimeEndpoints _runtime;
    private readonly ControlSessionEndpoints _sessions;
    private readonly ControlUsageMetricsEndpoints _usageMetrics;
    private readonly ControlSettingsEndpoints _settings;
    private readonly ControlLogEndpoints _logs;
    private readonly ControlJobEndpoints _jobs;
    private readonly ControlOperationEndpoints _operations;

    public LocalControlApi(LocalControlDependencies dependencies)
    {
        _deps = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        ArgumentNullException.ThrowIfNull(dependencies.Actions);
        _admission = new ControlRequestAdmissionService(dependencies);
        var context = new ControlEndpointContext(
            dependencies,
            dependencies.ModelGroups ?? new ModelGroupService(dependencies.StateStore));
        _models = new ControlModelEndpoints(context);
        _modelGroups = new ControlModelGroupEndpoints(context);
        _runtime = new ControlRuntimeEndpoints(context);
        _sessions = new ControlSessionEndpoints(context);
        _usageMetrics = new ControlUsageMetricsEndpoints(context);
        _settings = new ControlSettingsEndpoints(context);
        _logs = new ControlLogEndpoints(context);
        _jobs = new ControlJobEndpoints(context);
        _operations = new ControlOperationEndpoints(context);
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
            "self" when method == "GET" => await _sessions.IdentifySelfAsync(request, cancellationToken),
            "models" => await _models.ModelsAsync(method, segments, request, cancellationToken),
            "model-groups" => await _modelGroups.HandleAsync(method, segments, request, cancellationToken),
            "runtimes" => await _runtime.RuntimesAsync(method, segments, request, cancellationToken),
            "sessions" => await _sessions.SessionsAsync(method, segments, request, cancellationToken),
            "gateway" => await _sessions.GatewayAsync(method, segments, cancellationToken),
            "settings" => await _settings.SettingsAsync(method, segments, request, cancellationToken),
            "logs" => await _logs.LogsAsync(method, segments, request, cancellationToken),
            "metrics" when segments.Length == 3 && method == "GET" => await _sessions.AllMetricsAsync(cancellationToken),
            "metrics" => await _usageMetrics.HandleAsync(method, segments, request, cancellationToken),
            "jobs" => await _jobs.JobsAsync(method, segments, cancellationToken),
            "huggingface" => await _jobs.HuggingFaceAsync(method, segments, request, cancellationToken),
            "operations" => await _operations.HandleAsync(method, segments, request, cancellationToken),
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
            sessions = sessions.Select(ControlEndpointHandler.SessionView).ToArray(),
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
                "lifetime-metrics", "daily-usage-metrics", "prompt-cache-statistics", "gateway-control", "app-updates-and-lifecycle", "dry-run-operations"
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
                "GET /api/v1/metrics", "GET /api/v1/metrics/usage?range=1d|7d|30d|90d|all", "GET /api/v1/jobs", "POST /api/v1/jobs/{job}/pause|resume|cancel",
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
            modelLaunchSettings = ControlEndpointHandler.SettingsSchema<ModelLaunchSettings>(),
            appSettings = ControlEndpointHandler.SettingsSchema<AppSettings>(),
            selfIdentificationHints = new[] { "sessionId", "model", "endpoint", "port", "processId" }
        };

    private static LocalControlApiResponse Ok(object body) => ControlApiResponses.Ok(body);

    private static LocalControlApiResponse Error(int status, string error) => ControlApiResponses.Error(status, error);

}
