using System.Globalization;
using System.Text;

namespace LocalLlmConsole.Services;

public static class BenchmarkExportService
{
    public static async Task<IReadOnlyList<StoredBenchmarkResult>> LoadAllAsync(
        StateStore store,
        string jobId,
        bool includePartialAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var rows = new List<StoredBenchmarkResult>();
        const int pageSize = 1000;
        while (true)
        {
            var page = await store.ListBenchmarkResultsAsync(jobId, pageSize, rows.Count, includePartialAttempts, cancellationToken);
            rows.AddRange(page);
            if (page.Count < pageSize) return rows;
        }
    }

    public static string Json(IReadOnlyList<StoredBenchmarkResult> results)
        => JsonSerializer.Serialize(results, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    public static string Csv(IReadOnlyList<StoredBenchmarkResult> results)
    {
        var csv = new StringBuilder("job_id,work_item_key,attempt,sequence,partial,execution_mode,profile_id,profile_name,speculative_type,concurrency,request_count,failed_request_count,draft_tokens,accepted_draft_tokens,draft_acceptance_percent,speculative_metrics_observed,avg_prompt_ts,avg_latency_ms,stddev_latency_ms,classification,workload_signature,environment_signature,manager_version,operating_environment,n_prompt,n_gen,n_ctx,n_depth,n_batch,n_ubatch,n_threads,cpu_mask,cpu_strict,poll,n_gpu_layers,n_cpu_moe,cache_type_k,cache_type_v,split_mode,main_gpu,no_kv_offload,flash_attention,devices,tensor_split,tensor_buffer_overrides,load_mode,embeddings,no_op_offload,no_host,fit_target,fit_min_ctx,avg_ts,stddev_ts,avg_ns,stddev_ns,build_commit,build_number,model_filename,test_time\r\n");
        foreach (var row in results)
        {
            var result = row.Result;
            csv.AppendJoin(',',
                Quote(row.JobId), Quote(row.WorkItemKey), Number(row.Attempt), Number(row.Sequence), row.IsPartialAttempt ? "1" : "0",
                Quote(result.ExecutionMode.ToString()), Quote(result.ProfileId), Quote(result.ProfileName), Quote(result.SpeculativeType),
                Number(result.Concurrency), Number(result.RequestCount), Number(result.FailedRequestCount), Number(result.DraftTokens),
                Number(result.AcceptedDraftTokens), Real(result.DraftAcceptancePercent), result.SpeculativeMetricsObserved ? "1" : "0",
                Real(result.AveragePromptTokensPerSecond), Real(result.AverageLatencyMilliseconds), Real(result.StandardDeviationLatencyMilliseconds),
                Quote(result.Classification.ToString()), Quote(result.WorkloadSignature), Quote(result.EnvironmentSignature),
                Quote(result.ManagerVersion), Quote(result.OperatingEnvironment),
                Number(result.PromptTokens), Number(result.GenerationTokens), Number(result.ContextSize), Number(result.Depth), Number(result.BatchSize),
                Number(result.MicroBatchSize), Number(result.Threads), Quote(result.CpuMask), result.CpuStrict ? "1" : "0", Number(result.Poll),
                Number(result.GpuLayers), Number(result.CpuMoeLayers), Quote(result.CacheTypeK), Quote(result.CacheTypeV), Quote(result.SplitMode),
                Number(result.MainGpu), result.NoKvOffload ? "1" : "0", Quote(result.FlashAttention), Quote(result.Devices),
                Quote(result.TensorSplit), Quote(result.TensorBufferOverrides), Quote(result.LoadMode), result.Embeddings ? "1" : "0",
                result.NoOpOffload ? "1" : "0", result.NoHost ? "1" : "0", Number(result.FitTarget), Number(result.FitMinimumContext), Real(result.AverageTokensPerSecond),
                Real(result.StandardDeviationTokensPerSecond), Number(result.AverageNanoseconds), Number(result.StandardDeviationNanoseconds),
                Quote(result.BuildCommit), Number(result.BuildNumber), Quote(result.ModelFilename), Quote(result.TestTime));
            csv.Append("\r\n");
        }
        return csv.ToString();
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Real(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Quote(string value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
}
