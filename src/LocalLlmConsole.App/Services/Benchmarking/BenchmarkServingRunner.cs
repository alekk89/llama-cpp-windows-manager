using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed class BenchmarkServingRunner : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Func<IGpuMemoryProbe> _createMemoryProbe;

    public BenchmarkServingRunner()
        : this(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, ownsHttpClient: true, null)
    {
    }

    internal BenchmarkServingRunner(
        HttpClient httpClient,
        bool ownsHttpClient = false,
        Func<IGpuMemoryProbe>? createMemoryProbe = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
        _createMemoryProbe = createMemoryProbe ?? (() => new WindowsGpuMemoryProbe());
    }

    public async Task<IReadOnlyList<BenchmarkParsedResult>> RunAsync(
        BenchmarkPlan plan,
        BenchmarkWorkItem item,
        RuntimeRecord runtime,
        ModelRecord model,
        AppSettings settings,
        Func<BenchmarkParsedResult, Task> onResult,
        Action<string>? onProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item.LaunchSettings);
        var endpoint = RuntimeEndpointService.LocalServerBaseUrl(settings);
        var servedModel = await WaitUntilReadyAsync(
            endpoint,
            settings,
            TimeSpan.FromSeconds(plan.Serving.ReadyTimeoutSeconds),
            cancellationToken);
        var results = new List<BenchmarkParsedResult>();
        foreach (var workload in BenchmarkPlanService.ServingWorkloads(plan))
            foreach (var concurrency in plan.Serving.Concurrencies.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var memorySampler = await BenchmarkGpuMemorySampler.StartAsync(_createMemoryProbe, cancellationToken);
                onProgress?.Invoke($"Warming profile {item.ProfileNames.FirstOrDefault()} ({workload.PromptTokens}/{workload.GenerationTokens}, c={concurrency})");
                if (plan.Warmup)
                    await RunBatchAsync(endpoint, servedModel, settings, workload, concurrency, plan.Serving, cancellationToken);

                var samples = new List<ServingBatchSample>(plan.Repetitions);
                for (var repetition = 0; repetition < plan.Repetitions; repetition++)
                {
                    onProgress?.Invoke($"Serving {workload.PromptTokens}/{workload.GenerationTokens}, c={concurrency}, repetition {repetition + 1}/{plan.Repetitions}");
                    samples.Add(await RunBatchAsync(endpoint, servedModel, settings, workload, concurrency, plan.Serving, cancellationToken));
                    if (plan.DelaySeconds > 0 && repetition + 1 < plan.Repetitions)
                        await Task.Delay(TimeSpan.FromSeconds(plan.DelaySeconds), cancellationToken);
                }

                var memoryPeaks = await memorySampler.FinishAsync();
                cancellationToken.ThrowIfCancellationRequested();
                var parsed = BuildResult(plan, item, runtime, model, workload, concurrency, samples, memoryPeaks);
                var speculativeType = SpeculativeTypePolicy.Normalize(parsed.SpeculativeType);
                if (plan.Serving.RequireSpeculativeMetrics
                    && speculativeType is not ("" or "none")
                    && !parsed.SpeculativeMetricsObserved)
                    throw new InvalidOperationException(
                        $"Profile '{parsed.ProfileName}' is configured for '{parsed.SpeculativeType}', but the server returned no draft/MTP activity. " +
                        "The benchmark was rejected instead of reporting a misleading bare-model result.");
                await onResult(parsed);
                results.Add(parsed);
            }
        return results;
    }

    internal async Task<string> WaitUntilReadyAsync(
        string endpoint,
        AppSettings settings,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var readinessTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readinessTimeout.CancelAfter(timeout);
        Exception? lastError = null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var request = Authorized(HttpMethod.Get, $"{endpoint}/v1/models", settings);
                    using var response = await _http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        readinessTimeout.Token);
                    var json = await response.Content.ReadAsStringAsync(readinessTimeout.Token);
                    if (response.IsSuccessStatusCode)
                        return RuntimeEndpointService.ExtractServedModelIds(json).FirstOrDefault() ?? "benchmark-model";
                    lastError = new HttpRequestException($"HTTP {(int)response.StatusCode}: {Bound(json)}");
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    lastError = ex;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(500), readinessTimeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && readinessTimeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The benchmark profile did not become ready within {timeout.TotalSeconds:0.###} seconds. {lastError?.Message}".Trim());
        }
    }

    private async Task<ServingBatchSample> RunBatchAsync(
        string endpoint,
        string servedModel,
        AppSettings settings,
        BenchmarkPromptGenerationPair workload,
        int concurrency,
        BenchmarkServingOptions options,
        CancellationToken cancellationToken)
    {
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, concurrency)
            .Select(index => SendAsync(endpoint, servedModel, settings, workload, options, index, requestTimeout.Token))
            .ToArray();
        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();
        var generated = responses.Sum(response => response.GenerationTokens);
        var throughput = generated / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.000001);
        return new ServingBatchSample(throughput, stopwatch.Elapsed.TotalMilliseconds, responses);
    }

    private async Task<ServingResponseSample> SendAsync(
        string endpoint,
        string servedModel,
        AppSettings settings,
        BenchmarkPromptGenerationPair workload,
        BenchmarkServingOptions options,
        int requestIndex,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = servedModel,
            messages = new[] { new { role = "user", content = Prompt(workload.PromptTokens, requestIndex) } },
            max_tokens = workload.GenerationTokens,
            temperature = options.Temperature,
            seed = options.Seed + requestIndex,
            stream = false,
            cache_prompt = false,
            ignore_eos = true
        });
        using var request = Authorized(HttpMethod.Post, $"{endpoint}/v1/chat/completions", settings);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        var stopwatch = Stopwatch.StartNew();
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Benchmark request failed with HTTP {(int)response.StatusCode}: {Bound(json)}");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var usage = Child(root, "usage");
        var timings = Child(root, "timings");
        var prompt = Number(usage, "prompt_tokens", Number(timings, "prompt_n"));
        var generation = Number(usage, "completion_tokens", Number(timings, "predicted_n"));
        if (generation <= 0)
            throw new InvalidOperationException("The server returned no generated tokens for a timed benchmark request.");
        return new ServingResponseSample(
            prompt,
            generation,
            Real(timings, "prompt_per_second"),
            Real(timings, "predicted_per_second"),
            Number(timings, "draft_n"),
            Number(timings, "draft_n_accepted"),
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private static BenchmarkParsedResult BuildResult(
        BenchmarkPlan plan,
        BenchmarkWorkItem item,
        RuntimeRecord runtime,
        ModelRecord model,
        BenchmarkPromptGenerationPair workload,
        int concurrency,
        IReadOnlyList<ServingBatchSample> batches,
        IReadOnlyList<BenchmarkGpuMemoryPeak> memoryPeaks)
    {
        var responses = batches.SelectMany(batch => batch.Responses).ToArray();
        var throughput = batches.Select(batch => batch.GenerationThroughput).ToArray();
        var latencies = responses.Select(response => response.LatencyMilliseconds).ToArray();
        var promptTokens = (int)Math.Round(responses.Average(response => response.PromptTokens));
        var generationTokens = (int)Math.Round(responses.Average(response => response.GenerationTokens));
        var draft = responses.Sum(response => (long)response.DraftTokens);
        var accepted = responses.Sum(response => (long)response.AcceptedDraftTokens);
        var avgThroughput = Mean(throughput);
        var avgLatency = Mean(latencies);
        var raw = JsonSerializer.Serialize(new
        {
            build_commit = RuntimeMetadata(runtime, "commit"),
            build_number = 0,
            cpu_info = "",
            gpu_info = "",
            backends = runtime.Backend.ToString(),
            model_filename = Path.GetFileName(model.ModelPath),
            model_type = model.Name,
            model_size = FileSize(model.ModelPath),
            model_n_params = 0,
            n_prompt = promptTokens,
            n_gen = generationTokens,
            n_depth = 0,
            n_ctx = item.LaunchSettings?.ContextSize ?? 0,
            n_batch = item.LaunchSettings?.BatchSize ?? 0,
            n_ubatch = item.LaunchSettings?.MicroBatchSize ?? 0,
            n_threads = item.LaunchSettings?.Threads ?? 0,
            n_gpu_layers = item.LaunchSettings?.GpuLayers ?? 0,
            type_k = item.LaunchSettings?.CacheTypeK ?? "",
            type_v = item.LaunchSettings?.CacheTypeV ?? "",
            flash_attn = item.LaunchSettings?.FlashAttention ?? "",
            devices = item.LaunchSettings?.GpuDevices ?? "",
            tensor_split = item.LaunchSettings?.GpuSplit ?? "",
            tensor_buffer_overrides = item.LaunchSettings?.TensorBufferOverrides ?? "",
            tensor_buft_overrides = item.LaunchSettings?.TensorBufferOverrides ?? "",
            avg_ns = (long)Math.Round(avgLatency * 1_000_000),
            stddev_ns = (long)Math.Round(StandardDeviation(latencies) * 1_000_000),
            avg_ts = avgThroughput,
            stddev_ts = StandardDeviation(throughput),
            test_time = DateTimeOffset.UtcNow.ToString("O"),
            execution_mode = "profile_serving",
            profile_id = item.ProfileIds.FirstOrDefault() ?? "",
            profile_name = item.ProfileNames.FirstOrDefault() ?? "",
            speculative_type = item.LaunchSettings?.SpeculativeType ?? "none",
            concurrency,
            request_count = responses.Length,
            failed_request_count = 0,
            avg_prompt_ts = Mean(responses.Select(response => response.PromptTokensPerSecond).Where(value => value > 0).ToArray()),
            avg_latency_ms = avgLatency,
            stddev_latency_ms = StandardDeviation(latencies),
            draft_tokens = draft,
            accepted_draft_tokens = accepted,
            draft_acceptance_percent = draft > 0 ? accepted * 100d / draft : 0,
            speculative_metrics_observed = draft > 0,
            target_prompt_tokens = workload.PromptTokens,
            target_generation_tokens = workload.GenerationTokens,
            gpu_memory_peaks = memoryPeaks,
            gpu_memory_measurement_window = "workload",
            gpu_memory_sample_interval_ms = BenchmarkGpuMemorySampler.IntervalMilliseconds,
            vulkan_allocation_block_size_mib = item.LaunchSettings?.VulkanAllocationBlockSizeMiB ?? 0
        });
        if (!BenchmarkResultService.TryParse(
                raw, item.ModelFingerprint, item.EffectiveCommandSignature + $"|c={concurrency}|p={workload.PromptTokens}|g={workload.GenerationTokens}",
                runtime.Mode, runtime.Backend, out var parsed, out var error,
                AppUpdateService.CurrentVersionLabel(), RuntimeInformation.OSDescription)
            || parsed is null)
            throw new InvalidOperationException($"Could not normalize the serving benchmark result: {error}");
        return parsed with
        {
            ExecutionMode = BenchmarkExecutionMode.ProfileServing,
            ProfileId = item.ProfileIds.FirstOrDefault() ?? "",
            ProfileName = item.ProfileNames.FirstOrDefault() ?? "",
            SpeculativeType = item.LaunchSettings?.SpeculativeType ?? "none",
            Concurrency = concurrency,
            RequestCount = responses.Length,
            AveragePromptTokensPerSecond = Mean(responses.Select(response => response.PromptTokensPerSecond).Where(value => value > 0).ToArray()),
            AverageLatencyMilliseconds = avgLatency,
            StandardDeviationLatencyMilliseconds = StandardDeviation(latencies),
            DraftTokens = draft,
            AcceptedDraftTokens = accepted,
            DraftAcceptancePercent = draft > 0 ? accepted * 100d / draft : 0,
            SpeculativeMetricsObserved = draft > 0,
            ContextSize = item.LaunchSettings?.ContextSize ?? 0,
            GpuMemoryPeaks = memoryPeaks,
            GpuMemorySampleIntervalMilliseconds = BenchmarkGpuMemorySampler.IntervalMilliseconds,
            VulkanAllocationBlockSizeMiB = item.LaunchSettings?.VulkanAllocationBlockSizeMiB ?? 0
        };
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, AppSettings settings)
    {
        var request = new HttpRequestMessage(method, url);
        var key = RuntimeEndpointService.ModelApiKeyForClient(settings);
        if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    private static string Prompt(int targetTokens, int requestIndex)
    {
        var builder = new StringBuilder(Math.Max(targetTokens * 2, 64));
        builder.Append("Continue this deterministic benchmark sequence with concise tokens. Request ").Append(requestIndex).Append(": ");
        for (var i = 0; i < targetTokens; i++) builder.Append("x ");
        return builder.ToString();
    }

    private static JsonElement Child(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) ? value : default;
    private static int Number(JsonElement root, string name, int fallback = 0)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static double Real(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) && double.IsFinite(result) ? result : 0;
    private static double Mean(IReadOnlyList<double> values) => values.Count == 0 ? 0 : values.Average();
    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = Mean(values);
        return Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / values.Count);
    }
    private static string RuntimeMetadata(RuntimeRecord runtime, string name)
    {
        try { return (JsonNode.Parse(runtime.MetadataJson) as JsonObject)?[name]?.ToString() ?? ""; }
        catch { return ""; }
    }
    private static long FileSize(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }
    private static string Bound(string value) => value.Length <= 1000 ? value : value[..1000];

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }

    private sealed record ServingResponseSample(
        int PromptTokens,
        int GenerationTokens,
        double PromptTokensPerSecond,
        double GenerationTokensPerSecond,
        int DraftTokens,
        int AcceptedDraftTokens,
        double LatencyMilliseconds);

    private sealed record ServingBatchSample(
        double GenerationThroughput,
        double ElapsedMilliseconds,
        IReadOnlyList<ServingResponseSample> Responses);
}
