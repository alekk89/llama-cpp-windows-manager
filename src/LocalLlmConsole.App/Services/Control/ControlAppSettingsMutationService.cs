namespace LocalLlmConsole.Services;

public sealed class ControlAppSettingsMutationService
{
    public AppSettings RotateModelApiKey(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var key = ApiSecurity.GenerateHexToken(32);
        return current with
        {
            RequireApiKeyAuth = true,
            ModelApiKey = key,
            ModelApiKeyBackup = key
        };
    }

    public AppSettings Patch(
        AppSettings current,
        JsonObject? patch,
        IReadOnlyList<LoadedModelSessionSnapshot> sessions)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(sessions);

        var updated = ControlJsonPatch.Apply(
            current,
            patch,
            nameof(AppSettings.WorkspaceRoot),
            nameof(AppSettings.ModelApiKey),
            nameof(AppSettings.ModelApiKeyBackup));
        if (!updated.RequireApiKeyAuth)
            throw new InvalidOperationException("API-key authentication is required for all model-serving endpoints.");

        updated = Normalize(current, updated);
        Validate(updated, sessions);
        return updated;
    }

    private static void Validate(AppSettings settings, IReadOnlyList<LoadedModelSessionSnapshot> sessions)
    {
        if (settings.AutoLoadGatewayPort is < 1 or > 65535)
            throw new InvalidOperationException("Gateway port must be between 1 and 65535.");
        if (settings.Port is < 1 or > 65535)
            throw new InvalidOperationException("Default model port must be between 1 and 65535.");
        if (!settings.RequireApiKeyAuth)
            throw new InvalidOperationException("API-key authentication is required for all model-serving endpoints.");
        if (!ApiSecurity.IsStrongBearerSecret(settings.ModelApiKey))
            throw new InvalidOperationException("The existing model API key is missing or invalid. Rotate it in the app before changing settings.");
        if (string.IsNullOrWhiteSpace(settings.ModelsRoot)
            || string.IsNullOrWhiteSpace(settings.RuntimeRoot)
            || string.IsNullOrWhiteSpace(settings.CacheRoot))
            throw new InvalidOperationException("Models, runtime, and cache roots are required.");
        if (settings.AutoLoadGatewayEnabled && sessions.Any(session =>
                session.IsRunning && session.LaunchSettings.Port == settings.AutoLoadGatewayPort))
            throw new InvalidOperationException($"Gateway port {settings.AutoLoadGatewayPort} is already used by a running model.");
        if (settings.MaxLogFileSizeMb is < 1 or > 4096)
            throw new InvalidOperationException("Maximum log file size must be between 1 and 4096 MiB.");
        if (settings.AutoUnloadIdleMinutes is < 0 or > 10080)
            throw new InvalidOperationException("Auto-unload idle minutes must be between 0 and 10080.");
    }

    private static AppSettings Normalize(AppSettings current, AppSettings updated)
    {
        var accessMode = AppPreferenceService.ModelAccessMode(updated.ModelAccessMode);
        var normalized = updated with
        {
            WorkspaceRoot = current.WorkspaceRoot,
            ThemeMode = AppPreferenceService.ThemeMode(updated.ThemeMode),
            MinimizeBehavior = AppPreferenceService.MinimizeBehavior(updated.MinimizeBehavior),
            ModelAccessMode = accessMode,
            Host = AppPreferenceService.RuntimeHostForAccessMode(accessMode),
            AutoLoadGatewayPolicy = AppPreferenceService.GatewaySwapPolicy(updated.AutoLoadGatewayPolicy),
            CudaPackagePreference = AppPreferenceService.CudaPackagePreference(updated.CudaPackagePreference),
            UiCulture = string.IsNullOrWhiteSpace(updated.UiCulture) ? current.UiCulture : updated.UiCulture.Trim(),
            RequireApiKeyAuth = true
        };
        var apiKey = ApiSecurity.StrongBearerSecretOrNew(
            normalized.ModelApiKey,
            normalized.ModelApiKeyBackup,
            current.ModelApiKey,
            current.ModelApiKeyBackup);
        return normalized with { ModelApiKey = apiKey, ModelApiKeyBackup = apiKey };
    }
}
