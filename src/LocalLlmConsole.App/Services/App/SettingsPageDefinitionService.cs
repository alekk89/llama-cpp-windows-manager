namespace LocalLlmConsole.Services;

public sealed class SettingsPageDefinitionService
{
    public IReadOnlyList<SettingRowDefinition> BuildRows(AppSettings settings, long? cacheSizeBytes = null) =>
    [
        new(Loc.T("Settings.Group.Storage"), Loc.T("Setting.Cache"), "cache",
            CacheDisplayValue(settings.CacheRoot, cacheSizeBytes), "readonly", Action: Loc.T("Action.Clear"),
            ToolTip: Loc.T("Tooltip.Setting.Cache")),
        new(Loc.T("Settings.Group.Window"), Loc.T("Setting.MinimizeBehavior"), "minimizeBehavior", AppPreferenceService.MinimizeBehaviorLabel(settings.MinimizeBehavior), "choice", AppPreferenceService.MinimizeBehaviorOptions(),
            ToolTip: Loc.T("Tooltip.Setting.MinimizeBehavior")),
        new(Loc.T("Settings.Group.Window"), Loc.T("Setting.StartWithWindows"), "startWithWindows", AppPreferenceService.YesNoLabel(settings.StartWithWindows), "choice", AppPreferenceService.YesNoOptions(),
            ToolTip: Loc.T("Tooltip.Setting.StartWithWindows")),
        new(Loc.T("Settings.Group.Model"), Loc.T("Setting.AutoUnloadIdleMin"), "autoUnloadIdleMinutes", settings.AutoUnloadIdleMinutes.ToString(CultureInfo.InvariantCulture),
            ToolTip: Loc.T("Tooltip.Setting.AutoUnloadIdleMin")),
        new(Loc.T("Settings.Group.Runtime"), Loc.T("Setting.DeleteSourceAfterBuild"), "deleteRuntimeSourceAfterSuccessfulBuild", AppPreferenceService.YesNoLabel(settings.DeleteRuntimeSourceAfterSuccessfulBuild), "choice", AppPreferenceService.YesNoOptions(),
            ToolTip: Loc.T("Tooltip.Setting.DeleteSourceAfterBuild")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.LanExposure"), "modelAccessMode", AppPreferenceService.ModelAccessModeLabel(settings.ModelAccessMode), "choice", AppPreferenceService.ModelAccessModeOptions(),
            ToolTip: Loc.T("Tooltip.Setting.LanExposure")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.AutoLoadGateway"), "autoLoadGatewayEnabled", AppPreferenceService.YesNoLabel(settings.AutoLoadGatewayEnabled), "choice", AppPreferenceService.YesNoOptions(),
            ToolTip: Loc.T("Tooltip.Setting.AutoLoadGateway")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.GatewayPort"), "autoLoadGatewayPort", settings.AutoLoadGatewayPort.ToString(CultureInfo.InvariantCulture),
            ToolTip: Loc.T("Tooltip.Setting.GatewayPort")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.GatewayPolicy"), "autoLoadGatewayPolicy", AppPreferenceService.GatewaySwapPolicyLabel(settings.AutoLoadGatewayPolicy), "choice", AppPreferenceService.GatewaySwapPolicyOptions(),
            ToolTip: Loc.T("Tooltip.Setting.GatewayPolicy")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.ApiKeyAuth"), "requireApiKeyAuth", AppPreferenceService.EnableDisableLabel(settings.RequireApiKeyAuth), "choice", AppPreferenceService.EnableDisableOptions(),
            ToolTip: Loc.T("Tooltip.Setting.ApiKeyAuth")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.ApiKey"), "modelApiKey", settings.ModelApiKey, "secret", Action: Loc.T("Action.Generate"),
            ToolTip: Loc.T("Tooltip.Setting.ApiKey")),
        UiVisibility(Loc.T("Overview.Metric.ModelStatus"), "showOverviewModelStatus", settings.ShowOverviewModelStatus),
        UiVisibility(Loc.T("Overview.Metric.Hardware"), "showOverviewHardware", settings.ShowOverviewHardware),
        UiVisibility(Loc.T("Overview.Metric.Slots"), "showOverviewSlots", settings.ShowOverviewSlots),
        UiVisibility(Loc.T("Overview.Metric.Tokens"), "showOverviewTokens", settings.ShowOverviewTokens),
        UiVisibility(Loc.T("Overview.Metric.MtpTokens"), "showOverviewMtpTokens", settings.ShowOverviewMtpTokens),
        UiVisibility(Loc.T("Overview.Metric.KvCache"), "showOverviewKvCache", settings.ShowOverviewKvCache),
        UiVisibility(Loc.T("Overview.LiveRuntimeLogTitle"), "showOverviewLiveRuntimeLog", settings.ShowOverviewLiveRuntimeLog),
        UiVisibility(Loc.T("Overview.RuntimeMetricsTitle"), "showOverviewAllMetrics", settings.ShowOverviewAllMetrics),
        new(Loc.T("Settings.Group.UI"), Loc.T("Setting.ShowModelsHuggingFace"), "showModelsHuggingFace", AppPreferenceService.ShowHideLabel(settings.ShowModelsHuggingFace), "choice", AppPreferenceService.ShowHideOptions(),
            ToolTip: Loc.T("Tooltip.Setting.ShowModelsHuggingFace")),
        new(Loc.T("Settings.Group.Electricity"), Loc.T("Setting.ElectricityCurrency"), "electricityCurrencyCode", settings.ElectricityCurrencyCode,
            ToolTip: Loc.T("Tooltip.Setting.ElectricityCurrency")),
        new(Loc.T("Settings.Group.Electricity"), Loc.T("Setting.ElectricityDayRate"), "electricityDayRatePerKwh", settings.ElectricityDayRatePerKwh.ToString(CultureInfo.InvariantCulture),
            ToolTip: Loc.T("Tooltip.Setting.ElectricityDayRate")),
        new(Loc.T("Settings.Group.Electricity"), Loc.T("Setting.ElectricityNightRate"), "electricityNightRatePerKwh", settings.ElectricityNightRatePerKwh.ToString(CultureInfo.InvariantCulture),
            ToolTip: Loc.T("Tooltip.Setting.ElectricityNightRate")),
        new(Loc.T("Settings.Group.Electricity"), Loc.T("Setting.ElectricityNightStart"), "electricityNightStartLocal", settings.ElectricityNightStartLocal,
            ToolTip: Loc.T("Tooltip.Setting.ElectricityNightStart")),
        new(Loc.T("Settings.Group.Electricity"), Loc.T("Setting.ElectricityNightEnd"), "electricityNightEndLocal", settings.ElectricityNightEndLocal,
            ToolTip: Loc.T("Tooltip.Setting.ElectricityNightEnd")),
        new(Loc.T("Settings.Group.Electricity"), Loc.T("Setting.TrackGpuEnergyWhileIdle"), "trackGpuEnergyWhileIdle",
            AppPreferenceService.YesNoLabel(settings.TrackGpuEnergyWhileIdle), "choice", AppPreferenceService.YesNoOptions(),
            ToolTip: Loc.T("Tooltip.Setting.TrackGpuEnergyWhileIdle")),
        new(Loc.T("Settings.Group.Logs"), Loc.T("Setting.MaxLogFileSizeMb"), "maxLogFileSizeMb", settings.MaxLogFileSizeMb.ToString(CultureInfo.InvariantCulture),
            ToolTip: Loc.T("Tooltip.Setting.MaxLogFileSizeMb")),
        new(Loc.T("Settings.Group.Logs"), Loc.T("Setting.RuntimeLogOrder"), "runtimeLogOrder",
            AppPreferenceService.RuntimeLogOrderLabel(settings.RuntimeLogOrder), "choice", AppPreferenceService.RuntimeLogOrderOptions(),
            ToolTip: Loc.T("Tooltip.Setting.RuntimeLogOrder"))
    ];

    public string CacheDisplayValue(string cacheRoot, long? cacheSizeBytes)
        => Loc.T(
            "Setting.CacheValue",
            cacheSizeBytes.HasValue ? DisplayFormatService.BytesOrZero(cacheSizeBytes.Value) : Loc.T("Status.Refreshing"),
            cacheRoot);

    private static SettingRowDefinition UiVisibility(string label, string key, bool visible)
        => new(
            Loc.T("Settings.Group.UI"),
            label,
            key,
            AppPreferenceService.ShowHideLabel(visible),
            "choice",
            AppPreferenceService.ShowHideOptions(),
            ToolTip: Loc.T("Tooltip.Setting.ShowOverviewSection", label));
}
