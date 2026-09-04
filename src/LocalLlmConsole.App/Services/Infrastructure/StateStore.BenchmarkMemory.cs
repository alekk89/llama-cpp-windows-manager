namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    // llama-bench can buffer output and run several workloads per process. Its
    // peak belongs to the whole process attempt, not to any one emitted row.
    public Task SetBenchmarkMemoryAsync(
        string jobId, string workItemKey, int attempt,
        IReadOnlyList<BenchmarkGpuMemoryPeak> peaks, int intervalMilliseconds)
        => WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE benchmark_results
                SET raw_json = json_set(raw_json,
                    '$.gpu_memory_peaks', json($peaks),
                    '$.gpu_memory_sample_interval_ms', $interval,
                    '$.gpu_memory_measurement_window', 'process')
                WHERE job_id = $job AND work_item_key = $item AND attempt = $attempt;
                """;
            command.Parameters.AddWithValue("$peaks", JsonSerializer.Serialize(peaks));
            command.Parameters.AddWithValue("$interval", intervalMilliseconds);
            command.Parameters.AddWithValue("$job", jobId);
            command.Parameters.AddWithValue("$item", workItemKey);
            command.Parameters.AddWithValue("$attempt", attempt);
            await command.ExecuteNonQueryAsync();
        });
}
