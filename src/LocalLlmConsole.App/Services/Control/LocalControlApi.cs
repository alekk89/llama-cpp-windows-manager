namespace LocalLlmConsole.Services;

public sealed partial class LocalControlApi
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
    private readonly ControlBenchmarkEndpoints _benchmarks;
    private readonly ControlOperationEndpoints _operations;
    private readonly IReadOnlyDictionary<string, ControlRouteHandler> _routeHandlers;

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
        _benchmarks = new ControlBenchmarkEndpoints(context);
        _operations = new ControlOperationEndpoints(context);
        _routeHandlers = CreateRouteHandlers();
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

        return _routeHandlers.TryGetValue(segments[2], out var handler)
            ? await handler(method, segments, request, cancellationToken)
            : NotFound();
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
                "llama-bench", "benchmark-automation", "benchmark-history", "benchmark-agent-contract", "benchmark-comparison", "benchmark-delete",
                "runtime-packages-and-builds", "windows-and-wsl-management", "maintenance",
                "lifetime-metrics", "daily-usage-metrics", "prompt-cache-statistics", "gateway-control", "app-updates-and-lifecycle", "dry-run-operations"
            },
            routes = ControlRouteCatalog.AdvertisedRoutes,
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
            benchmarks = ControlBenchmarkContract.CapabilitySummary,
            selfIdentificationHints = new[] { "sessionId", "model", "endpoint", "port", "processId" }
        };

    private static LocalControlApiResponse Ok(object body) => ControlApiResponses.Ok(body);

    private static LocalControlApiResponse Error(int status, string error) => ControlApiResponses.Error(status, error);

}
