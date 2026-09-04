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
        new(Loc.T("Settings.Group.Model"), Loc.T("Setting.SameModelLoadPolicy"), "sameModelLoadPolicy", AppPreferenceService.SameModelLoadPolicyLabel(settings.SameModelLoadPolicy), "choice", AppPreferenceService.SameModelLoadPolicyOptions(),
            ToolTip: Loc.T("Tooltip.Setting.SameModelLoadPolicy")),
        new(Loc.T("Settings.Group.Runtime"), Loc.T("Setting.DeleteSourceAfterBuild"), "deleteRuntimeSourceAfterSuccessfulBuild", AppPreferenceService.YesNoLabel(settings.DeleteRuntimeSourceAfterSuccessfulBuild), "choice", AppPreferenceService.YesNoOptions(),
            ToolTip: Loc.T("Tooltip.Setting.DeleteSourceAfterBuild")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.LanExposure"), "modelAccessMode", AppPreferenceService.ModelAccessModeLabel(settings.ModelAccessMode), "choice", AppPreferenceService.ModelAccessModeOptions(),
            ToolTip: Loc.T("Tooltip.Setting.LanExposure")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.ModelGateway"), "autoLoadGatewayEnabled", AppPreferenceService.YesNoLabel(settings.AutoLoadGatewayEnabled), "choice", AppPreferenceService.YesNoOptions(),
            ToolTip: Loc.T("Tooltip.Setting.ModelGateway")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.GatewayAutoLoadModels"), "gatewayAutoLoadModels", AppPreferenceService.YesNoLabel(settings.GatewayAutoLoadModels), "choice", AppPreferenceService.YesNoOptions(),
            ToolTip: Loc.T("Tooltip.Setting.GatewayAutoLoadModels")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.GatewayPort"), "autoLoadGatewayPort", settings.AutoLoadGatewayPort.ToString(CultureInfo.InvariantCulture),
            ToolTip: Loc.T("Tooltip.Setting.GatewayPort")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.DirectModelAliasSuffix"), "directModelAliasSuffix", settings.DirectModelAliasSuffix,
            ToolTip: Loc.T("Tooltip.Setting.DirectModelAliasSuffix")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.GatewayPolicy"), "autoLoadGatewayPolicy", AppPreferenceService.GatewaySwapPolicyLabel(settings.AutoLoadGatewayPolicy), "choice", AppPreferenceService.GatewaySwapPolicyOptions(),
            ToolTip: Loc.T("Tooltip.Setting.GatewayPolicy")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.ApiKeyAuth"), "requireApiKeyAuth", AppPreferenceService.EnableDisableLabel(settings.RequireApiKeyAuth), "choice", AppPreferenceService.EnableDisableOptions(),
            ToolTip: Loc.T("Tooltip.Setting.ApiKeyAuth")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.ApiKey"), "modelApiKey", settings.ModelApiKey, "secret", Action: Loc.T("Action.Generate"),
            ToolTip: Loc.T("Tooltip.Setting.ApiKey")),
        new(Loc.T("Settings.Group.UI"), Loc.T("Setting.UiScale"), "uiScalePercent", AppPreferenceService.NormalizeUiScalePercent(settings.UiScalePercent).ToString(CultureInfo.InvariantCulture), "slider",
            ToolTip: Loc.T("Tooltip.Setting.UiScale")),
        new(Loc.T("Settings.Group.UI"), Loc.T("Setting.FontScale"), "fontScalePercent", AppPreferenceService.NormalizeFontScalePercent(settings.FontScalePercent).ToString(CultureInfo.InvariantCulture), "slider",
            ToolTip: Loc.T("Tooltip.Setting.FontScale")),
        UiVisibility(Loc.T("Overview.ModelStatusLabel"), "showOverviewModelSection", settings.ShowOverviewModelSection),
        UiVisibility(Loc.T("Overview.LiveRuntimeLogTitle"), "showOverviewLiveRuntimeLog", settings.ShowOverviewLiveRuntimeLog),
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
        new(Loc.T("Settings.Group.Benchmarks"), Loc.T("Setting.BenchmarkPreventSystemSleep"), "benchmarkPreventSystemSleep",
            AppPreferenceService.YesNoLabel(settings.BenchmarkPreventSystemSleep), "choice", AppPreferenceService.YesNoOptions(),
            ToolTip: Loc.T("Tooltip.Setting.BenchmarkPreventSystemSleep")),
        new(Loc.T("Settings.Group.Benchmarks"), Loc.T("Setting.BenchmarkStopActiveSessions"), "benchmarkStopActiveSessions",
            AppPreferenceService.YesNoLabel(settings.BenchmarkStopActiveSessions), "choice", AppPreferenceService.YesNoOptions(),
            ToolTip: Loc.T("Tooltip.Setting.BenchmarkStopActiveSessions")),
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
