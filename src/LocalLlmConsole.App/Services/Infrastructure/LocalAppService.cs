
namespace LocalLlmConsole.Services;

public sealed class LocalAppService : ILocalAppServiceHost
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ApiSecurity _security = new();
    private readonly object _requestHandlersLock = new();
    private readonly HashSet<Task> _requestHandlers = [];
    private readonly StateStore _stateStore;
    private readonly JobEngine _jobs;
    private readonly LocalControlApi? _controlApi;
    private readonly LocalControlDiscoveryService? _discovery;
    private Task? _loop;
    private int _listenerErrorCount;

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
        _loop = Task.Run(() => ListenAsync(_stop.Token));
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
                LastListenerError = $"Local app service listener error: {ex.Message}";
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
                TraceFaultedTask(completed, "Local app service request handler failed.");
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
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
        if (request.ContentLength64 > maxBodyBytes)
            throw new InvalidOperationException("Control API request bodies are limited to 1 MiB.");

        JsonObject? body = null;
        if (request.HasEntityBody)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var buffer = new char[8192];
            var text = new StringBuilder();
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (count == 0) break;
                text.Append(buffer, 0, count);
                if (Encoding.UTF8.GetByteCount(text.ToString()) > maxBodyBytes)
                    throw new InvalidOperationException("Control API request bodies are limited to 1 MiB.");
            }
            if (!string.IsNullOrWhiteSpace(text.ToString()))
                body = JsonNode.Parse(text.ToString()) as JsonObject
                    ?? throw new InvalidOperationException("Control API request body must be a JSON object.");
        }

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
        _discovery?.Remove();
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
            Trace.TraceWarning($"Local app service background task completed with an observed exception: {ex}");
        }
    }

    private static void TraceFaultedTask(Task task, string message)
    {
        if (!task.IsFaulted || task.Exception is null) return;
        Trace.TraceError($"{message} {task.Exception}");
    }
}
