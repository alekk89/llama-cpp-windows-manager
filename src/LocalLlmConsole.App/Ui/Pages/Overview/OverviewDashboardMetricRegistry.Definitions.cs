namespace LocalLlmConsole;

public sealed partial class OverviewDashboardMetricRegistry
{
    public static IReadOnlyList<OverviewDashboardMetricDefinition> BuiltInDefinitions()
        =>
        [
            Definition(OverviewDashboardMetricIds.ModelStatus, Loc.T("Overview.Metric.ModelStatus"), Loc.T("Dashboard.Category.Core"), false, presentation: OverviewDashboardMetricPresentation.Status),
            Metric(OverviewDashboardMetricIds.Cpu, "Cpu", "Hardware", true, FixedMaximum: 100, presentation: OverviewDashboardMetricPresentation.Hardware, displayName: Loc.T("Dashboard.Metric.CpuUsage")),
            HardwareDefinition(OverviewDashboardMetricIds.CpuTemperature, $"{Loc.T("Dashboard.Metric.Cpu")} · {Loc.T("EndpointInspection.Temperature")}", 125),
            HardwareDefinition(OverviewDashboardMetricIds.CpuCoreClock, $"{Loc.T("Dashboard.Metric.Cpu")} · {Loc.T("Dashboard.Metric.CoreClock")}"),
            Metric(OverviewDashboardMetricIds.Ram, "Ram", "Hardware", true, FixedMaximum: 100, presentation: OverviewDashboardMetricPresentation.Hardware, displayName: Loc.T("Dashboard.Metric.MemoryUsage")),
            HardwareDefinition(OverviewDashboardMetricIds.RamUsed, $"{Loc.T("Dashboard.Metric.Ram")} · {Loc.T("Dashboard.Metric.MemoryUsed")}", pickerVisible: false),
            HardwareDefinition(OverviewDashboardMetricIds.RamClock, $"{Loc.T("Dashboard.Metric.Ram")} · {Loc.T("Dashboard.Metric.MemoryClock")}", chartable: false, pickerVisible: false),
            HardwareDefinition(OverviewDashboardMetricIds.ServerProcessCpu, Loc.T("Dashboard.Metric.ServerProcessCpu"), 100),
            HardwareDefinition(OverviewDashboardMetricIds.ServerProcessMemory, Loc.T("Dashboard.Metric.ServerProcessMemory")),
            Metric(OverviewDashboardMetricIds.ActiveSlots, "ActiveSlots", "Core", false, presentation: OverviewDashboardMetricPresentation.Count),
            Metric(OverviewDashboardMetricIds.QueuedRequests, "QueuedRequests", "Core", false, presentation: OverviewDashboardMetricPresentation.Count),
            Metric(OverviewDashboardMetricIds.BusyDecodeSlots, "AverageDecodeConcurrency", "Advanced", false, presentation: OverviewDashboardMetricPresentation.Count),
            Metric(OverviewDashboardMetricIds.RecentGenerationRate, "RecentGenerationRate", "Core", true, presentation: OverviewDashboardMetricPresentation.Rate),
            Metric(OverviewDashboardMetricIds.RecentPromptRate, "RecentPromptRate", "Core", true, "Accent", presentation: OverviewDashboardMetricPresentation.Rate),
            Metric(OverviewDashboardMetricIds.AverageGenerationRate, "AverageGenerationRate", "Core", true, presentation: OverviewDashboardMetricPresentation.Rate),
            Metric(OverviewDashboardMetricIds.AveragePromptRate, "AveragePromptRate", "Core", true, "Accent", presentation: OverviewDashboardMetricPresentation.Rate),
            Metric(OverviewDashboardMetricIds.PromptCacheReuse, "PromptCacheReuse", "Core", true, FixedMaximum: 100, presentation: OverviewDashboardMetricPresentation.Percentage),
            Metric(OverviewDashboardMetricIds.DraftAcceptance, "DraftAcceptance", "Core", true, "Accent", FixedMaximum: 100, presentation: OverviewDashboardMetricPresentation.Percentage),
            Metric(OverviewDashboardMetricIds.PeakContextUsed, "PeakContextUsed", "Core", true, presentation: OverviewDashboardMetricPresentation.TokenCount),
            Metric(OverviewDashboardMetricIds.ContextShifts, "ContextShifts", "Advanced", false, presentation: OverviewDashboardMetricPresentation.Count, requiresObservedValue: true),
            Metric(OverviewDashboardMetricIds.GeneratedTokens, "GeneratedTokens", "Advanced", false, presentation: OverviewDashboardMetricPresentation.TokenCount),
            Metric(OverviewDashboardMetricIds.PromptTokens, "PromptTokens", "Advanced", false, "Accent", presentation: OverviewDashboardMetricPresentation.TokenCount),
            Metric(OverviewDashboardMetricIds.AverageMtpGeneratedRate, "AverageMtpGeneratedRate", "Advanced", true, "Warning", presentation: OverviewDashboardMetricPresentation.Rate),
            Metric(OverviewDashboardMetricIds.AverageMtpAcceptedRate, "AverageMtpAcceptedRate", "Advanced", false, "Accent", presentation: OverviewDashboardMetricPresentation.Rate, pickerVisible: false),
            Metric(OverviewDashboardMetricIds.MtpGeneratedTokens, "MtpGeneratedTokens", "Advanced", false, "Warning", presentation: OverviewDashboardMetricPresentation.TokenCount),
            Metric(OverviewDashboardMetricIds.MtpAcceptedTokens, "MtpAcceptedTokens", "Advanced", false, "Accent", presentation: OverviewDashboardMetricPresentation.TokenCount),
            Metric(OverviewDashboardMetricIds.KvCacheUsed, "KvCacheUsed", "Advanced", false, presentation: OverviewDashboardMetricPresentation.TokenCount, pickerVisible: false),
            Metric(OverviewDashboardMetricIds.KvCacheCapacity, "KvCacheCapacity", "Advanced", false, presentation: OverviewDashboardMetricPresentation.TokenCount, pickerVisible: false),
            Metric(OverviewDashboardMetricIds.KvCacheUsage, "KvCacheUsage", "Core", true, FixedMaximum: 100, presentation: OverviewDashboardMetricPresentation.Percentage),
            Metric(OverviewDashboardMetricIds.KvCacheAllocation, "KvCacheAllocation", "Advanced", false, presentation: OverviewDashboardMetricPresentation.Text, pickerVisible: false),
            GatewayMetric(OverviewDashboardMetricIds.GatewayTimeToFirstData, "GatewayTimeToFirstData", true, OverviewDashboardMetricPresentation.Count),
            GatewayMetric(OverviewDashboardMetricIds.GatewayRequestDuration, "GatewayRequestDuration", true, OverviewDashboardMetricPresentation.Count),
            GatewayMetric(OverviewDashboardMetricIds.GatewayResponseThroughput, "GatewayResponseThroughput", true, OverviewDashboardMetricPresentation.Rate),
            GatewayMetric(OverviewDashboardMetricIds.GatewayRequests, "GatewayRequests", false, OverviewDashboardMetricPresentation.Count),
            GatewayMetric(OverviewDashboardMetricIds.GatewayFailures, "GatewayFailures", false, OverviewDashboardMetricPresentation.Count),
            GatewayMetric(OverviewDashboardMetricIds.GatewayFailureRate, "GatewayFailureRate", true, OverviewDashboardMetricPresentation.Percentage, 100),
            ObservedEnergyTotalDefinition(),
            ObservedElectricityCostTotalDefinition()
        ];

