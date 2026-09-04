namespace LocalLlmConsole.Services;

internal sealed class ControlSessionEndpoints : ControlEndpointHandler
{
    public ControlSessionEndpoints(ControlEndpointContext context)
        : base(context)
    {
    }

    internal async Task<LocalControlApiResponse> SessionsAsync(
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

    internal async Task<LocalControlApiResponse> GatewayAsync(
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
            AppPreferenceService.GatewayPolicyLabel(settings),
            AppPreferenceService.ModelAccessModeLabel(settings.ModelAccessMode),
            cancellationToken);
        return Ok(new { ok = true, report });
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

    internal Task<LocalControlApiResponse> AllMetricsAsync(CancellationToken cancellationToken)
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

    internal async Task<LocalControlApiResponse> IdentifySelfAsync(
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
            return Ok(new
            {
                ok = true,
                identified = false,
                confidence = "inferred-single-running-session",
                message = "One managed model session is running, but no request hint proves that it serves this client. Supply sessionId, model, endpoint, port, or processId.",
                candidates = sessions.Select(SessionView).ToArray()
            });

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

}
