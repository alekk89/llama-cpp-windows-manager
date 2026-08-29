namespace LocalLlmConsole.Services;

public sealed partial class LocalControlApi
{
    private delegate Task<LocalControlApiResponse> ControlRouteHandler(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken);

    private IReadOnlyDictionary<string, ControlRouteHandler> CreateRouteHandlers()
    {
        var handlers = new Dictionary<string, ControlRouteHandler>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = (method, _, _, _) => Immediate(method == "GET" ? Ok(Status()) : NotFound()),
            ["capabilities"] = (method, _, _, _) => Immediate(method == "GET" ? Ok(Capabilities()) : NotFound()),
            ["self"] = (method, _, request, cancellationToken) => method == "GET"
                ? _sessions.IdentifySelfAsync(request, cancellationToken)
                : Immediate(NotFound()),
            ["models"] = (method, segments, request, cancellationToken) =>
                _models.ModelsAsync(method, segments, request, cancellationToken),
            ["model-groups"] = (method, segments, request, cancellationToken) =>
                _modelGroups.HandleAsync(method, segments, request, cancellationToken),
            ["runtimes"] = (method, segments, request, cancellationToken) =>
                _runtime.RuntimesAsync(method, segments, request, cancellationToken),
            ["sessions"] = (method, segments, request, cancellationToken) =>
                _sessions.SessionsAsync(method, segments, request, cancellationToken),
            ["gateway"] = (method, segments, _, cancellationToken) =>
                _sessions.GatewayAsync(method, segments, cancellationToken),
            ["settings"] = (method, segments, request, cancellationToken) =>
                _settings.SettingsAsync(method, segments, request, cancellationToken),
            ["logs"] = (method, segments, request, cancellationToken) =>
                _logs.LogsAsync(method, segments, request, cancellationToken),
            ["metrics"] = (method, segments, request, cancellationToken) =>
                segments.Length == 3 && method == "GET"
                    ? _sessions.AllMetricsAsync(cancellationToken)
                    : _usageMetrics.HandleAsync(method, segments, request, cancellationToken),
            ["jobs"] = (method, segments, _, cancellationToken) =>
                _jobs.JobsAsync(method, segments, cancellationToken),
            ["benchmarks"] = (method, segments, request, cancellationToken) =>
                _benchmarks.HandleAsync(method, segments, request, cancellationToken),
            ["huggingface"] = (method, segments, request, cancellationToken) =>
                _jobs.HuggingFaceAsync(method, segments, request, cancellationToken),
            ["operations"] = (method, segments, request, cancellationToken) =>
                _operations.HandleAsync(method, segments, request, cancellationToken)
        };

        var catalogRoots = ControlRouteCatalog.Groups.Select(group => group.Root).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!catalogRoots.SetEquals(handlers.Keys))
            throw new InvalidOperationException("The control route catalog and request handlers are inconsistent.");
        return handlers;
    }

    private static Task<LocalControlApiResponse> Immediate(LocalControlApiResponse response)
        => Task.FromResult(response);

    private static LocalControlApiResponse NotFound()
        => Error(404, "Not found.");
}