    private static OverviewDashboardMetricDefinition GatewayMetric(string id, string name, bool chartable, OverviewDashboardMetricPresentation presentation, double? maximum = null)
        => Metric(id, name, "Gateway", chartable, FixedMaximum: maximum, presentation: presentation, requiresObservedValue: true);

    private static OverviewDashboardMetricDefinition Metric(string id, string name, string category, bool chartable, string primaryBrushKey = "AccentBlue", double? FixedMaximum = null, OverviewDashboardMetricPresentation presentation = OverviewDashboardMetricPresentation.Text, string? displayName = null, bool pickerVisible = true, bool requiresObservedValue = false)
        => Definition(id, displayName ?? Loc.T($"Dashboard.Metric.{name}"), Loc.T($"Dashboard.Category.{category}"), chartable, primaryBrushKey, FixedMaximum: FixedMaximum, presentation: presentation, requiresObservedValue: requiresObservedValue, pickerVisible: pickerVisible);

    private static OverviewDashboardMetricDefinition GpuDefinition(string id, int index)
        => Definition(id, Loc.T("Dashboard.Metric.GpuUsage", index), Loc.T("Dashboard.Category.Hardware"), true, FixedMaximum: 100, presentation: OverviewDashboardMetricPresentation.Hardware, requiresObservedValue: true);

    private static OverviewDashboardMetricDefinition GpuSensorDefinition(string id, int index, string suffixKey, double? fixedMaximum = null, bool chartable = true)
        => HardwareDefinition(id, $"{Loc.T("Dashboard.Metric.Gpu", index)} · {Loc.T(suffixKey)}", fixedMaximum, chartable);

    private static OverviewDashboardMetricDefinition HardwareDefinition(string id, string displayName, double? fixedMaximum = null, bool chartable = true, bool requiresObservedValue = true, bool pickerVisible = true)
        => Definition(id, displayName, Loc.T("Dashboard.Category.Hardware"), chartable, FixedMaximum: fixedMaximum, presentation: OverviewDashboardMetricPresentation.Hardware, requiresObservedValue: requiresObservedValue, pickerVisible: pickerVisible);
}
