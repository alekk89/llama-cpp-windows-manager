namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    public async Task RecordGpuEnergyAsync(IEnumerable<GpuEnergyDelta> deltas)
        => await RecordGpuEnergyAsync(deltas, []);

    public async Task RecordGpuEnergyAsync(
        IEnumerable<GpuEnergyDelta> deltas,
        IEnumerable<GpuEnergyDeviceDelta> deviceDeltas)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        ArgumentNullException.ThrowIfNull(deviceDeltas);
        var valid = deltas
            .Where(delta => double.IsFinite(delta.WattHours) && delta.WattHours >= 0
                            && double.IsFinite(delta.SampledSeconds) && delta.SampledSeconds > 0)
            .ToArray();
        var validDevices = deviceDeltas
            .Where(delta => !string.IsNullOrWhiteSpace(delta.SensorKey)
                            && !string.IsNullOrWhiteSpace(delta.GpuName)
                            && delta.GpuIndex >= 0
                            && double.IsFinite(delta.WattHours) && delta.WattHours >= 0
                            && double.IsFinite(delta.SampledSeconds) && delta.SampledSeconds > 0)
            .ToArray();
        if (valid.Length == 0 && validDevices.Length == 0) return;

        await WithConnectionAsync(async () =>
        {
            await using var transaction = await _connection.BeginTransactionAsync();
            try
            {
                foreach (var delta in valid)
                {
                    await using var command = _connection.CreateCommand();
                    command.Transaction = (SqliteTransaction)transaction;
                    command.CommandText = """
INSERT INTO gpu_energy_hourly (
  bucket_start_utc, watt_hours, sampled_seconds, complete_coverage,
  observed_gpu_count, detected_gpu_count, updated_at)
VALUES (
  $bucket_start_utc, $watt_hours, $sampled_seconds, $complete_coverage,
  $observed_gpu_count, $detected_gpu_count, $updated_at)
ON CONFLICT(bucket_start_utc) DO UPDATE SET
  watt_hours = gpu_energy_hourly.watt_hours + excluded.watt_hours,
  sampled_seconds = gpu_energy_hourly.sampled_seconds + excluded.sampled_seconds,
  complete_coverage = MIN(gpu_energy_hourly.complete_coverage, excluded.complete_coverage),
  observed_gpu_count = MIN(gpu_energy_hourly.observed_gpu_count, excluded.observed_gpu_count),
  detected_gpu_count = MAX(gpu_energy_hourly.detected_gpu_count, excluded.detected_gpu_count),
  updated_at = excluded.updated_at;
""";
                    command.Parameters.AddWithValue("$bucket_start_utc", delta.BucketStartUtc.ToUniversalTime().ToString("O"));
                    command.Parameters.AddWithValue("$watt_hours", delta.WattHours);
                    command.Parameters.AddWithValue("$sampled_seconds", delta.SampledSeconds);
                    command.Parameters.AddWithValue("$complete_coverage", delta.CompleteCoverage ? 1 : 0);
                    command.Parameters.AddWithValue("$observed_gpu_count", Math.Max(0, delta.ObservedGpuCount));
                    command.Parameters.AddWithValue("$detected_gpu_count", Math.Max(0, delta.DetectedGpuCount));
                    command.Parameters.AddWithValue("$updated_at", delta.CapturedAt.ToUniversalTime().ToString("O"));
                    await command.ExecuteNonQueryAsync();
                }
                foreach (var delta in validDevices)
                {
                    await using var command = _connection.CreateCommand();
                    command.Transaction = (SqliteTransaction)transaction;
                    command.CommandText = """
INSERT INTO gpu_energy_device_hourly (
  bucket_start_utc, sensor_key, gpu_index, gpu_name,
  watt_hours, sampled_seconds, updated_at)
VALUES (
  $bucket_start_utc, $sensor_key, $gpu_index, $gpu_name,
  $watt_hours, $sampled_seconds, $updated_at)
ON CONFLICT(bucket_start_utc, sensor_key) DO UPDATE SET
  gpu_index = excluded.gpu_index,
  gpu_name = excluded.gpu_name,
  watt_hours = gpu_energy_device_hourly.watt_hours + excluded.watt_hours,
  sampled_seconds = gpu_energy_device_hourly.sampled_seconds + excluded.sampled_seconds,
  updated_at = excluded.updated_at;
""";
                    command.Parameters.AddWithValue("$bucket_start_utc", delta.BucketStartUtc.ToUniversalTime().ToString("O"));
                    command.Parameters.AddWithValue("$sensor_key", delta.SensorKey.Trim());
                    command.Parameters.AddWithValue("$gpu_index", delta.GpuIndex);
                    command.Parameters.AddWithValue("$gpu_name", delta.GpuName.Trim());
                    command.Parameters.AddWithValue("$watt_hours", delta.WattHours);
                    command.Parameters.AddWithValue("$sampled_seconds", delta.SampledSeconds);
                    command.Parameters.AddWithValue("$updated_at", delta.CapturedAt.ToUniversalTime().ToString("O"));
                    await command.ExecuteNonQueryAsync();
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

    public async Task<IReadOnlyList<GpuEnergyDeviceBucket>> ListGpuEnergyDeviceBucketsAsync(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null)
        => await WithConnectionAsync<IReadOnlyList<GpuEnergyDeviceBucket>>(async () =>
        {
            var rows = new List<GpuEnergyDeviceBucket>();
            await using var command = _connection.CreateCommand();
            command.CommandText = """
SELECT bucket_start_utc, sensor_key, gpu_index, gpu_name,
       watt_hours, sampled_seconds, updated_at
FROM gpu_energy_device_hourly
""" + UtcRangeClause(fromUtc, toUtc) + """
ORDER BY bucket_start_utc, gpu_index, sensor_key;
""";
            AddUtcRangeParameters(command, fromUtc, toUtc);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new GpuEnergyDeviceBucket(
                    DateValue(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetDouble(4),
                    reader.GetDouble(5),
                    DateValue(reader.GetString(6))));
            }
            return rows;
        });

    public async Task<IReadOnlyList<GpuEnergyBucket>> ListGpuEnergyBucketsAsync(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null)
        => await WithConnectionAsync<IReadOnlyList<GpuEnergyBucket>>(async () =>
        {
            var rows = new List<GpuEnergyBucket>();
            await using var command = _connection.CreateCommand();
            command.CommandText = """
SELECT bucket_start_utc, watt_hours, sampled_seconds, complete_coverage,
       observed_gpu_count, detected_gpu_count, updated_at
FROM gpu_energy_hourly
""" + UtcRangeClause(fromUtc, toUtc) + """
ORDER BY bucket_start_utc;
""";
            AddUtcRangeParameters(command, fromUtc, toUtc);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new GpuEnergyBucket(
                    DateValue(reader.GetString(0)),
                    reader.GetDouble(1),
                    reader.GetDouble(2),
                    reader.GetInt64(3) == 1,
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    DateValue(reader.GetString(6))));
            }
            return rows;
        });

    public async Task<DateTimeOffset?> GetGpuEnergyTrackingStartedAtAsync()
        => await WithConnectionAsync<DateTimeOffset?>(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT MIN(bucket_start_utc) FROM gpu_energy_hourly;";
            var value = await command.ExecuteScalarAsync();
            return value is string text ? DateValue(text) : null;
        });

    public Task DeleteAllGpuEnergyAsync()
        => WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM gpu_energy_hourly; DELETE FROM gpu_energy_device_hourly;";
            await command.ExecuteNonQueryAsync();
        });
}
