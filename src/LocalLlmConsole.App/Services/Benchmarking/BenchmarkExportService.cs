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
        var devices = results.SelectMany(row => row.Result.GpuMemoryPeaks ?? [])
            .Select(peak => peak.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var csv = new StringBuilder("job_id,work_item_key,attempt,sequence,partial,execution_mode,profile_id,profile_name,speculative_type,concurrency,request_count,failed_request_count,draft_tokens,accepted_draft_tokens,draft_acceptance_percent,speculative_metrics_observed,avg_prompt_ts,avg_latency_ms,stddev_latency_ms,gpu_memory_used_mib,classification,workload_signature,environment_signature,manager_version,operating_environment,n_prompt,n_gen,n_ctx,n_depth,n_batch,n_ubatch,n_threads,cpu_mask,cpu_strict,poll,n_gpu_layers,n_cpu_moe,cache_type_k,cache_type_v,split_mode,main_gpu,no_kv_offload,flash_attention,devices,tensor_split,tensor_buffer_overrides,load_mode,embeddings,no_op_offload,no_host,fit_target,fit_min_ctx,avg_ts,stddev_ts,avg_ns,stddev_ns,build_commit,build_number,model_filename,test_time\r\n");
        csv.Length -= 2;
        csv.Append(",vulkan_allocation_block_size_mib,gpu_memory_status,gpu_memory_scope,gpu_memory_window,gpu_memory_sample_interval_ms");
        for (var i = 0; i < devices.Length; i++)
            csv.Append($",gpu_{i}_id,gpu_{i}_name,gpu_{i}_peak_dedicated_mib,gpu_{i}_dedicated_capacity_mib,gpu_{i}_peak_shared_mib,gpu_{i}_memory_samples");
        csv.Append("\r\n");
        foreach (var row in results)
        {
            var result = row.Result;
            csv.AppendJoin(',',
                Quote(row.JobId), Quote(row.WorkItemKey), Number(row.Attempt), Number(row.Sequence), row.IsPartialAttempt ? "1" : "0",
                Quote(result.ExecutionMode.ToString()), Quote(result.ProfileId), Quote(result.ProfileName), Quote(result.SpeculativeType),
                Number(result.Concurrency), Number(result.RequestCount), Number(result.FailedRequestCount), Number(result.DraftTokens),
                Number(result.AcceptedDraftTokens), Real(result.DraftAcceptancePercent), result.SpeculativeMetricsObserved ? "1" : "0",
                Real(result.AveragePromptTokensPerSecond), Real(result.AverageLatencyMilliseconds), Real(result.StandardDeviationLatencyMilliseconds),
                result.ObservedGpuMemoryUsedMiB > 0 ? Number(result.ObservedGpuMemoryUsedMiB) : "",
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
            var peaks = result.GpuMemoryPeaks ?? [];
            csv.Append(',');
            csv.AppendJoin(',', Number(result.VulkanAllocationBlockSizeMiB),
                peaks.Any(peak => peak.SampleCount > 0) ? "Sampled" : "Unavailable",
                "Device-wide (includes other applications)", Quote(result.GpuMemoryMeasurementWindow),
                Number(result.GpuMemorySampleIntervalMilliseconds));
            foreach (var device in devices)
            {
                var peak = peaks.FirstOrDefault(candidate => candidate.DeviceId.Equals(device, StringComparison.OrdinalIgnoreCase));
                csv.Append(',');
                csv.AppendJoin(',', Quote(device), Quote(peak?.DeviceName ?? ""),
                    Optional(peak?.PeakDedicatedUsedMiB), Optional(peak?.DedicatedCapacityMiB),
                    Optional(peak?.PeakSharedUsedMiB), Number(peak?.SampleCount ?? 0));
            }
            csv.Append("\r\n");
        }
        return csv.ToString();
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Optional(long? value) => value.HasValue ? Number(value.Value) : "";
    private static string Real(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Quote(string value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
}
