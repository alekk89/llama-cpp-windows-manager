using LocalLlmConsole.Models;
using System.Globalization;

namespace LocalLlmConsole.Services;

public sealed record StoredBenchmarkResult(
    long Id,
    string JobId,
    string WorkItemKey,
    int Attempt,
    int Sequence,
    bool IsPartialAttempt,
    BenchmarkParsedResult Result,
    DateTimeOffset CreatedAt);

public sealed partial class StateStore
{
    public async Task<IReadOnlyList<JobRecord>> ListBenchmarkJobsAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        return await WithConnectionAsync<IReadOnlyList<JobRecord>>(async () =>
        {
            var jobs = new List<JobRecord>();
            await using var command = _connection.CreateCommand();
            command.CommandText = """
SELECT id, kind, status, payload_json, log_path, created_at, updated_at
FROM jobs
WHERE kind = $kind
ORDER BY created_at DESC
LIMIT $limit OFFSET $offset;
""";
            command.Parameters.AddWithValue("$kind", BenchmarkApplicationService.JobKind);
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                jobs.Add(new JobRecord(
                    reader.GetString(0), reader.GetString(1), EnumValue(reader.GetString(2), JobStatus.Failed),
                    reader.GetString(3), reader.GetString(4), DateValue(reader.GetString(5)), DateValue(reader.GetString(6))));
            }
            return jobs;
        });
    }

    public async Task<long> InsertBenchmarkResultAsync(
        string jobId,
        string workItemKey,
        int attempt,
        int sequence,
        BenchmarkParsedResult result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
INSERT INTO benchmark_results (
  job_id, work_item_key, attempt, sequence, is_partial_attempt, classification,
  workload_signature, environment_signature, manager_version, operating_environment, raw_json, build_commit, build_number,
  model_filename, model_type, n_prompt, n_gen, n_depth, avg_ns, stddev_ns, avg_ts, stddev_ts, created_at)
VALUES (
  $job_id, $work_item_key, $attempt, $sequence, 1, $classification,
  $workload_signature, $environment_signature, $manager_version, $operating_environment, $raw_json, $build_commit, $build_number,
  $model_filename, $model_type, $n_prompt, $n_gen, $n_depth, $avg_ns, $stddev_ns, $avg_ts, $stddev_ts, $created_at);
SELECT last_insert_rowid();
""";
            command.Parameters.AddWithValue("$job_id", jobId);
            command.Parameters.AddWithValue("$work_item_key", workItemKey);
            command.Parameters.AddWithValue("$attempt", attempt);
            command.Parameters.AddWithValue("$sequence", sequence);
            command.Parameters.AddWithValue("$classification", result.Classification.ToString());
            command.Parameters.AddWithValue("$workload_signature", result.WorkloadSignature);
            command.Parameters.AddWithValue("$environment_signature", result.EnvironmentSignature);
            command.Parameters.AddWithValue("$manager_version", result.ManagerVersion);
            command.Parameters.AddWithValue("$operating_environment", result.OperatingEnvironment);
            command.Parameters.AddWithValue("$raw_json", result.RawJson);
            command.Parameters.AddWithValue("$build_commit", result.BuildCommit);
            command.Parameters.AddWithValue("$build_number", result.BuildNumber);
            command.Parameters.AddWithValue("$model_filename", result.ModelFilename);
            command.Parameters.AddWithValue("$model_type", result.ModelType);
            command.Parameters.AddWithValue("$n_prompt", result.PromptTokens);
            command.Parameters.AddWithValue("$n_gen", result.GenerationTokens);
            command.Parameters.AddWithValue("$n_depth", result.Depth);
            command.Parameters.AddWithValue("$avg_ns", result.AverageNanoseconds);
            command.Parameters.AddWithValue("$stddev_ns", result.StandardDeviationNanoseconds);
            command.Parameters.AddWithValue("$avg_ts", result.AverageTokensPerSecond);
            command.Parameters.AddWithValue("$stddev_ts", result.StandardDeviationTokensPerSecond);
            command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        });
    }

    public async Task CompleteBenchmarkAttemptAsync(
        string jobId,
        string workItemKey,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
UPDATE benchmark_results
SET is_partial_attempt = 0
WHERE job_id = $job_id AND work_item_key = $work_item_key AND attempt = $attempt;
""";
            command.Parameters.AddWithValue("$job_id", jobId);
            command.Parameters.AddWithValue("$work_item_key", workItemKey);
            command.Parameters.AddWithValue("$attempt", attempt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        });
    }

    public async Task<IReadOnlyList<StoredBenchmarkResult>> ListBenchmarkResultsAsync(
        string jobId,
        int limit = 200,
        int offset = 0,
        bool includePartialAttempts = true,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1_000) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        cancellationToken.ThrowIfCancellationRequested();
        return await WithConnectionAsync<IReadOnlyList<StoredBenchmarkResult>>(async () =>
        {
            var rows = new List<StoredBenchmarkResult>();
            await using var command = _connection.CreateCommand();
            command.CommandText = """
SELECT id, job_id, work_item_key, attempt, sequence, is_partial_attempt,
       workload_signature, environment_signature, manager_version, operating_environment, raw_json, created_at
FROM benchmark_results
WHERE job_id = $job_id AND ($include_partial = 1 OR is_partial_attempt = 0)
ORDER BY id
LIMIT $limit OFFSET $offset;
""";
            command.Parameters.AddWithValue("$job_id", jobId);
            command.Parameters.AddWithValue("$include_partial", includePartialAttempts ? 1 : 0);
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var raw = reader.GetString(10);
                if (!BenchmarkResultService.TryParse(raw, "stored", "stored", RuntimeMode.Native, RuntimeBackend.Cpu, out var parsed, out _)
                    || parsed is null)
                    continue;
                parsed = parsed with
                {
                    WorkloadSignature = reader.GetString(6),
                    EnvironmentSignature = reader.GetString(7),
                    ManagerVersion = reader.GetString(8),
                    OperatingEnvironment = reader.GetString(9)
                };
                rows.Add(new StoredBenchmarkResult(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4),
                    reader.GetInt32(5) != 0, parsed, DateValue(reader.GetString(11))));
            }
            return rows;
        });
    }

    public async Task<int> CountBenchmarkResultsAsync(string jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM benchmark_results WHERE job_id = $job_id;";
            command.Parameters.AddWithValue("$job_id", jobId);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        });
    }
}
