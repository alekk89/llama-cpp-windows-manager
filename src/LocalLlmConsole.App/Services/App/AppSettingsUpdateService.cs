namespace LocalLlmConsole.Services;

public sealed record AppSettingsUpdateRequest(
    AppSettings CurrentSettings,
    string WorkspaceRoot,
    string ThemeMode,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlySet<int> RunningModelPorts);

public sealed record AppSettingsUpdateResult(
    bool Success,
    AppSettings Settings,
    string StatusMessage,
    bool GeneratedApiKey);

public sealed class AppSettingsUpdateService
{
    public AppSettingsUpdateResult Build(AppSettingsUpdateRequest request)
    {
        var current = request.CurrentSettings;
        var values = request.Values;
        string V(string key, string fallback) => values.TryGetValue(key, out var value) ? value : fallback;

        var accessMode = AppPreferenceService.ModelAccessMode(V("modelAccessMode", current.ModelAccessMode));
        var requireApiKeyAuth = AppPreferenceService.EnableDisableValue(
            V("requireApiKeyAuth", AppPreferenceService.EnableDisableLabel(current.RequireApiKeyAuth)),
            current.RequireApiKeyAuth);
        if (!requireApiKeyAuth && !ModelAccessPolicy.AllowsUnauthenticatedAccess(accessMode))
            return Fail(current, "API-key authentication can be disabled only when LAN exposure is Local only.");

        var requestedApiKey = (V("modelApiKey", current.ModelApiKey) ?? "").Trim();
        if (requireApiKeyAuth
            && values.ContainsKey("modelApiKey")
            && requestedApiKey.Length > 0
            && !ApiSecurity.IsStrongBearerSecret(requestedApiKey))
            return Fail(current, "Model API key must be at least 32 non-whitespace characters.");

        var hadStrongApiKey = new[] { requestedApiKey, current.ModelApiKey, current.ModelApiKeyBackup }
            .Any(ApiSecurity.IsStrongBearerSecret);
        var preservedApiKey = ApiSecurity.StrongBearerSecretOrNew(
            requestedApiKey,
            current.ModelApiKey,
            current.ModelApiKeyBackup);
        var generatedApiKey = requireApiKeyAuth && !hadStrongApiKey;

        if (!AppPreferenceService.TryIntValue(V("autoLoadGatewayPort", current.AutoLoadGatewayPort.ToString(CultureInfo.InvariantCulture)), out var autoLoadGatewayPort))
            return Fail(current, "Gateway port must be a whole number.");

        if (autoLoadGatewayPort is < 1 or > 65535)
            return Fail(current, "Gateway port must be between 1 and 65535.");

        var autoLoadGatewayEnabled = AppPreferenceService.YesNoValue(
            V("autoLoadGatewayEnabled", AppPreferenceService.YesNoLabel(current.AutoLoadGatewayEnabled)),
            current.AutoLoadGatewayEnabled);
        if (autoLoadGatewayEnabled && request.RunningModelPorts.Contains(autoLoadGatewayPort))
            return Fail(current, $"Gateway port {autoLoadGatewayPort} is already used by a loaded model.");

        if (!AppPreferenceService.TryIntValue(V("autoUnloadIdleMinutes", current.AutoUnloadIdleMinutes.ToString(CultureInfo.InvariantCulture)), out var autoUnloadIdleMinutes))
            return Fail(current, "Auto unload idle min must be a whole number.");

        if (!AppPreferenceService.TryIntValue(V("maxLogFileSizeMb", current.MaxLogFileSizeMb.ToString(CultureInfo.InvariantCulture)), out var maxLogFileSizeMb))
            return Fail(current, "Max log file MB must be a whole number.");

        if (!TryRate(V("electricityDayRatePerKwh", current.ElectricityDayRatePerKwh.ToString(CultureInfo.InvariantCulture)), out var electricityDayRate)
            || !TryRate(V("electricityNightRatePerKwh", current.ElectricityNightRatePerKwh.ToString(CultureInfo.InvariantCulture)), out var electricityNightRate))
            return Fail(current, "Electricity rates must be numbers in currency units per kWh.");
        if (!ElectricityTariffPolicy.TryCreate(
                V("electricityCurrencyCode", current.ElectricityCurrencyCode),
                electricityDayRate,
                electricityNightRate,
                V("electricityNightStartLocal", current.ElectricityNightStartLocal),
                V("electricityNightEndLocal", current.ElectricityNightEndLocal),
                out var electricityTariff,
                out var electricityTariffError))
            return Fail(current, electricityTariffError);

        var updated = current with
        {
            WorkspaceRoot = request.WorkspaceRoot,
            ThemeMode = AppPreferenceService.ThemeMode(request.ThemeMode),
            MinimizeBehavior = AppPreferenceService.MinimizeBehavior(V("minimizeBehavior", current.MinimizeBehavior)),
            StartWithWindows = AppPreferenceService.YesNoValue(
                V("startWithWindows", AppPreferenceService.YesNoLabel(current.StartWithWindows)),
                current.StartWithWindows),
            AutoUnloadIdleMinutes = Math.Clamp(autoUnloadIdleMinutes, 0, 10080),
            DeleteRuntimeSourceAfterSuccessfulBuild = AppPreferenceService.YesNoValue(
                V("deleteRuntimeSourceAfterSuccessfulBuild", AppPreferenceService.YesNoLabel(current.DeleteRuntimeSourceAfterSuccessfulBuild)),
                current.DeleteRuntimeSourceAfterSuccessfulBuild),
            ModelAccessMode = accessMode,
            AutoLoadGatewayEnabled = autoLoadGatewayEnabled,
            AutoLoadGatewayPort = autoLoadGatewayPort,
            AutoLoadGatewayPolicy = AppPreferenceService.GatewaySwapPolicy(
                V("autoLoadGatewayPolicy", AppPreferenceService.GatewaySwapPolicyLabel(current.AutoLoadGatewayPolicy))),
            Host = AppPreferenceService.RuntimeHostForAccessMode(accessMode),
            RequireApiKeyAuth = requireApiKeyAuth,
            ModelApiKey = requireApiKeyAuth ? preservedApiKey : "",
            ModelApiKeyBackup = preservedApiKey,
            UiScalePercent = AppPreferenceService.ParseUiScalePercent(
                V("uiScalePercent", current.UiScalePercent.ToString(CultureInfo.InvariantCulture)),
                current.UiScalePercent),
            FontScalePercent = AppPreferenceService.ParseFontScalePercent(
                V("fontScalePercent", current.FontScalePercent.ToString(CultureInfo.InvariantCulture)),
                current.FontScalePercent),
            ShowOverviewModelStatus = Visibility("showOverviewModelStatus", current.ShowOverviewModelStatus),
            ShowOverviewHardware = Visibility("showOverviewHardware", current.ShowOverviewHardware),
            ShowOverviewSlots = Visibility("showOverviewSlots", current.ShowOverviewSlots),
            ShowOverviewTokens = Visibility("showOverviewTokens", current.ShowOverviewTokens),
            ShowOverviewMtpTokens = Visibility("showOverviewMtpTokens", current.ShowOverviewMtpTokens),
            ShowOverviewKvCache = Visibility("showOverviewKvCache", current.ShowOverviewKvCache),
            ShowOverviewModelSection = Visibility("showOverviewModelSection", current.ShowOverviewModelSection),
            ShowOverviewLiveRuntimeLog = Visibility("showOverviewLiveRuntimeLog", current.ShowOverviewLiveRuntimeLog),
            RuntimeLogOrder = AppPreferenceService.RuntimeLogOrder(V("runtimeLogOrder", current.RuntimeLogOrder)),
            ShowOverviewAllMetrics = Visibility("showOverviewAllMetrics", current.ShowOverviewAllMetrics),
            ShowModelsHuggingFace = Visibility("showModelsHuggingFace", current.ShowModelsHuggingFace),
            MaxLogFileSizeMb = Math.Clamp(maxLogFileSizeMb, 1, 4096),
            ElectricityCurrencyCode = electricityTariff.CurrencyCode,
            ElectricityDayRatePerKwh = electricityTariff.DayRatePerKwh,
            ElectricityNightRatePerKwh = electricityTariff.NightRatePerKwh,
            ElectricityNightStartLocal = ElectricityTariffPolicy.TimeText(electricityTariff.NightStartLocal),
            ElectricityNightEndLocal = ElectricityTariffPolicy.TimeText(electricityTariff.NightEndLocal),
            TrackGpuEnergyWhileIdle = AppPreferenceService.YesNoValue(
                V("trackGpuEnergyWhileIdle", AppPreferenceService.YesNoLabel(current.TrackGpuEnergyWhileIdle)),
                current.TrackGpuEnergyWhileIdle),
            BenchmarkPreventSystemSleep = AppPreferenceService.YesNoValue(
                V("benchmarkPreventSystemSleep", AppPreferenceService.YesNoLabel(current.BenchmarkPreventSystemSleep)),
                current.BenchmarkPreventSystemSleep),
            BenchmarkStopActiveSessions = AppPreferenceService.YesNoValue(
                V("benchmarkStopActiveSessions", AppPreferenceService.YesNoLabel(current.BenchmarkStopActiveSessions)),
                current.BenchmarkStopActiveSessions)
        };

        var previousVisibility = OverviewDashboardLayoutPolicy.LegacyVisibility(current);
        var requestedVisibility = OverviewDashboardLayoutPolicy.LegacyVisibility(updated);
        var dashboardLayout = OverviewDashboardLayoutPolicy.ApplyLegacyVisibilityChanges(
            current.OverviewDashboardLayout,
            previousVisibility,
            requestedVisibility);
        updated = OverviewDashboardLayoutPolicy.WithLayout(updated, dashboardLayout);

        return new AppSettingsUpdateResult(true, updated, "", generatedApiKey);

        bool Visibility(string key, bool fallback)
            => AppPreferenceService.ShowHideValue(V(key, AppPreferenceService.ShowHideLabel(fallback)), fallback);
    }

    private static AppSettingsUpdateResult Fail(AppSettings current, string message)
        => new(false, current, message, GeneratedApiKey: false);

    private static bool TryRate(string? value, out double result)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result)
           || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

}
