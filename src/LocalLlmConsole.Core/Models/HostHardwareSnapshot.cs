namespace LocalLlmConsole.Models;

public sealed record HostCpuSnapshot(
    string Name,
    double? UtilizationPercent,
    double? TemperatureCelsius,
    double? CoreClockMhz,
    int? PhysicalCores,
    int? LogicalProcessors);

public sealed record HostMemorySnapshot(
    double? UtilizationPercent,
    double? UsedGibibytes,
    double? TotalGibibytes,
    double? ClockMhz);

public sealed record HostProcessSnapshot(
    double? CpuPercent,
    double? PrivateMemoryGibibytes);

public sealed record HostGpuSnapshot(
    int Index,
    string Name,
    double? UtilizationPercent,
    double? VramUsedGibibytes,
    double? VramTotalGibibytes,
    double? PowerWatts,
    double? CoreClockMhz,
    double? TemperatureCelsius,
    double? MemoryTemperatureCelsius,
    double? MemoryClockMhz,
    double? MemoryActivityPercent,
    double? FanSpeedPercent,
    double? PowerLimitWatts,
    bool? IsThrottling);

public sealed record HostHardwareSnapshot(
    string Summary,
    HostCpuSnapshot? Cpu,
    HostMemorySnapshot? Memory,
    HostProcessSnapshot? Process,
    IReadOnlyList<HostGpuSnapshot> Gpus,
    DateTimeOffset CapturedAt)
{
    public static HostHardwareSnapshot Unavailable(DateTimeOffset capturedAt)
        => new("Unavailable", null, null, null, [], capturedAt.ToUniversalTime());
}
