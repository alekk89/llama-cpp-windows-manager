namespace LocalLlmConsole.Services;

internal static class BenchmarkMemoryReportService
{
    internal static string Label(BenchmarkParsedResult result)
    {
        var readings = result.GpuMemoryPeaks ?? [];
        if (readings.Count == 0)
            return result.ObservedGpuMemoryUsedMiB > 0
                ? Loc.T("Benchmark.Memory.Legacy", result.ObservedGpuMemoryUsedMiB.ToString("N0"))
                : Loc.T("Benchmark.Memory.Unavailable");
        var scope = Loc.T(result.GpuMemoryMeasurementWindow == "process"
            ? "Benchmark.Memory.ProcessPeak" : "Benchmark.Memory.WorkloadPeak");
        var devices = readings.Select((peak, index) => Loc.T("Benchmark.Memory.Device",
            $"GPU {index}: {peak.DeviceName}", Value(peak.PeakDedicatedUsedMiB), Value(peak.DedicatedCapacityMiB), Value(peak.PeakSharedUsedMiB)));
        return scope + ": " + string.Join("; ", devices);
    }

    private static string Value(long? value)
        => value.HasValue ? value.Value.ToString("N0") + " MiB" : Loc.T("Benchmark.Memory.UnavailableValue");
}
