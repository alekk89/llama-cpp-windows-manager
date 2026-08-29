using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ControlBenchmarkApiTests : ManagerRegressionTestBase
{
    private const string ResultJson = """{"build_commit":"test","build_number":1,"cpu_info":"CPU","gpu_info":"GPU","backends":"CUDA","model_filename":"model.gguf","model_type":"test","model_size":1,"model_n_params":1,"n_prompt":512,"n_gen":128,"n_depth":0,"n_batch":2048,"n_ubatch":512,"n_threads":8,"n_gpu_layers":-1,"n_cpu_moe":0,"type_k":"f16","type_v":"f16","split_mode":"layer","main_gpu":0,"no_kv_offload":false,"flash_attn":"on","tensor_split":"1,1","load_mode":"mmap","avg_ns":1000,"stddev_ns":20,"avg_ts":100.0,"stddev_ts":1.5,"test_time":"2026-08-29T00:00:00Z"}""";

    [Fact]
    public async Task BenchmarkApiExposesDiscoveryCapabilitiesArtifactsComparisonAndConfirmedDelete()
    {
        var root = CreateTempRoot();
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = new AppServiceFactory(root);
        await using var store = new StateStore(factory.DatabasePath);
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var catalog = factory.CreateModelCatalogService(store);
        var profiles = factory.CreateModelLaunchProfileService(store, sessions);
        var runtimes = factory.CreateRuntimeRegistryService(store);
        var jobs = factory.CreateJobEngine(store);
        using var http = new HttpClient(new StaticJsonHandler("[]"));
        var processRunner = new ScriptedProcessRunner(startInfo =>
            startInfo.ArgumentList.Cast<string>().Contains("--list-devices", StringComparer.Ordinal)
                ? new ProcessRunResult(0, "ggml_cuda_init: found 2 devices\nCUDA0:\nCUDA1:\n", "")
                : new ProcessRunResult(0, "--model --output --list-devices --split-mode --tensor-split", ""));
        await using var benchmarks = new BenchmarkApplicationService(
            store,
            jobs,
            sessions,
            new BenchmarkCapabilityService(processRunner),
            new BenchmarkProcessRunner(new WslRuntimeStopService(processRunner)),
            root);
        var settings = AppSettings.CreateDefault(root) with { ModelApiKey = new string('a', 32) };
        var api = new LocalControlApi(new LocalControlDependencies(
            root,
            store,
            sessions,
            catalog,
            profiles,
            runtimes,
            factory.CreateHuggingFaceService(store, jobs, catalog),
            factory.CreateRuntimeTelemetryApplicationService(factory.CreateRuntimeMetricPollerService(http)),
            factory.CreateRuntimeLogTailService(),
            factory.CreateRuntimeEndpointProbeService(http),
            factory.CreateLogPageWorkflowService(store),
            new LocalControlActions(
                () => settings,
                (next, _) => Task.FromResult(settings = next),
                (_, _, _, _, _, _) => throw new InvalidOperationException("No model launch expected."),
                (_, _) => Task.CompletedTask,
                _ => Task.CompletedTask),
            Benchmarks: () => benchmarks));

        var runtimeFolder = Path.Combine(root, "runtimes", "cuda", "bin");
        Directory.CreateDirectory(runtimeFolder);
        var serverPath = Path.Combine(runtimeFolder, "llama-server.exe");
        await File.WriteAllTextAsync(serverPath, "server", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(runtimeFolder, "llama-bench.exe"), "bench", cancellationToken);
        await store.UpsertRuntimeAsync(new RuntimeRecord(
            "cuda-runtime", "CUDA runtime", RuntimeMode.Native, RuntimeBackend.Cuda, serverPath, "{}", DateTimeOffset.UtcNow));

        var baseline = await CreateCompletedRunAsync(store, jobs, "Baseline", 100, cancellationToken);
        var candidate = await CreateCompletedRunAsync(store, jobs, "Candidate", 112, cancellationToken);
        await File.WriteAllTextAsync(candidate.LogPath, $"benchmark log {settings.ModelApiKey}", cancellationToken);

        var capabilities = await api.HandleAsync(Request("GET", "/api/v1/capabilities"), cancellationToken);
        var schema = await api.HandleAsync(Request("GET", "/api/v1/benchmarks/schema"), cancellationToken);
        var presets = await api.HandleAsync(Request("GET", "/api/v1/benchmarks/presets"), cancellationToken);
        var runtimeCapabilities = await api.HandleAsync(Request(
            "GET",
            "/api/v1/benchmarks/capabilities",
            query: new Dictionary<string, string> { ["runtime"] = "cuda-runtime" }), cancellationToken);
        Assert.Equal(200, capabilities.StatusCode);
        Assert.Contains("benchmark-agent-contract", JsonSerializer.Serialize(capabilities.Body), StringComparison.Ordinal);
        Assert.Equal(200, schema.StatusCode);
        var schemaJson = JsonSerializer.Serialize(schema.Body);
        Assert.Contains("promptGenerationPair", schemaJson, StringComparison.Ordinal);
        Assert.Contains("speculativeConfiguration", schemaJson, StringComparison.Ordinal);
        Assert.Contains("type/head pair", schemaJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(200, presets.StatusCode);
        Assert.Contains("Long context benchmark", JsonSerializer.Serialize(presets.Body), StringComparison.Ordinal);
        Assert.Equal(200, runtimeCapabilities.StatusCode);
        var runtimeJson = JsonSerializer.Serialize(runtimeCapabilities.Body);
        Assert.Contains("CUDA0", runtimeJson, StringComparison.Ordinal);
        Assert.Contains("\"detectedDeviceCount\":2", runtimeJson, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"row\"", runtimeJson, StringComparison.Ordinal);

        var comparison = await api.HandleAsync(Request(
            "POST",
            "/api/v1/benchmarks/compare",
            new JsonObject
            {
                ["baselineRunId"] = baseline.Id,
                ["candidateRunId"] = candidate.Id
            }), cancellationToken);
        Assert.Equal(200, comparison.StatusCode);
        var comparisonJson = JsonSerializer.SerializeToNode(comparison.Body)!.AsObject();
        Assert.Equal(1, comparisonJson["summary"]!["matchedWorkloads"]!.GetValue<int>());
        Assert.Equal(1, comparisonJson["summary"]!["improvedWorkloads"]!.GetValue<int>());
        Assert.Equal(12, comparisonJson["rows"]![0]!["percentChange"]!.GetValue<double>(), 6);

        var plan = await api.HandleAsync(Request("GET", $"/api/v1/benchmarks/{candidate.Id}/plan"), cancellationToken);
        Assert.Equal(200, plan.StatusCode);
        Assert.Contains("Candidate", JsonSerializer.Serialize(plan.Body), StringComparison.Ordinal);
        var log = await api.HandleAsync(Request("GET", $"/api/v1/benchmarks/{candidate.Id}/log"), cancellationToken);
        Assert.Equal(200, log.StatusCode);
        var logJson = JsonSerializer.Serialize(log.Body);
        Assert.Contains("benchmark log", logJson, StringComparison.Ordinal);
        Assert.DoesNotContain(settings.ModelApiKey, logJson, StringComparison.Ordinal);

        Assert.Equal(400, (await api.HandleAsync(Request("DELETE", $"/api/v1/benchmarks/{candidate.Id}"), cancellationToken)).StatusCode);
        var deleted = await api.HandleAsync(Request(
            "DELETE",
            $"/api/v1/benchmarks/{candidate.Id}",
            query: new Dictionary<string, string> { ["confirm"] = "true" }), cancellationToken);
        Assert.Equal(200, deleted.StatusCode);
        Assert.Equal(0, await store.CountBenchmarkResultsAsync(candidate.Id, cancellationToken));
        Assert.Equal(404, (await api.HandleAsync(Request("GET", $"/api/v1/benchmarks/{candidate.Id}"), cancellationToken)).StatusCode);
    }

    private static async Task<JobRecord> CreateCompletedRunAsync(
        StateStore store,
        JobEngine jobs,
        string name,
        double tokensPerSecond,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new BenchmarkJobPayload(
            new BenchmarkPlan { Name = name },
            [],
            [],
            BenchmarkRunOutcome.Success,
            0,
            1,
            0,
            1,
            2,
            "Completed",
            now,
            now);
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var job = await jobs.CreateAsync(BenchmarkApplicationService.JobKind, payloadJson, cancellationToken);
        await jobs.UpdateAsync(job, JobStatus.Completed, payloadJson, cancellationToken);
        Assert.True(BenchmarkResultService.TryParse(
            ResultJson.Replace("\"avg_ts\":100.0", $"\"avg_ts\":{tokensPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
            "model",
            "command",
            RuntimeMode.Native,
            RuntimeBackend.Cuda,
            out var result,
            out var error), error);
        await store.InsertBenchmarkResultAsync(job.Id, "item", 1, 1, result!, cancellationToken);
        await store.CompleteBenchmarkAttemptAsync(job.Id, "item", 1, cancellationToken);
        return job;
    }

    private static LocalControlRequest Request(
        string method,
        string path,
        JsonObject? body = null,
        IReadOnlyDictionary<string, string>? query = null)
        => new(method, path, query ?? new Dictionary<string, string>(), body, new Dictionary<string, string>());

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
