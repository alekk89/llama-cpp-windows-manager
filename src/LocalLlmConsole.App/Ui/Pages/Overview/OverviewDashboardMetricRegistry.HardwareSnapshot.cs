namespace LocalLlmConsole;

public sealed partial class OverviewDashboardMetricRegistry
{
    public IReadOnlyList<OverviewDashboardMetricReading> ObserveHardware(HostHardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _hardwareDefinitions.Clear();
        var readings = new List<OverviewDashboardMetricReading>();
        AddCpuReadings(readings, snapshot.Cpu);
        AddMemoryReadings(readings, snapshot.Memory);
        AddProcessReadings(readings, snapshot.Process);
        foreach (var gpu in snapshot.Gpus)
            AddGpuReadings(readings, gpu);
        return readings;
    }

    private static void AddCpuReadings(
        List<OverviewDashboardMetricReading> readings,
        HostCpuSnapshot? cpu)
    {
        if (cpu is null) return;
        var topology = cpu.PhysicalCores is { } cores && cpu.LogicalProcessors is { } threads
            ? $"{cores}C/{threads}T"
            : cpu.PhysicalCores is { } physical
                ? $"{physical} cores"
                : cpu.LogicalProcessors is { } logical ? $"{logical} threads" : "";
        var identity = JoinDetails(cpu.Name, topology);
        readings.Add(new OverviewDashboardMetricReading(
            OverviewDashboardMetricIds.Cpu,
            PercentageValue(cpu.UtilizationPercent),
            Primary: cpu.UtilizationPercent,
            Unit: cpu.UtilizationPercent is null ? "" : "%",
            Detail: identity));
        AddSensorReading(readings, OverviewDashboardMetricIds.CpuTemperature,
            cpu.TemperatureCelsius, "0.#", "°C", cpu.Name);
        AddSensorReading(readings, OverviewDashboardMetricIds.CpuCoreClock,
            cpu.CoreClockMhz, "0", "MHz", cpu.Name);
    }

    private static void AddMemoryReadings(
        List<OverviewDashboardMetricReading> readings,
        HostMemorySnapshot? memory)
    {
        if (memory is null) return;
        var capacity = memory.UsedGibibytes is { } used && memory.TotalGibibytes is { } total
            ? $"{used:0.0}/{total:0.0} GiB"
            : "";
        readings.Add(new OverviewDashboardMetricReading(
            OverviewDashboardMetricIds.Ram,
            PercentageValue(memory.UtilizationPercent),
            Primary: memory.UtilizationPercent,
            Unit: memory.UtilizationPercent is null ? "" : "%",
            Detail: capacity));
        if (memory.UsedGibibytes is { } usedGib && memory.TotalGibibytes is { } totalGib)
            readings.Add(new OverviewDashboardMetricReading(
                OverviewDashboardMetricIds.RamUsed,
                usedGib.ToString("0.0", CultureInfo.CurrentCulture),
                Primary: usedGib,
                Secondary: totalGib,
                Unit: "GiB",
                Detail: $"of {totalGib:0.0} GiB"));
        AddSensorReading(readings, OverviewDashboardMetricIds.RamClock,
            memory.ClockMhz, "0", "MHz");
    }

    private static void AddProcessReadings(
        List<OverviewDashboardMetricReading> readings,
        HostProcessSnapshot? process)
    {
        if (process is null) return;
        AddSensorReading(readings, OverviewDashboardMetricIds.ServerProcessCpu,
            process.CpuPercent, "0.#", "%", "llama-server");
        AddSensorReading(readings, OverviewDashboardMetricIds.ServerProcessMemory,
            process.PrivateMemoryGibibytes, "0.00", "GiB", "llama-server");
    }

