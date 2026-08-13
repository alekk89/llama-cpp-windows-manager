namespace LocalLlmConsole.Services;

public sealed class ModelGatewayService : IModelGatewayHost
{
    private readonly ModelGatewayOptions _options;
    private readonly IModelGatewayRuntimeController _runtime;
    private readonly ModelGatewayRequestAccessPolicy _accessPolicy;
    private readonly ModelGatewayUpstreamProxy _upstreamProxy;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly ModelGatewayRequestGate _modelRequestGate = new();
    private readonly object _requestHandlersLock = new();
    private readonly HashSet<Task> _requestHandlers = [];
    private Task? _loop;
    private int _listenerErrorCount;

    public ModelGatewayService(
        ModelGatewayOptions options,
        IModelGatewayRuntimeController runtime,
        ModelGatewayUpstreamProxy? upstreamProxy = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtime);

        _options = options;
        _runtime = runtime;
        _accessPolicy = new(options);
        _upstreamProxy = upstreamProxy ?? new ModelGatewayUpstreamProxy();
        _listener.Prefixes.Add(options.ListenerPrefix);
    }

    public Uri BaseUri => new(_options.LocalOpenAiBaseUrl);
    public string LastListenerError { get; private set; } = "";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("The model gateway is disabled.");
        if (_options.Port is < 1 or > 65535)
            throw new InvalidOperationException("Gateway port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || !ApiSecurity.IsStrongBearerSecret(_options.ApiKey))
            throw new InvalidOperationException("The model gateway requires a strong API key.");
        if (_options.MaxRequestBodyBytes <= 0)
            throw new InvalidOperationException("Gateway request body limit must be greater than zero.");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5) // ERROR_ACCESS_DENIED
        {
            // Attempt one-time URL ACL registration via UAC elevation.
            // After registration succeeds, the listener can bind without admin.
            var registered = await GatewayUrlReservationService.TryRegisterAsync(
                _options.Port,
                _options.AllowLanAccess,
                cancellationToken);

            if (registered)
            {
                // The netsh command succeeded — retry binding.
                _listener.Start();
            }
            else
            {
                throw new InvalidOperationException(
                    $"Cannot start the auto-load gateway on port {_options.Port}.{Environment.NewLine}" +
                    $"Windows requires a one-time permission to listen on this port.{Environment.NewLine}" +
                    $"Please approve the UAC prompt that appears, or run this command as Administrator:{Environment.NewLine}" +
                    $"{NetshCommand(_options)}",
                    ex);
            }
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Cannot start the auto-load gateway on port {_options.Port}.{Environment.NewLine}" +
                $"Windows blocked the listener: {ex.Message}.{Environment.NewLine}" +
                $"Run this command as Administrator and restart:{Environment.NewLine}" +
                $"{NetshCommand(_options)}",
                ex);
        }

        _loop = Task.Run(() => ListenAsync(_stop.Token), cancellationToken);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (!_stop.IsCancellationRequested && _listener.IsListening)
            {
                LastListenerError = $"Model gateway listener error: {ex.Message}";
                if (++_listenerErrorCount >= 3)
                    return;
                await Task.Delay(250, cancellationToken);
                continue;
            }

            QueueRequest(context, cancellationToken);
            _listenerErrorCount = 0;
        }
    }

    private void QueueRequest(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var task = Task.Run(() => HandleAsync(context, cancellationToken), cancellationToken);
        lock (_requestHandlersLock)
        {
            _requestHandlers.Add(task);
        }

        task.ContinueWith(
            completed =>
            {
                lock (_requestHandlersLock)
                {
                    _requestHandlers.Remove(completed);
                }
                TraceFaultedTask(completed, "Model gateway request handler failed.");
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            _accessPolicy.AddResponseHeaders(context);
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
                await ModelGatewayResponseWriter.WriteJsonAsync(context, 200, new { ok = true, gateway = "model-auto-load" }, cancellationToken);
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
                    ModelGatewayResponseWriter.ModelsResponse(await _runtime.ListModelsAsync(cancellationToken)),
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
        byte[] body;
        try
        {
            body = await ReadBodyAsync(context.Request, _options.MaxRequestBodyBytes, cancellationToken);
        }
        catch (ModelGatewayRequestBodyTooLargeException ex)
        {
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 413, new { error = new { message = ex.Message, type = "request_too_large" } }, cancellationToken);
            return;
        }

        var requestedModel = ModelGatewayRequestResolver.ExtractRequestedModel(body);
        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 400, new { error = new { message = "Request body must include a model value.", type = "invalid_request_error" } }, cancellationToken);
            return;
        }

        var route = ModelGatewayRequestResolver.ResolveModel(await _runtime.ListModelsAsync(cancellationToken), requestedModel);
        if (route is null)
        {
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 404, new { error = new { message = $"Unknown model '{requestedModel}'.", type = "model_not_found" } }, cancellationToken);
            return;
        }

        using var modelRequestLease = await _modelRequestGate.EnterAsync(
            route.Model.Id,
            route.Profile.Id,
            cancellationToken);
        LoadedModelSessionSnapshot session;
        try
        {
            session = await EnsureLoadedAsync(route, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 503, ModelGatewayResponseWriter.GatewayError(
                ModelGatewayResponseWriter.GatewayClientLoadError(route, requestedModel, ex),
                "model_load_failed",
                "model_load_failed"), cancellationToken);
            return;
        }

        try
        {
            await _upstreamProxy.ForwardAsync(context, session, body, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or IOException)
        {
            await ModelGatewayResponseWriter.WriteJsonAsync(context, 502, ModelGatewayResponseWriter.GatewayError(
                $"Gateway loaded {route.Name}, but the direct endpoint {RuntimeEndpointService.LocalOpenAiBaseUrl(session.LaunchSettings)} did not return a usable response. Details: {ModelGatewayResponseWriter.InnermostMessage(ex)}.",
                "upstream_unavailable",
                "upstream_unavailable"), cancellationToken);
        }
    }

    private async Task<LoadedModelSessionSnapshot> EnsureLoadedAsync(ModelGatewayModelRoute route, CancellationToken cancellationToken)
    {
        var running = (await _runtime.RunningSessionsAsync(cancellationToken))
            .FirstOrDefault(session => route.MatchesRunningSession(session));
        if (running is not null) return running;

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

    private static Task<byte[]> ReadBodyAsync(HttpListenerRequest request, long maxRequestBodyBytes, CancellationToken cancellationToken)
        => Task.Run(() => ModelGatewayRequestBodyReader.ReadBodyBuffer(request.InputStream, request.ContentLength64, maxRequestBodyBytes), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        if (_loop is not null)
        {
            var completed = await Task.WhenAny(_loop, Task.Delay(1000));
            if (completed == _loop) await ObserveCompletionAsync(_loop);
        }

        Task[] activeHandlers;
        lock (_requestHandlersLock)
        {
            activeHandlers = _requestHandlers.ToArray();
        }

        if (activeHandlers.Length > 0)
        {
            var allHandlers = Task.WhenAll(activeHandlers);
            var completed = await Task.WhenAny(allHandlers, Task.Delay(1000));
            if (completed == allHandlers) await ObserveCompletionAsync(allHandlers);
        }

        _upstreamProxy.Dispose();
        _loadGate.Dispose();
        _stop.Dispose();
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Model gateway background task completed with an observed exception: {ex}");
        }
    }

    private static string NetshCommand(ModelGatewayOptions options)
        => $"netsh http add urlacl url={GatewayUrlReservationService.ListenerPrefixForPort(options.Port, options.AllowLanAccess)} user=\"%USERDOMAIN%\\%USERNAME%\"";

    private static void TraceFaultedTask(Task task, string message)
    {
        if (!task.IsFaulted || task.Exception is null) return;
        Trace.TraceError($"{message} {task.Exception}");
    }
}
