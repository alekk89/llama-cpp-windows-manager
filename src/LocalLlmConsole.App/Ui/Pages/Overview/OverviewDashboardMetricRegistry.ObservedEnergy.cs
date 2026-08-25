namespace LocalLlmConsole;

public sealed partial class OverviewDashboardMetricRegistry
{
    public IReadOnlyList<OverviewDashboardMetricReading> ObserveGpuEnergy(
        ObservedGpuEnergySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Devices.Count == 0) return [];

        var readings = new List<OverviewDashboardMetricReading>
        {
            new(
                OverviewDashboardMetricIds.ObservedGpuEnergyTotal,
                EnergyValue(snapshot.KilowattHours),
                ObservedEnergyRuntimeKey,
                snapshot.KilowattHours,
                Unit: "kWh",
                Detail: Loc.T("Dashboard.Detail.PowerReportingGpus", snapshot.Devices.Count)),
            new(
                OverviewDashboardMetricIds.ObservedGpuElectricityCostTotal,
                CostValue(snapshot.ElectricityCost),
                ObservedEnergyRuntimeKey,
                snapshot.ElectricityCost,
                Unit: snapshot.ElectricityCurrencyCode,
                Detail: Loc.T("Dashboard.Detail.CurrentTariff"))
        };
        foreach (var device in snapshot.Devices)
        {
            var id = OverviewDashboardMetricIds.ObservedGpuEnergy(device.GpuIndex);
            _hardwareDefinitions[id] = ObservedEnergyDeviceDefinition(id, device.GpuIndex, device.GpuName);
            readings.Add(new OverviewDashboardMetricReading(
                id,
                EnergyValue(device.KilowattHours),
                ObservedEnergyRuntimeKey,
                device.KilowattHours,
                Unit: "kWh",
                Detail: device.GpuName));
            var costId = OverviewDashboardMetricIds.ObservedGpuElectricityCost(device.GpuIndex);
            _hardwareDefinitions[costId] = ObservedElectricityCostDeviceDefinition(
                costId, device.GpuIndex, device.GpuName);
            readings.Add(new OverviewDashboardMetricReading(
                costId,
                CostValue(device.ElectricityCost),
                ObservedEnergyRuntimeKey,
                device.ElectricityCost,
                Unit: snapshot.ElectricityCurrencyCode,
                Detail: device.GpuName));
        }
        return readings;
    }

    private const string ObservedEnergyRuntimeKey = "overview-observed-energy-live";

    private static OverviewDashboardMetricDefinition ObservedEnergyTotalDefinition()
        => ObservedEnergyDefinition(
            OverviewDashboardMetricIds.ObservedGpuEnergyTotal,
            Loc.T("Dashboard.Metric.ObservedEnergyLiveTotal"));

    private static OverviewDashboardMetricDefinition ObservedElectricityCostTotalDefinition()
        => ObservedElectricityCostDefinition(
            OverviewDashboardMetricIds.ObservedGpuElectricityCostTotal,
            Loc.T("Dashboard.Metric.ObservedElectricityCostLiveTotal"));

    private static OverviewDashboardMetricDefinition ObservedElectricityCostDeviceDefinition(
        string id,
        int index,
        string gpuName = "")
        => ObservedElectricityCostDefinition(
            id,
            Loc.T("Dashboard.Metric.ObservedElectricityCostLiveGpu", Loc.T("Dashboard.Metric.Gpu", index)),
            gpuName);

    private static OverviewDashboardMetricDefinition ObservedEnergyDeviceDefinition(
        string id,
        int index,
        string gpuName = "")
        => ObservedEnergyDefinition(
            id,
            Loc.T("Dashboard.Metric.ObservedEnergyLiveGpu", Loc.T("Dashboard.Metric.Gpu", index)),
            gpuName);

    private static OverviewDashboardMetricDefinition ObservedEnergyDefinition(
        string id,
        string displayName,
        string gpuName = "")
        => new(
            id,
            displayName,
            Loc.T("Dashboard.Category.Energy"),
            Chartable: false,
            Presentation: OverviewDashboardMetricPresentation.Hardware,
            RequiresObservedValue: false,
            Tooltip: string.Join("\n", new[]
            {
                Loc.T("Dashboard.Tooltip.ObservedEnergyLive"),
                Loc.T("Dashboard.Tooltip.ObservedEnergyReset"),
                string.IsNullOrWhiteSpace(gpuName) ? "" : Loc.T("Dashboard.Tooltip.Device", gpuName),
                Loc.T("Dashboard.Tooltip.TechnicalMetric", id)
            }.Where(value => !string.IsNullOrWhiteSpace(value))));

    private static OverviewDashboardMetricDefinition ObservedElectricityCostDefinition(
        string id,
        string displayName,
        string gpuName = "")
        => new(
            id,
            displayName,
            Loc.T("Dashboard.Category.Energy"),
            Chartable: false,
            Presentation: OverviewDashboardMetricPresentation.Hardware,
            RequiresObservedValue: false,
            Tooltip: string.Join("\n", new[]
            {
                Loc.T("Dashboard.Tooltip.ObservedElectricityCostLive"),
                Loc.T("Dashboard.Tooltip.ObservedEnergyReset"),
                Loc.T("Dashboard.Tooltip.TelemetryGaps"),
                string.IsNullOrWhiteSpace(gpuName) ? "" : Loc.T("Dashboard.Tooltip.Device", gpuName),
                Loc.T("Dashboard.Tooltip.TechnicalMetric", id)
            }.Where(value => !string.IsNullOrWhiteSpace(value))));

    private static string EnergyValue(double kilowattHours)
        => kilowattHours.ToString(kilowattHours < 1 ? "N4" : "N2", CultureInfo.CurrentCulture);

    private static string CostValue(double amount)
        => amount.ToString(amount < 1 ? "N4" : "N2", CultureInfo.CurrentCulture);
}
