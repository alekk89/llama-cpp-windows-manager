namespace LocalLlmConsole.Models;

// Device-wide memory, including other applications. Null means the driver did not supply a reading.
public sealed record GpuMemorySample(
    string DeviceId,
    string DeviceName,
    long? DedicatedCapacityMiB,
    long? DedicatedUsedMiB,
    long? SharedUsedMiB);

public sealed record BenchmarkGpuMemoryPeak(
    string DeviceId,
    string DeviceName,
    long? DedicatedCapacityMiB,
    long? PeakDedicatedUsedMiB,
    long? PeakSharedUsedMiB,
    int SampleCount);