    private void AddGpuReadings(
        List<OverviewDashboardMetricReading> readings,
        HostGpuSnapshot gpu)
    {
        var id = OverviewDashboardMetricIds.Gpu(gpu.Index);
        _hardwareDefinitions[id] = GpuDefinition(id, gpu.Index);
        readings.Add(new OverviewDashboardMetricReading(
            id,
            PercentageValue(gpu.UtilizationPercent),
            Primary: gpu.UtilizationPercent,
            Unit: gpu.UtilizationPercent is null ? "" : "%",
            Detail: gpu.Name));

        AddGpuReading(readings, GpuSensorDefinition(
                OverviewDashboardMetricIds.GpuVram(gpu.Index), gpu.Index, "Dashboard.Metric.VramUsed"),
            gpu.VramUsedGibibytes, "0.0", "GiB", gpu.VramTotalGibibytes,
            gpu.VramTotalGibibytes is { } total ? $"of {total:0.0} GiB · {gpu.Name}" : gpu.Name);
        AddGpuReading(readings, GpuSensorDefinition(
                OverviewDashboardMetricIds.GpuPower(gpu.Index), gpu.Index, "Dashboard.Metric.PowerDraw"),
            gpu.PowerWatts, "0.#", "W", detail: gpu.Name);
        RegisterGpuEnergyDefinitions(gpu);
        AddGpuReading(readings, GpuSensorDefinition(
                OverviewDashboardMetricIds.GpuCoreClock(gpu.Index), gpu.Index, "Dashboard.Metric.CoreClock"),
            gpu.CoreClockMhz, "0", "MHz", detail: gpu.Name);
        AddGpuReading(readings, GpuSensorDefinition(
                OverviewDashboardMetricIds.GpuTemperature(gpu.Index), gpu.Index,
                Loc.T("EndpointInspection.Temperature"), 125),
            gpu.TemperatureCelsius, "0.#", "°C", detail: gpu.Name);
        AddGpuReading(readings, GpuSensorDefinition(
                OverviewDashboardMetricIds.GpuVramTemperature(gpu.Index), gpu.Index,
                "Dashboard.Metric.VramTemperature", 125),
            gpu.MemoryTemperatureCelsius, "0.#", "°C", detail: gpu.Name);
        AddGpuReading(readings, GpuSensorDefinition(
                OverviewDashboardMetricIds.GpuMemoryClock(gpu.Index), gpu.Index, "Dashboard.Metric.MemoryClock"),
            gpu.MemoryClockMhz, "0", "MHz", detail: gpu.Name);
        AddGpuReading(readings, GpuSensorDefinition(
                OverviewDashboardMetricIds.GpuMemoryActivity(gpu.Index), gpu.Index,
                "Dashboard.Metric.MemoryActivity", 100),
            gpu.MemoryActivityPercent, "0.#", "%", detail: gpu.Name);
        AddGpuReading(readings, GpuSensorDefinition(
                OverviewDashboardMetricIds.GpuFanSpeed(gpu.Index), gpu.Index,
                "Dashboard.Metric.FanSpeed", 100),
            gpu.FanSpeedPercent, "0.#", "%", detail: gpu.Name);
        AddGpuReading(readings, GpuSensorDefinition(
                OverviewDashboardMetricIds.GpuPowerLimit(gpu.Index), gpu.Index,
                "Dashboard.Metric.PowerLimit", chartable: false),
            gpu.PowerLimitWatts, "0.#", "W", detail: gpu.Name);
        if (gpu.IsThrottling is { } throttling)
            AddGpuThrottleReading(readings, gpu, throttling);
    }

    private void RegisterGpuEnergyDefinitions(HostGpuSnapshot gpu)
    {
        if (gpu.PowerWatts is null) return;
        var energyId = OverviewDashboardMetricIds.ObservedGpuEnergy(gpu.Index);
        _hardwareDefinitions[energyId] = ObservedEnergyDeviceDefinition(energyId, gpu.Index, gpu.Name);
        var costId = OverviewDashboardMetricIds.ObservedGpuElectricityCost(gpu.Index);
        _hardwareDefinitions[costId] = ObservedElectricityCostDeviceDefinition(costId, gpu.Index, gpu.Name);
    }

    private void AddGpuThrottleReading(
        List<OverviewDashboardMetricReading> readings,
        HostGpuSnapshot gpu,
        bool active)
    {
        var definition = GpuSensorDefinition(
            OverviewDashboardMetricIds.GpuThrottling(gpu.Index),
            gpu.Index,
            "Dashboard.Metric.Throttling",
            1,
            chartable: false);
        _hardwareDefinitions[definition.Id] = definition;
        readings.Add(new(definition.Id,
            active ? Loc.T("Dashboard.Value.Active") : Loc.T("Dashboard.Value.None"),
            Primary: active ? 1 : 0,
            Detail: gpu.Name));
    }
}
