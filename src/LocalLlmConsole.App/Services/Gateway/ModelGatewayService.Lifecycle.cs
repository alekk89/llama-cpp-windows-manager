namespace LocalLlmConsole.Services;

public sealed partial class ModelGatewayService
{
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        var listenerPrefix = await GatewayUrlReservationService.PreferredListenerPrefixAsync(
            _options.Port,
            _options.AllowLanAccess,
            cancellationToken);
        _listener.Prefixes.Add(listenerPrefix);

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            var registered = await GatewayUrlReservationService.TryRegisterAsync(
                _options.Port,
                _options.AllowLanAccess,
                cancellationToken);
            if (registered)
                _listener.Start();
            else
                throw new InvalidOperationException(
                    $"Cannot start the auto-load gateway on port {_options.Port}.{Environment.NewLine}" +
                    $"Windows requires a one-time permission to listen on this port.{Environment.NewLine}" +
                    $"Please approve the UAC prompt that appears, or run this command as Administrator:{Environment.NewLine}" +
                    NetshCommand(listenerPrefix),
                    ex);
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Cannot start the auto-load gateway on port {_options.Port}.{Environment.NewLine}" +
                $"Windows blocked the listener: {ex.Message}.{Environment.NewLine}" +
                $"Run this command as Administrator and restart:{Environment.NewLine}" +
                NetshCommand(listenerPrefix),
                ex);
        }

        _loop = ListenAsync(_stop.Token);
    }

    private void ValidateOptions()
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("The model gateway is disabled.");
        if (_options.Port is < 1 or > 65535)
            throw new InvalidOperationException("Gateway port must be between 1 and 65535.");
        if (!_options.RequireApiKeyAuth && _options.AllowLanAccess)
            throw new InvalidOperationException("API-key authentication can be disabled only for a local-only model gateway.");
        if (_options.RequireApiKeyAuth
            && (string.IsNullOrWhiteSpace(_options.ApiKey) || !ApiSecurity.IsStrongBearerSecret(_options.ApiKey)))
            throw new InvalidOperationException("The model gateway requires a strong API key.");
        if (!_options.RequireApiKeyAuth && !string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("The model gateway API key must be empty when authentication is disabled.");
        if (_options.MaxRequestBodyBytes <= 0)
            throw new InvalidOperationException("Gateway request body limit must be greater than zero.");
        if (_options.MaxConcurrentRequests <= 0)
            throw new InvalidOperationException("Gateway concurrent request limit must be greater than zero.");
        if (_options.RequestBodyTimeoutSeconds <= 0)
            throw new InvalidOperationException("Gateway request body timeout must be greater than zero.");
    }

    private Task ListenAsync(CancellationToken cancellationToken)
        => HttpListenerAcceptLoop.RunAsync(
            _listener,
            QueueRequest,
            ex => LastListenerError = $"Model gateway listener error: {ex.Message}",
            cancellationToken);

    private void QueueRequest(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!_requestSlots.Wait(0))
        {
            RejectOverloadedRequest(context);
            return;
        }

        var task = HandleAcceptedRequestAsync(context, cancellationToken);
        _requestTracker.Track(context, task, "Model gateway request handler failed.");
    }

    private async Task HandleAcceptedRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await HandleAsync(context, cancellationToken);
        }
        finally
        {
            _requestSlots.Release();
        }
    }

    private void RejectOverloadedRequest(HttpListenerContext context)
    {
        try
        {
            _accessPolicy.AddResponseHeaders(context);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                error = new
                {
                    message = "The model gateway is temporarily at request capacity.",
                    type = "gateway_overloaded",
                    code = "gateway_overloaded"
                }
            }));
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            context.Response.Headers["Retry-After"] = "1";
            context.Response.OutputStream.Write(body);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Model gateway could not write an overload response: {ex.Message}");
            ModelGatewayResponseWriter.TryClose(context.Response);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _stop.Cancel();
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        if (_loop is not null)
            await HttpListenerRequestTracker.ObserveCompletionAsync(
                _loop,
                "Model gateway listener completed with an observed exception:");
        var drain = await _requestTracker.AbortAndDrainAsync(
            "Model gateway request handlers completed with an observed exception:");

        if (drain.CompletedWithinTimeout)
            DisposeOwnedResources();
        else
            Volatile.Write(
                ref _ownedResourceDisposalCompletion,
                DisposeOwnedResourcesAfterDrainAsync(drain.Completion));
    }

    private async Task DisposeOwnedResourcesAfterDrainAsync(Task completion)
    {
        try
        {
            await completion;
        }
        catch
        {
            // The request tracker owns reporting handler failures.
        }
        DisposeOwnedResources();
    }

    private void DisposeOwnedResources()
    {
        if (Interlocked.Exchange(ref _ownedResourcesDisposed, 1) != 0) return;
        _upstreamProxy.Dispose();
        _requestSlots.Dispose();
        _loadGate.Dispose();
        _stop.Dispose();
    }

    private static string NetshCommand(string listenerPrefix)
        => $"netsh http add urlacl url={listenerPrefix} user=\"%USERDOMAIN%\\%USERNAME%\"";

}
