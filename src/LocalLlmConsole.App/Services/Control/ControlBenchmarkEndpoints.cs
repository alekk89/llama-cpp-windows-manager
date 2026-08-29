using System.Globalization;
using System.Text.Json.Serialization;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed record LocalControlBenchmarkPlanRequest(BenchmarkPlan Plan);
public sealed record LocalControlBenchmarkRunRequest(BenchmarkPlan Plan, bool Confirm = false, bool DryRun = false);
public sealed record LocalControlBenchmarkCompareRequest(
    string BaselineRunId = "",
    string CandidateRunId = "",
    bool IncludePartialAttempts = false);

internal sealed class ControlBenchmarkEndpoints : ControlEndpointHandler
{
    private static readonly JsonSerializerOptions BenchmarkPlanJsonOptions = CreateBenchmarkPlanJsonOptions();
    public ControlBenchmarkEndpoints(ControlEndpointContext context) : base(context) { }

    internal async Task<LocalControlApiResponse> HandleAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 4 && segments[3].Equals("schema", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return Ok(new { ok = true, contract = ControlBenchmarkContract.Schema() });
        if (segments.Length == 4 && segments[3].Equals("presets", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return Ok(ControlBenchmarkContract.Presets());
        if (segments.Length == 4 && segments[3].Equals("capabilities", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            var capabilities = await Service().RuntimeCapabilitiesAsync(
                request.Query.TryGetValue("runtime", out var runtime) ? runtime : "",
                request.Query.TryGetValue("wslDistro", out var distro) ? distro : "",
                cancellationToken);
            return Ok(new { ok = true, runtimes = capabilities.Select(ControlBenchmarkContract.RuntimeCapability).ToArray() });
        }
        if (segments.Length == 4 && segments[3].Equals("compare", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var body = BenchmarkBody<LocalControlBenchmarkCompareRequest>(request.Body);
            if (string.IsNullOrWhiteSpace(body.BaselineRunId) || string.IsNullOrWhiteSpace(body.CandidateRunId))
                throw new InvalidOperationException("'baselineRunId' and 'candidateRunId' are required.");
            return Ok(ControlBenchmarkContract.Comparison(await Service().CompareAsync(
                body.BaselineRunId,
                body.CandidateRunId,
                body.IncludePartialAttempts,
                cancellationToken)));
        }
        var service = Service();
        if (segments.Length == 3 && method == "GET")
            return Ok(new
            {
                ok = true,
                runs = await service.ListAsync(
                    IntQuery(request.Query, "limit", 100),
                    cancellationToken,
                    Math.Max(IntQuery(request.Query, "offset", 0), 0))
            });
        if (segments.Length == 4 && segments[3].Equals("validate", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var body = BenchmarkBody<LocalControlBenchmarkPlanRequest>(request.Body);
            var preview = await service.ValidateAsync(body.Plan, cancellationToken);
            return Ok(new { ok = preview.IsValid, preview });
        }
        if (segments.Length == 4 && segments[3].Equals("run", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var body = BenchmarkBody<LocalControlBenchmarkRunRequest>(request.Body);
            var preview = await service.ValidateAsync(body.Plan, cancellationToken);
            if (body.DryRun) return Ok(new { ok = preview.IsValid, dryRun = true, preview });
            if (!preview.IsValid) return Error(400, string.Join(Environment.NewLine, preview.Errors));
            var run = await service.StartAsync(body.Plan, body.Confirm, cancellationToken);
            return new LocalControlApiResponse(202, new { ok = true, run });
        }
        if (segments.Length < 4) return Error(404, "Not found.");
        var jobId = segments[3];
        if (segments.Length == 4 && method == "GET")
            return Ok(new { ok = true, run = await service.InspectAsync(jobId, cancellationToken) });
        if (segments.Length == 4 && method == "DELETE")
        {
            var deleted = await service.DeleteAsync(jobId, BoolQuery(request.Query, "confirm"), cancellationToken);
            return Ok(new { ok = true, deletedRunId = deleted.Job.Id, deletedResultRows = deleted.PersistedResultRows });
        }
        if (segments.Length == 5 && segments[4].Equals("wait", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            var after = LongQuery(request.Query, "afterRevision", -1);
            var timeout = Math.Clamp(IntQuery(request.Query, "timeoutSeconds", 30), 1, 60);
            return Ok(new { ok = true, run = await service.WaitForRevisionAsync(jobId, after, TimeSpan.FromSeconds(timeout), cancellationToken) });
        }
        if (segments.Length == 5 && segments[4].Equals("results", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            await service.InspectAsync(jobId, cancellationToken);
            var results = await _deps.StateStore.ListBenchmarkResultsAsync(
                jobId,
                Math.Clamp(IntQuery(request.Query, "limit", 200), 1, 1000),
                Math.Max(IntQuery(request.Query, "offset", 0), 0),
                includePartialAttempts: !request.Query.ContainsKey("includePartial") || BoolQuery(request.Query, "includePartial"),
                cancellationToken);
            return Ok(new { ok = true, jobId, results });
        }
        if (segments.Length == 5 && segments[4].Equals("plan", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            var run = await service.InspectAsync(jobId, cancellationToken);
            return Ok(new { ok = true, jobId, plan = run.Payload.Plan });
        }
        if (segments.Length == 5 && segments[4].Equals("log", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            var run = await service.InspectAsync(jobId, cancellationToken);
            if (!LogFileService.TryValidateWorkspaceLogFile(_deps.WorkspaceRoot, run.Job.LogPath, out var fullPath, out var error))
                throw new KeyNotFoundException(error);
            var text = LogFileService.Tail(fullPath, Math.Clamp(IntQuery(request.Query, "tail", 80000), 1000, 250000));
            return Ok(new
            {
                ok = true,
                jobId,
                file = Path.GetFileName(fullPath),
                path = fullPath,
                text = LogFileService.RedactSensitiveText(text, _deps.Actions.GetSettings().ModelApiKey)
            });
        }
        if (segments.Length == 5 && segments[4].Equals("export", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            await service.InspectAsync(jobId, cancellationToken);
            var results = await BenchmarkExportService.LoadAllAsync(_deps.StateStore, jobId, true, cancellationToken);
            request.Query.TryGetValue("format", out var format);
            if (string.IsNullOrWhiteSpace(format) || format.Equals("json", StringComparison.OrdinalIgnoreCase))
                return Ok(new { ok = true, jobId, format = "json", content = BenchmarkExportService.Json(results) });
            if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
                return Ok(new { ok = true, jobId, format = "csv", content = BenchmarkExportService.Csv(results) });
            return Error(400, "Benchmark export format must be json or csv.");
        }
        if (segments.Length == 5 && method == "POST")
        {
            switch (segments[4].ToLowerInvariant())
            {
                case "pause": await service.PauseAsync(jobId, cancellationToken); break;
                case "resume": await service.ResumeAsync(jobId, cancellationToken); break;
                case "cancel": await service.CancelAsync(jobId, cancellationToken); break;
                default: return Error(404, "Not found.");
            }
            return Ok(new { ok = true, run = await service.InspectAsync(jobId, cancellationToken) });
        }
        return Error(404, "Not found.");
    }

    private BenchmarkApplicationService Service()
        => _deps.Benchmarks?.Invoke()
           ?? throw new InvalidOperationException("Benchmark services are unavailable.");

    private static int IntQuery(IReadOnlyDictionary<string, string> query, string name, int fallback)
        => query.TryGetValue(name, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    private static long LongQuery(IReadOnlyDictionary<string, string> query, string name, long fallback)
        => query.TryGetValue(name, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static T BenchmarkBody<T>(JsonObject? body)
        => (body ?? new JsonObject()).Deserialize<T>(BenchmarkPlanJsonOptions)
            ?? throw new InvalidOperationException($"Request body could not be read as {typeof(T).Name}.");

    private static JsonSerializerOptions CreateBenchmarkPlanJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonOptions);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

}
