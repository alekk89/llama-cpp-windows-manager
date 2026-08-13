namespace LocalLlmConsole.Services;

public sealed class SettingsPageDefinitionService
{
    public IReadOnlyList<SettingRowDefinition> BuildRows(AppSettings settings) =>
    [
        new(Loc.T("Settings.Group.Storage"), Loc.T("Setting.Cache"), "cache",
            Loc.T("Setting.CacheValue", DisplayFormatService.BytesOrZero(CacheMaintenanceService.Size(settings.CacheRoot)), settings.CacheRoot), "readonly", Action: Loc.T("Action.Clear"),
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
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.ApiKeyAuth"), "requireApiKeyAuth", AppPreferenceService.YesNoLabel(settings.RequireApiKeyAuth), "choice", AppPreferenceService.YesNoOptions(),
            ToolTip: Loc.T("Tooltip.Setting.ApiKeyAuth")),
        new(Loc.T("Settings.Group.Network"), Loc.T("Setting.ApiKey"), "modelApiKey", settings.ModelApiKey, "secret", Action: Loc.T("Action.Generate"),
            ToolTip: Loc.T("Tooltip.Setting.ApiKey")),
        new(Loc.T("Settings.Group.Logs"), Loc.T("Setting.MaxLogFileSizeMb"), "maxLogFileSizeMb", settings.MaxLogFileSizeMb.ToString(CultureInfo.InvariantCulture),
            ToolTip: Loc.T("Tooltip.Setting.MaxLogFileSizeMb"))
    ];
}
