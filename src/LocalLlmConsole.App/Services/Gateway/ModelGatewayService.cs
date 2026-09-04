namespace LocalLlmConsole.Services;

public sealed partial class ModelGatewayService : IModelGatewayHost
{
    private readonly ModelGatewayOptions _options;
    private readonly IModelGatewayRuntimeController _runtime;
    private readonly ModelGatewayRequestAccessPolicy _accessPolicy;
    private readonly ModelGatewayUpstreamProxy _upstreamProxy;
    private readonly GatewayPerformanceTracker? _performance;
    private HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _requestSlots;
    private readonly ModelGatewayRequestGate _modelRequestGate = new();
    private readonly HttpListenerRequestTracker _requestTracker = new();
    private Task? _loop;
    private Task _ownedResourceDisposalCompletion = Task.CompletedTask;
    private int _disposed;
    private int _ownedResourcesDisposed;

    public ModelGatewayService(
        ModelGatewayOptions options,
        IModelGatewayRuntimeController runtime,
        ModelGatewayUpstreamProxy? upstreamProxy = null,
        GatewayPerformanceTracker? performance = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtime);

        _options = options;
        _runtime = runtime;
        _accessPolicy = new(options);
        _upstreamProxy = upstreamProxy ?? new ModelGatewayUpstreamProxy();
        _performance = performance;
        _requestSlots = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentRequests));
    }

    public Uri BaseUri => new(_options.LocalOpenAiBaseUrl);
    public string LastListenerError { get; private set; } = "";
    public bool IsListening => _listener.IsListening && _loop is { IsCompleted: false };
    internal bool OwnedResourcesDisposed => Volatile.Read(ref _ownedResourcesDisposed) != 0;
    internal Task OwnedResourceDisposalCompletion => Volatile.Read(ref _ownedResourceDisposalCompletion);

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            _accessPolicy.AddResponseHeaders(context);
            if (!_accessPolicy.IsRemoteEndpointAllowed(context.Request.RemoteEndPoint?.Address))
            {
                await ModelGatewayResponseWriter.WriteJsonAsync(context, 403, new { error = new { message = "Remote endpoint rejected.", type = "forbidden" } }, cancellationToken);
                return;
            }
            if (!_accessPolicy.IsHostAllowed(context.Request.Headers["Host"]))
            {
                await ModelGatewayResponseWriter.WriteJsonAsync(context, 403, new { error = new { message = "Host header rejected.", type = "forbidden" } }, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            if (!_accessPolicy.IsAuthorized(context.Request))
            {
                await ModelGatewayResponseWriter.WriteJsonAsync(context, 401, new { error = new { message = "Missing or invalid API key.", type = "unauthorized" } }, cancellationToken);
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            {
                await ModelGatewayResponseWriter.WriteJsonAsync(context, 200, new { ok = true, gateway = "model-auto-load", autoLoadModels = _options.AutoLoadModels }, cancellationToken);
                return;
            }

            if (path.Equals("/running", StringComparison.OrdinalIgnoreCase))
            {
                await ModelGatewayResponseWriter.WriteJsonAsync(
                    context,
                    200,
                    new { data = ModelGatewayResponseWriter.RunningModelRows(await _runtime.RunningSessionsAsync(cancellationToken)) },
                    cancellationToken);
                return;
            }

            if (path.Equals("/v1/models", StringComparison.OrdinalIgnoreCase)
                && context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                await ModelGatewayResponseWriter.WriteJsonAsync(
                    context,
                    200,
                    ModelGatewayResponseWriter.ModelsResponse(await DiscoverableModelsAsync(cancellationToken)),
                    cancellationToken);
                return;
            }

            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || !ModelGatewayRequestResolver.IsProxiedPostPath(path))
            {
                await ModelGatewayResponseWriter.WriteJsonAsync(context, 404, new { error = new { message = "Not found.", type = "not_found" } }, cancellationToken);
                return;
            }

            await ProxyModelRequestAsync(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ModelGatewayResponseWriter.TryClose(context.Response);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Model gateway request failed: {ex}");
            try
            {
                await ModelGatewayResponseWriter.WriteJsonAsync(context, 500, new { error = new { message = "An internal gateway error occurred.", type = "gateway_error" } }, CancellationToken.None);
            }
            catch (Exception writeEx)
            {
                Trace.TraceWarning($"Model gateway failed to write error response: {writeEx}");
                ModelGatewayResponseWriter.TryClose(context.Response);
            }
        }
    }

    private async Task ProxyModelRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        byte[] body;
        try
        {
            body = await ReadBodyAsync(context.Request, _options.MaxRequestBodyBytes, cancellationToken);
        }
        catch (ModelGatewayRequestBodyTooLargeException ex)
        {
            ObserveRejectedRequest(started);
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 413, new { error = new { message = ex.Message, type = "request_too_large" } }, cancellationToken);
            return;
        }
        catch (ModelGatewayRequestBodyTimeoutException ex)
        {
            ObserveRejectedRequest(started);
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 408, new { error = new { message = ex.Message, type = "request_timeout" } }, cancellationToken);
            return;
        }

        var requestedModel = ModelGatewayRequestResolver.ExtractRequestedModel(body);
        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            ObserveRejectedRequest(started);
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 400, new { error = new { message = "Request body must include a model value.", type = "invalid_request_error" } }, cancellationToken);
            return;
        }

        var route = ModelGatewayRequestResolver.ResolveModel(await _runtime.ListModelsAsync(cancellationToken), requestedModel);
        if (route is null)
        {
            ObserveRejectedRequest(started);
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 404, new { error = new { message = $"Unknown model '{requestedModel}'.", type = "model_not_found" } }, cancellationToken);
            return;
        }

        using var modelRequestLease = await _modelRequestGate.EnterAsync(
            route.Model.Id,
            route.Profile.Id,
            cancellationToken);
        LoadedModelSessionSnapshot? session;
        try
        {
            session = await EnsureLoadedAsync(route, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ObserveRejectedRequest(started);
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 503, ModelGatewayResponseWriter.GatewayError(
                ModelGatewayResponseWriter.GatewayClientLoadError(route, requestedModel, ex),
                "model_load_failed",
                "model_load_failed"), cancellationToken);
            return;
        }

        if (session is null)
        {
            ObserveRejectedRequest(started);
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 503, ModelGatewayResponseWriter.GatewayError(
                $"{route.Name} is not loaded and gateway auto-loading is disabled. Load this profile in the Manager or choose a loaded profile from /v1/models.",
                "model_not_loaded",
                "model_not_loaded"), cancellationToken);
            return;
        }

        try
        {
            var upstreamBody = ModelGatewayRequestResolver.BodyForRuntime(body, session.LaunchSettings);
            await _upstreamProxy.ForwardAsync(context, session, upstreamBody, cancellationToken, started.Elapsed);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or IOException)
        {
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 502, ModelGatewayResponseWriter.GatewayError(
                $"The direct endpoint for {route.Name} at {RuntimeEndpointService.LocalOpenAiBaseUrl(session.LaunchSettings)} did not return a usable response. Details: {ModelGatewayResponseWriter.InnermostMessage(ex)}.",
                "upstream_unavailable",
                "upstream_unavailable"), cancellationToken);
        }
    }

    private void ObserveRejectedRequest(Stopwatch started)
        => _performance?.Observe(false, started.Elapsed, null, null);

    private async Task<IReadOnlyList<ModelGatewayModelRoute>> DiscoverableModelsAsync(CancellationToken cancellationToken)
    {
        var routes = await _runtime.ListModelsAsync(cancellationToken);
        if (_options.AutoLoadModels) return routes;
        var sessions = await _runtime.RunningSessionsAsync(cancellationToken);
        // Filter after catalog naming so duplicate alias suffixes never change with load state.
        return routes.Where(route => sessions.Any(route.MatchesRunningSession)).ToArray();
    }

    private async Task<LoadedModelSessionSnapshot?> EnsureLoadedAsync(ModelGatewayModelRoute route, CancellationToken cancellationToken)
    {
        var running = (await _runtime.RunningSessionsAsync(cancellationToken))
            .FirstOrDefault(session => route.MatchesRunningSession(session));
        if (running is not null) return running;
        if (!_options.AutoLoadModels) return null;

        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            running = (await _runtime.RunningSessionsAsync(cancellationToken))
                .FirstOrDefault(session => route.MatchesRunningSession(session));
            return running ?? await _runtime.EnsureModelLoadedAsync(route, _options.SwapPolicy, cancellationToken);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task<byte[]> ReadBodyAsync(HttpListenerRequest request, long maxRequestBodyBytes, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestBodyTimeoutSeconds));
        try
        {
            return await ModelGatewayRequestBodyReader.ReadBodyBufferAsync(
                request.InputStream,
                request.ContentLength64,
                maxRequestBodyBytes,
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ModelGatewayRequestBodyTimeoutException(
                $"Gateway request body was not received within {_options.RequestBodyTimeoutSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
        }
    }

}
