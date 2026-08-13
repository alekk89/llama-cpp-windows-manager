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
        var requireAuth = AppPreferenceService.YesNoValue(
            V("requireApiKeyAuth", AppPreferenceService.YesNoLabel(current.RequireApiKeyAuth)),
            current.RequireApiKeyAuth);

        string apiKey;
        string apiKeyBackup;
        var generatedApiKey = false;
        if (requireAuth)
        {
            apiKey = (V("modelApiKey", current.ModelApiKey) ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                // When re-enabling, restore the key we backed up when disabling
                if (!string.IsNullOrWhiteSpace(current.ModelApiKeyBackup))
                    apiKey = current.ModelApiKeyBackup.Trim();

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    apiKey = ApiSecurity.GenerateHexToken(32);
                    generatedApiKey = true;
                }
            }
            if (!ApiSecurity.IsStrongBearerSecret(apiKey))
                return Fail(current, "Model API key must be at least 32 non-whitespace characters.");
            apiKeyBackup = apiKey;
        }
        else
        {
            apiKeyBackup = (current.ModelApiKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiKeyBackup) && !string.IsNullOrWhiteSpace(current.ModelApiKeyBackup))
                apiKeyBackup = current.ModelApiKeyBackup.Trim();
            apiKey = string.Empty;
        }

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
            RequireApiKeyAuth = requireAuth,
            ModelApiKey = apiKey,
            ModelApiKeyBackup = apiKeyBackup,
            MaxLogFileSizeMb = Math.Clamp(maxLogFileSizeMb, 1, 4096)
        };

        return new AppSettingsUpdateResult(true, updated, "", generatedApiKey);
    }

    private static AppSettingsUpdateResult Fail(AppSettings current, string message)
        => new(false, current, message, GeneratedApiKey: false);
}
