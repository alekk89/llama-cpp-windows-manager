using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public static class BenchmarkGpuMemoryService
{
    public static IReadOnlyList<BenchmarkGpuMemoryPeak> Merge(IEnumerable<BenchmarkGpuMemoryPeak> readings)
        => readings.GroupBy(reading => reading.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new BenchmarkGpuMemoryPeak(
                group.Key, group.First().DeviceName,
                group.Max(reading => reading.DedicatedCapacityMiB),
                group.Max(reading => reading.PeakDedicatedUsedMiB),
                group.Max(reading => reading.PeakSharedUsedMiB),
                group.Sum(reading => reading.SampleCount)))
            .ToArray();
}
