namespace LocalLlmConsole.Services;

public static class GpuPowerObservationParser
{
    public static GpuPowerObservation Parse(string summary, DateTimeOffset capturedAt)
        => Parse(HostHardwareSnapshotParser.Parse(summary, capturedAt), capturedAt);

    public static GpuPowerObservation Parse(HostHardwareSnapshot snapshot, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sensors = snapshot.Gpus
            .Where(gpu => gpu.PowerWatts is >= 0 and <= 2000)
            .Select(gpu => new GpuPowerSensorReading(
                $"GPU {gpu.Index}: {gpu.Name}",
                gpu.Index,
                gpu.Name,
                gpu.PowerWatts!.Value))
            .ToArray();

        return new GpuPowerObservation(
            capturedAt.ToUniversalTime(),
            sensors.Sum(sensor => sensor.Watts),
            sensors.Select(sensor => sensor.Key).ToArray(),
            snapshot.Gpus.Count)
        {
            Sensors = sensors
        };
    }
}
