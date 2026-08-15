namespace LocalLlmConsole.Services;

public sealed record AppSettingsSaveApplicationRequest(
    AppSettings CurrentSettings,
    string ThemeMode,
    IReadOnlyDictionary<string, string> Values,
    IEnumerable<LoadedModelSessionSnapshot> Sessions);

public enum AppSettingsSaveApplicationOutcome
{
    Failed,
    Saved,
    SavedWithGeneratedApiKey
}

public sealed record AppSettingsSaveApplicationActions(
    Action<AppSettings> ApplySettings,
    Action<string> ApplyTheme,
    Action ApplyLaunchSettingsToControls,
    Func<Task<bool>> RestartGatewayAsync,
    Func<bool> IsSettingsPageActive,
    Action RefreshSettingsPage,
    Action<string> SetStatus);

public sealed class AppSettingsApplicationService
{
    private readonly AppSettingsWorkflowService _settingsWorkflow;
    private readonly WindowsStartupRegistrationService _startupRegistration;

    public AppSettingsApplicationService(
        AppSettingsWorkflowService settingsWorkflow,
        WindowsStartupRegistrationService startupRegistration)
    {
        _settingsWorkflow = settingsWorkflow ?? throw new ArgumentNullException(nameof(settingsWorkflow));
        _startupRegistration = startupRegistration ?? throw new ArgumentNullException(nameof(startupRegistration));
    }

    public Task<AppSettingsUpdateResult> SaveEditedAsync(
        AppSettingsSaveApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Values);
        ArgumentNullException.ThrowIfNull(request.Sessions);

        var runningModelPorts = request.Sessions
            .Where(session => session.IsRunning)
            .Select(session => session.LaunchSettings.Port)
            .ToHashSet();

        return _settingsWorkflow.SaveEditedAsync(new AppSettingsSaveWorkflowRequest(
            request.CurrentSettings,
            request.ThemeMode,
            request.Values,
            runningModelPorts), cancellationToken);
    }

    public async Task<AppSettingsSaveApplicationOutcome> SaveEditedAndApplyAsync(
        AppSettingsSaveApplicationRequest request,
        AppSettingsSaveApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        Validate(actions);

        var result = await SaveEditedAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Success)
        {
            actions.SetStatus(result.StatusMessage);
            return AppSettingsSaveApplicationOutcome.Failed;
        }

        var startupRegistration = _startupRegistration.Apply(result.Settings.StartWithWindows);
        actions.ApplySettings(result.Settings);
        actions.ApplyTheme(result.Settings.ThemeMode);
        actions.ApplyLaunchSettingsToControls();
        var gatewayStarted = true;
        if (GatewaySettingsChanged(request.CurrentSettings, result.Settings))
            gatewayStarted = await actions.RestartGatewayAsync();
        var status = result.GeneratedApiKey ? "Settings applied. A model API key was generated." : "Settings applied.";
        if (!gatewayStarted)
            status = $"{status} Gateway did not start. Try saving again or run the app as Administrator.";
        if (!startupRegistration.Success)
            status = $"{status} {startupRegistration.StatusMessage}";
        actions.SetStatus(status);
        if (actions.IsSettingsPageActive())
            actions.RefreshSettingsPage();

        return result.GeneratedApiKey
            ? AppSettingsSaveApplicationOutcome.SavedWithGeneratedApiKey
            : AppSettingsSaveApplicationOutcome.Saved;
    }

    internal static bool GatewaySettingsChanged(AppSettings current, AppSettings updated)
        => current.AutoLoadGatewayEnabled != updated.AutoLoadGatewayEnabled
           || current.AutoLoadGatewayPort != updated.AutoLoadGatewayPort
           || !string.Equals(current.AutoLoadGatewayPolicy, updated.AutoLoadGatewayPolicy, StringComparison.OrdinalIgnoreCase)
           || !string.Equals(current.ModelAccessMode, updated.ModelAccessMode, StringComparison.OrdinalIgnoreCase)
           || current.RequireApiKeyAuth != updated.RequireApiKeyAuth
           || !string.Equals(current.ModelApiKey, updated.ModelApiKey, StringComparison.Ordinal);

    public Task<AppSettingsEnsureApiKeyResult> EnsureModelApiKeyAsync(
        AppSettings persistedSettings,
        AppSettings targetSettings,
        CancellationToken cancellationToken = default)
        => _settingsWorkflow.EnsureModelApiKeyAsync(persistedSettings, targetSettings, cancellationToken);

    public Task<AppSettings> PersistAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => _settingsWorkflow.PersistAsync(settings, cancellationToken);

    private static void Validate(AppSettingsSaveApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.ApplySettings);
        ArgumentNullException.ThrowIfNull(actions.ApplyTheme);
        ArgumentNullException.ThrowIfNull(actions.ApplyLaunchSettingsToControls);
        ArgumentNullException.ThrowIfNull(actions.RestartGatewayAsync);
        ArgumentNullException.ThrowIfNull(actions.IsSettingsPageActive);
        ArgumentNullException.ThrowIfNull(actions.RefreshSettingsPage);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
