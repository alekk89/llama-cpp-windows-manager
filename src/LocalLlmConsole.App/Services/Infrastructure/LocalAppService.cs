
namespace LocalLlmConsole.Services;

public sealed class LocalAppService : ILocalAppServiceHost
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ApiSecurity _security = new();
    private readonly HttpListenerRequestTracker _requestTracker = new();
    private readonly StateStore _stateStore;
    private readonly JobEngine _jobs;
    private readonly LocalControlApi? _controlApi;
    private readonly LocalControlDiscoveryService? _discovery;
    private Task? _loop;
    private int _disposed;

    public Uri BaseUri { get; }
    public string SessionToken => _security.SessionToken;
    public string LastListenerError { get; private set; } = "";

    public LocalAppService(
        StateStore stateStore,
        JobEngine jobs,
        int port,
        LocalControlApi? controlApi = null,
        LocalControlDiscoveryService? discovery = null)
    {
        _stateStore = stateStore;
        _jobs = jobs;
        _controlApi = controlApi;
        _discovery = discovery;
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(BaseUri.ToString());
    }

    public async Task StartAsync()
    {
        await _jobs.RecoverAfterRestartAsync();
        _listener.Start();
        _discovery?.Publish(BaseUri, _security.SessionToken);
        _loop = ListenAsync(_stop.Token);
    }

    private Task ListenAsync(CancellationToken cancellationToken)
        => HttpListenerAcceptLoop.RunAsync(
            _listener,
            QueueRequest,
            ex => LastListenerError = $"Local app service listener error: {ex.Message}",
            cancellationToken);

    private void QueueRequest(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var task = HandleAsync(context, cancellationToken);
        _requestTracker.Track(context, task, "Local app service request handler failed.");
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            AddSecurityHeaders(context);
            if (!_security.IsLocalHostHeaderAllowed(context.Request.Headers["Host"], BaseUri.Port))
            {
                await WriteJsonAsync(context, 403, new { ok = false, error = "Non-local Host header rejected." });
                return;
            }

            if (context.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (!_security.IsLocalOriginAllowed(context.Request.Headers["Origin"]))
            {
                await WriteJsonAsync(context, 403, new { ok = false, error = "Non-local browser origin rejected." });
                return;
            }
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                && path != "/api/health"
                && !_security.IsAuthorized(context.Request.Headers["Authorization"]))
            {
                await WriteJsonAsync(context, 401, new { ok = false, error = "Missing or invalid local API token." });
                return;
            }

            if (path == "/" || path == "/api/health")
            {
                await WriteJsonAsync(context, 200, new { ok = true, app = "llama.cpp Windows Manager", auth = "required", tokenHint = "WPF shell holds token in memory" });
                return;
            }
            if (path == "/api/jobs")
            {
                await WriteJsonAsync(context, 200, new { ok = true, jobs = RedactedJobs(await _stateStore.ListJobsAsync()) });
                return;
            }
            if (path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase) && _controlApi is not null)
            {
                var request = await BuildControlRequestAsync(context.Request, path, cancellationToken);
                var response = await _controlApi.HandleAsync(request, cancellationToken);
                await WriteJsonAsync(context, response.StatusCode, response.Body);
                return;
            }

            await WriteJsonAsync(context, 404, new { ok = false, error = "Not found." });
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            try { await WriteJsonAsync(context, 400, new { ok = false, error = ex.Message }); }
            catch (Exception writeEx)
            {
                Trace.TraceWarning($"Local app service failed to write validation response: {writeEx}");
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Local app service request failed: {ex}");
            try { await WriteJsonAsync(context, 500, new { ok = false, error = "Internal server error." }); }
            catch (Exception writeEx)
            {
                Trace.TraceWarning($"Local app service failed to write error response: {writeEx}");
            }
        }
    }

    private static object[] RedactedJobs(IReadOnlyList<LocalLlmConsole.Models.JobRecord> jobs)
        => jobs.Select(job => new
        {
            job.Id,
            job.Kind,
            Status = job.Status.ToString(),
            LogFile = string.IsNullOrWhiteSpace(job.LogPath) ? "" : Path.GetFileName(job.LogPath),
            job.CreatedAt,
            job.UpdatedAt
        }).ToArray();

    private void AddSecurityHeaders(HttpListenerContext context)
    {
        var origin = context.Request.Headers["Origin"];
        if (!string.IsNullOrWhiteSpace(origin) && _security.IsLocalOriginAllowed(origin))
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,PUT,PATCH,DELETE,OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "content-type,authorization";
        context.Response.Headers["Vary"] = "Origin";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, int status, object value)
    {
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static async Task<LocalControlRequest> BuildControlRequestAsync(
        HttpListenerRequest request,
        string path,
        CancellationToken cancellationToken)
    {
        const int maxBodyBytes = 1024 * 1024;
        var body = request.HasEntityBody
            ? await ControlRequestBodyReader.ReadJsonObjectAsync(
                request.InputStream,
                request.ContentEncoding ?? Encoding.UTF8,
                request.ContentLength64,
                maxBodyBytes,
                cancellationToken)
            : null;

        var query = request.QueryString.AllKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToDictionary(key => key!, key => request.QueryString[key!] ?? "", StringComparer.OrdinalIgnoreCase);
        var headers = request.Headers.AllKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToDictionary(key => key!, key => request.Headers[key!] ?? "", StringComparer.OrdinalIgnoreCase);
        return new LocalControlRequest(request.HttpMethod, path, query, body, headers);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _discovery?.Remove();
        _stop.Cancel();
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        if (_loop is not null)
            await HttpListenerRequestTracker.ObserveCompletionAsync(
                _loop,
                "Local control listener completed with an observed exception:");
        await _requestTracker.AbortAndDrainAsync(
            "Local control request handlers completed with an observed exception:");

        _stop.Dispose();
    }

}
