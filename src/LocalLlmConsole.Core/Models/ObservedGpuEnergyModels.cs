namespace LocalLlmConsole.Models;

public sealed record ObservedGpuEnergyDevice(
    string SensorKey,
    int GpuIndex,
    string GpuName,
    double WattHours,
    double ElectricityCost = 0)
{
    public double KilowattHours => WattHours / 1000;
}

/// <summary>
/// Host GPU energy observed since this Manager process started. The snapshot is
/// deliberately independent of model sessions and is not persisted; its
/// measured deltas are persisted separately by the lifetime metrics service.
/// </summary>
public sealed record ObservedGpuEnergySnapshot(
    DateTimeOffset StartedAt,
    DateTimeOffset CapturedAt,
    IReadOnlyList<ObservedGpuEnergyDevice> Devices,
    string ElectricityCurrencyCode = "")
{
    public double WattHours => Devices.Sum(device => Math.Max(0, device.WattHours));
    public double KilowattHours => WattHours / 1000;
    public double ElectricityCost => Devices.Sum(device => Math.Max(0, device.ElectricityCost));
}
