namespace LocalLlmConsole.Services;

public sealed class GpuEnergyAccumulator
{
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromSeconds(30);
    private GpuPowerObservation? _previous;

    public IReadOnlyList<GpuEnergyDelta> Observe(GpuPowerObservation observation)
        => ObserveDetailed(observation).TotalDeltas;

    public GpuEnergySampleResult ObserveDetailed(GpuPowerObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!observation.HasPower)
        {
            _previous = null;
            return GpuEnergySampleResult.Empty;
        }

        var previous = _previous;
        _previous = observation with
        {
            CapturedAt = observation.CapturedAt.ToUniversalTime(),
            SensorKeys = NormalizeKeys(observation.SensorKeys)
        };
        if (previous is null) return GpuEnergySampleResult.Empty;

        previous = previous with
        {
            CapturedAt = previous.CapturedAt.ToUniversalTime(),
            SensorKeys = NormalizeKeys(previous.SensorKeys)
        };
        var elapsed = _previous.CapturedAt - previous.CapturedAt;
        if (elapsed <= TimeSpan.Zero
            || elapsed > MaximumInterval
            || !previous.SensorKeys.SequenceEqual(_previous.SensorKeys, StringComparer.Ordinal))
            return GpuEnergySampleResult.Empty;

        return new GpuEnergySampleResult(
            SplitAcrossUtcHours(previous, _previous, elapsed),
            SplitDevicesAcrossUtcHours(previous, _previous, elapsed));
    }

    public void Reset() => _previous = null;

    private static IReadOnlyList<GpuEnergyDelta> SplitAcrossUtcHours(
        GpuPowerObservation previous,
        GpuPowerObservation current,
        TimeSpan elapsed)
    {
        var result = new List<GpuEnergyDelta>(2);
        var cursor = previous.CapturedAt;
        while (cursor < current.CapturedAt)
        {
            var hourStart = UtcHour(cursor);
            var nextHour = hourStart.AddHours(1);
            var segmentEnd = current.CapturedAt < nextHour ? current.CapturedAt : nextHour;
            var startRatio = (cursor - previous.CapturedAt).TotalSeconds / elapsed.TotalSeconds;
            var endRatio = (segmentEnd - previous.CapturedAt).TotalSeconds / elapsed.TotalSeconds;
            var startWatts = Lerp(previous.TotalWatts, current.TotalWatts, startRatio);
            var endWatts = Lerp(previous.TotalWatts, current.TotalWatts, endRatio);
            var seconds = (segmentEnd - cursor).TotalSeconds;
            result.Add(new GpuEnergyDelta(
                hourStart,
                (startWatts + endWatts) * .5 * seconds / 3600,
                seconds,
                previous.HasCompleteCoverage && current.HasCompleteCoverage,
                Math.Min(previous.ObservedGpuCount, current.ObservedGpuCount),
                Math.Max(previous.DetectedGpuCount, current.DetectedGpuCount),
                current.CapturedAt));
            cursor = segmentEnd;
        }
        return result;
    }

    private static IReadOnlyList<GpuEnergyDeviceDelta> SplitDevicesAcrossUtcHours(
        GpuPowerObservation previous,
        GpuPowerObservation current,
        TimeSpan elapsed)
    {
        var previousSensors = previous.Sensors.ToDictionary(
            sensor => NormalizeKey(sensor.Key),
            StringComparer.Ordinal);
        var currentSensors = current.Sensors.ToDictionary(
            sensor => NormalizeKey(sensor.Key),
            StringComparer.Ordinal);
        if (previousSensors.Count == 0 || previousSensors.Count != currentSensors.Count)
            return [];

        var result = new List<GpuEnergyDeviceDelta>();
        foreach (var (key, startSensor) in previousSensors)
        {
            if (!currentSensors.TryGetValue(key, out var endSensor)) return [];
            var cursor = previous.CapturedAt;
            while (cursor < current.CapturedAt)
            {
                var hourStart = UtcHour(cursor);
                var nextHour = hourStart.AddHours(1);
                var segmentEnd = current.CapturedAt < nextHour ? current.CapturedAt : nextHour;
                var startRatio = (cursor - previous.CapturedAt).TotalSeconds / elapsed.TotalSeconds;
                var endRatio = (segmentEnd - previous.CapturedAt).TotalSeconds / elapsed.TotalSeconds;
                var startWatts = Lerp(startSensor.Watts, endSensor.Watts, startRatio);
                var endWatts = Lerp(startSensor.Watts, endSensor.Watts, endRatio);
                var seconds = (segmentEnd - cursor).TotalSeconds;
                result.Add(new GpuEnergyDeviceDelta(
                    hourStart,
                    endSensor.Key,
                    endSensor.GpuIndex,
                    endSensor.GpuName,
                    (startWatts + endWatts) * .5 * seconds / 3600,
                    seconds,
                    current.CapturedAt));
                cursor = segmentEnd;
            }
        }
        return result;
    }

    private static IReadOnlyList<string> NormalizeKeys(IEnumerable<string>? keys)
        => keys?.Where(key => !string.IsNullOrWhiteSpace(key))
               .Select(key => key.Trim().ToUpperInvariant())
               .Distinct(StringComparer.Ordinal)
               .OrderBy(key => key, StringComparer.Ordinal)
               .ToArray()
           ?? [];

    private static string NormalizeKey(string key)
        => key.Trim().ToUpperInvariant();

    private static DateTimeOffset UtcHour(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    private static double Lerp(double start, double end, double ratio)
        => start + (end - start) * Math.Clamp(ratio, 0, 1);
}
