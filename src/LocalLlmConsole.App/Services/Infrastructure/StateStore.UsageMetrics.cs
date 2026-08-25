namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    public Task AddTokenUsageAsync(string modelId, string modelName, long promptTokens, long generatedTokens)
        => RecordTokenUsageAsync(new TokenUsageDelta(modelId, modelName, promptTokens, generatedTokens));

    public async Task RecordTokenUsageAsync(TokenUsageDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        if (!delta.HasActivity || string.IsNullOrWhiteSpace(delta.ModelId)) return;

        var capturedAt = delta.EffectiveCapturedAt.ToUniversalTime();
        var bucketStart = new DateTimeOffset(
            capturedAt.Year,
            capturedAt.Month,
            capturedAt.Day,
            capturedAt.Hour,
            0,
            0,
            TimeSpan.Zero);
        var updatedAt = capturedAt.ToString("O");
        var modelName = ValueOrFallback(delta.ModelName, delta.ModelId);
        var profileName = ValueOrFallback(delta.LaunchProfileName, delta.LaunchProfileId);
        var runtimeName = ValueOrFallback(delta.RuntimeName, delta.RuntimeId);
        var prompt = Math.Max(0, delta.PromptTokens);
        var cached = Math.Max(0, delta.CachedPromptTokens);
        var generated = Math.Max(0, delta.GeneratedTokens);
        var promptSeconds = FiniteNonNegative(delta.PromptSeconds);
        var generatedSeconds = FiniteNonNegative(delta.GeneratedSeconds);
        var requests = Math.Max(0, delta.RequestCount);
        var failedRequests = Math.Max(0, delta.FailedRequestCount);

        await WithConnectionAsync(async () =>
        {
            await using var transaction = await _connection.BeginTransactionAsync();
            try
            {
                await using (var lifetime = _connection.CreateCommand())
                {
                    lifetime.Transaction = (SqliteTransaction)transaction;
                    lifetime.CommandText = """
INSERT INTO token_usage (
  model_id, model_name, prompt_tokens, generated_tokens,
  updated_at, cached_prompt_tokens, cache_counter_observed)
VALUES (
  $model_id, $model_name, $prompt_tokens, $generated_tokens,
  $updated_at, $cached_prompt_tokens, $cache_counter_observed)
ON CONFLICT(model_id) DO UPDATE SET
  model_name = excluded.model_name,
  prompt_tokens = token_usage.prompt_tokens + excluded.prompt_tokens,
  cached_prompt_tokens = token_usage.cached_prompt_tokens + excluded.cached_prompt_tokens,
  generated_tokens = token_usage.generated_tokens + excluded.generated_tokens,
  cache_counter_observed = MAX(token_usage.cache_counter_observed, excluded.cache_counter_observed),
  updated_at = excluded.updated_at;
""";
                    AddUsageParameters(lifetime, delta, modelName, profileName, runtimeName, bucketStart, updatedAt, prompt, cached, generated);
                    await lifetime.ExecuteNonQueryAsync();
                }

                await using (var hourly = _connection.CreateCommand())
                {
                    hourly.Transaction = (SqliteTransaction)transaction;
                    hourly.CommandText = """
INSERT INTO token_usage_hourly (
  bucket_start_utc, model_id, model_name, launch_profile_id, launch_profile_name,
  runtime_id, runtime_name, runtime_mode, runtime_backend, prompt_tokens,
  cached_prompt_tokens, generated_tokens, cache_counter_observed, updated_at,
  prompt_seconds, generated_seconds, timing_counter_observed,
  request_count, failed_request_count, request_counter_observed)
VALUES (
  $bucket_start_utc, $model_id, $model_name, $launch_profile_id, $launch_profile_name,
  $runtime_id, $runtime_name, $runtime_mode, $runtime_backend, $prompt_tokens,
  $cached_prompt_tokens, $generated_tokens, $cache_counter_observed, $updated_at,
  $prompt_seconds, $generated_seconds, $timing_counter_observed,
  $request_count, $failed_request_count, $request_counter_observed)
ON CONFLICT(bucket_start_utc, model_id, launch_profile_id, runtime_id) DO UPDATE SET
  model_name = excluded.model_name,
  launch_profile_name = excluded.launch_profile_name,
  runtime_name = excluded.runtime_name,
  runtime_mode = excluded.runtime_mode,
  runtime_backend = excluded.runtime_backend,
  prompt_tokens = token_usage_hourly.prompt_tokens + excluded.prompt_tokens,
  cached_prompt_tokens = token_usage_hourly.cached_prompt_tokens + excluded.cached_prompt_tokens,
  generated_tokens = token_usage_hourly.generated_tokens + excluded.generated_tokens,
  cache_counter_observed = MAX(token_usage_hourly.cache_counter_observed, excluded.cache_counter_observed),
  prompt_seconds = token_usage_hourly.prompt_seconds + excluded.prompt_seconds,
  generated_seconds = token_usage_hourly.generated_seconds + excluded.generated_seconds,
  timing_counter_observed = MAX(token_usage_hourly.timing_counter_observed, excluded.timing_counter_observed),
  request_count = token_usage_hourly.request_count + excluded.request_count,
  failed_request_count = token_usage_hourly.failed_request_count + excluded.failed_request_count,
  request_counter_observed = MAX(token_usage_hourly.request_counter_observed, excluded.request_counter_observed),
  updated_at = excluded.updated_at;
""";
                    AddUsageParameters(hourly, delta, modelName, profileName, runtimeName, bucketStart, updatedAt, prompt, cached, generated, promptSeconds, generatedSeconds, requests, failedRequests);
                    await hourly.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    public async Task<IReadOnlyList<TokenUsageRecord>> ListTokenUsageAsync()
        => await WithConnectionAsync<IReadOnlyList<TokenUsageRecord>>(async () =>
        {
            var rows = new List<TokenUsageRecord>();
            await using var command = _connection.CreateCommand();
            command.CommandText = """
SELECT model_id, model_name, prompt_tokens, generated_tokens, updated_at,
       cached_prompt_tokens, cache_counter_observed
FROM token_usage
ORDER BY prompt_tokens + cached_prompt_tokens + generated_tokens DESC, model_name;
""";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new TokenUsageRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    DateValue(reader.GetString(4)),
                    reader.GetInt64(5),
                    reader.GetInt64(6) == 1));
            }
            return rows;
        });

    public async Task<IReadOnlyList<UsageMetricBucket>> ListTokenUsageBucketsAsync(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null)
        => await WithConnectionAsync<IReadOnlyList<UsageMetricBucket>>(async () =>
        {
            var rows = new List<UsageMetricBucket>();
            await using var command = _connection.CreateCommand();
            command.CommandText = """
SELECT bucket_start_utc, model_id, model_name, launch_profile_id, launch_profile_name,
       runtime_id, runtime_name, runtime_mode, runtime_backend, prompt_tokens,
       cached_prompt_tokens, generated_tokens, cache_counter_observed, updated_at,
       prompt_seconds, generated_seconds, timing_counter_observed,
       request_count, failed_request_count, request_counter_observed
FROM token_usage_hourly
""" + UtcRangeClause(fromUtc, toUtc) + """
ORDER BY bucket_start_utc, model_name;
""";
            AddUtcRangeParameters(command, fromUtc, toUtc);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new UsageMetricBucket(
                    DateValue(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    EnumValue(reader.GetString(7), RuntimeMode.Native),
                    EnumValue(reader.GetString(8), RuntimeBackend.Cpu),
                    reader.GetInt64(9),
                    reader.GetInt64(10),
                    reader.GetInt64(11),
                    reader.GetInt64(12) == 1,
                    DateValue(reader.GetString(13)),
                    reader.GetDouble(14),
                    reader.GetDouble(15),
                    reader.GetInt64(16) == 1,
                    reader.GetInt64(17),
                    reader.GetInt64(18),
                    reader.GetInt64(19) == 1));
            }
            return rows;
        });

    public async Task<UsageMetricDimensions> ListTokenUsageDimensionsAsync()
        => await WithConnectionAsync(async () =>
        {
            var models = await ListDimensionsUnlockedAsync("""
SELECT model_id, MAX(model_name)
FROM (
  SELECT model_id, model_name FROM token_usage
  UNION ALL
  SELECT model_id, model_name FROM token_usage_hourly
)
GROUP BY model_id
ORDER BY MAX(model_name) COLLATE NOCASE;
""");
            var profiles = await ListDimensionsUnlockedAsync("""
SELECT launch_profile_id, MAX(launch_profile_name)
FROM token_usage_hourly
WHERE launch_profile_id <> ''
GROUP BY launch_profile_id
ORDER BY MAX(launch_profile_name) COLLATE NOCASE;
""");
            var runtimes = await ListDimensionsUnlockedAsync("""
SELECT runtime_id, MAX(runtime_name)
FROM token_usage_hourly
WHERE runtime_id <> ''
GROUP BY runtime_id
ORDER BY MAX(runtime_name) COLLATE NOCASE;
""");
            return new UsageMetricDimensions(models, profiles, runtimes);
        });

    public async Task<DateTimeOffset?> GetTokenUsageTrackingStartedAtAsync()
        => await WithConnectionAsync<DateTimeOffset?>(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT MIN(bucket_start_utc) FROM token_usage_hourly;";
            var value = await command.ExecuteScalarAsync();
            return value is string text ? DateValue(text) : null;
        });

    public async Task DeleteTokenUsageAsync(string modelId)
    {
        await WithConnectionAsync(async () =>
        {
            await using var transaction = await _connection.BeginTransactionAsync();
            await using var command = _connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
DELETE FROM token_usage WHERE model_id = $model_id;
DELETE FROM token_usage_hourly WHERE model_id = $model_id;
""";
            command.Parameters.AddWithValue("$model_id", modelId);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        });
    }

    public async Task DeleteAllTokenUsageAsync()
    {
        await WithConnectionAsync(async () =>
        {
            await using var transaction = await _connection.BeginTransactionAsync();
            await using var command = _connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "DELETE FROM token_usage; DELETE FROM token_usage_hourly;";
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        });
    }

    private async Task<IReadOnlyList<UsageMetricDimension>> ListDimensionsUnlockedAsync(string sql)
    {
        var rows = new List<UsageMetricDimension>();
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new UsageMetricDimension(reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    private static void AddUsageParameters(
        SqliteCommand command,
        TokenUsageDelta delta,
        string modelName,
        string profileName,
        string runtimeName,
        DateTimeOffset bucketStart,
        string updatedAt,
        long prompt,
        long cached,
        long generated,
        double promptSeconds = 0,
        double generatedSeconds = 0,
        long requests = 0,
        long failedRequests = 0)
    {
        command.Parameters.AddWithValue("$bucket_start_utc", bucketStart.ToString("O"));
        command.Parameters.AddWithValue("$model_id", delta.ModelId);
        command.Parameters.AddWithValue("$model_name", modelName);
        command.Parameters.AddWithValue("$launch_profile_id", delta.LaunchProfileId ?? "");
        command.Parameters.AddWithValue("$launch_profile_name", profileName);
        command.Parameters.AddWithValue("$runtime_id", delta.RuntimeId ?? "");
        command.Parameters.AddWithValue("$runtime_name", runtimeName);
        command.Parameters.AddWithValue("$runtime_mode", delta.RuntimeMode.ToString());
        command.Parameters.AddWithValue("$runtime_backend", delta.RuntimeBackend.ToString());
        command.Parameters.AddWithValue("$prompt_tokens", prompt);
        command.Parameters.AddWithValue("$cached_prompt_tokens", cached);
        command.Parameters.AddWithValue("$generated_tokens", generated);
        command.Parameters.AddWithValue("$cache_counter_observed", delta.CacheCounterObserved ? 1 : 0);
        command.Parameters.AddWithValue("$prompt_seconds", promptSeconds);
        command.Parameters.AddWithValue("$generated_seconds", generatedSeconds);
        command.Parameters.AddWithValue("$timing_counter_observed", delta.TimingCounterObserved ? 1 : 0);
        command.Parameters.AddWithValue("$request_count", requests);
        command.Parameters.AddWithValue("$failed_request_count", failedRequests);
        command.Parameters.AddWithValue("$request_counter_observed", delta.RequestCounterObserved ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", updatedAt);
    }

    private static string ValueOrFallback(string? value, string? fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback ?? "" : value;

    private static double FiniteNonNegative(double value)
        => double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static string UtcRangeClause(DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
        => (fromUtc is not null, toUtc is not null) switch
        {
            (true, true) => "\nWHERE bucket_start_utc >= $from_utc AND bucket_start_utc < $to_utc\n",
            (true, false) => "\nWHERE bucket_start_utc >= $from_utc\n",
            (false, true) => "\nWHERE bucket_start_utc < $to_utc\n",
            _ => "\n"
        };

    private static void AddUtcRangeParameters(
        SqliteCommand command,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        if (fromUtc is not null)
            command.Parameters.AddWithValue("$from_utc", fromUtc.Value.ToUniversalTime().ToString("O"));
        if (toUtc is not null)
            command.Parameters.AddWithValue("$to_utc", toUtc.Value.ToUniversalTime().ToString("O"));
    }
}
