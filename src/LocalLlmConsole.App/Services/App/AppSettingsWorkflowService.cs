namespace LocalLlmConsole.Services;

public sealed record AppSettingsSaveWorkflowRequest(
    AppSettings CurrentSettings,
    string ThemeMode,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlySet<int> RunningModelPorts);

public sealed record AppSettingsEnsureApiKeyResult(
    AppSettings Settings,
    AppSettings PersistedSettings,
    bool GeneratedApiKey);

public sealed class AppSettingsWorkflowService
{
    private readonly StateStore _stateStore;
    private readonly AppSettingsUpdateService _updates;
    private readonly string _workspaceRoot;

    public AppSettingsWorkflowService(
        StateStore stateStore,
        AppSettingsUpdateService updates,
        string workspaceRoot)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot))
            : workspaceRoot;
    }

    public async Task<AppSettingsUpdateResult> SaveEditedAsync(
        AppSettingsSaveWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = _updates.Build(new AppSettingsUpdateRequest(
            request.CurrentSettings,
            _workspaceRoot,
            request.ThemeMode,
            request.Values,
            request.RunningModelPorts));
        if (!result.Success) return result;

        var persisted = await PersistAsync(result.Settings, cancellationToken);
        return result with { Settings = persisted };
    }

    public async Task<AppSettingsEnsureApiKeyResult> EnsureModelApiKeyAsync(
        AppSettings persistedSettings,
        AppSettings targetSettings,
        CancellationToken cancellationToken = default)
    {
        // When auth is disabled, clear any previously stored key so it cannot
        // be reused if authentication is re-enabled later.
        if (!targetSettings.RequireApiKeyAuth)
        {
            if (!string.IsNullOrWhiteSpace(targetSettings.ModelApiKey))
            {
                var cleared = targetSettings with { ModelApiKey = "" };
                var persistedCleared = await PersistAsync(persistedSettings with { ModelApiKey = "" }, cancellationToken);
                return new AppSettingsEnsureApiKeyResult(cleared, persistedCleared, GeneratedApiKey: false);
            }
            return new AppSettingsEnsureApiKeyResult(targetSettings, persistedSettings, GeneratedApiKey: false);
        }

        var apiKey = RuntimeEndpointService.ModelApiKeyForClient(targetSettings);

        if (!string.IsNullOrWhiteSpace(apiKey) && ApiSecurity.IsStrongBearerSecret(apiKey))
        {
            var trimmedTarget = targetSettings with { ModelApiKey = apiKey };
            return new AppSettingsEnsureApiKeyResult(
                trimmedTarget,
                persistedSettings with { ModelApiKey = RuntimeEndpointService.ModelApiKeyForClient(persistedSettings) },
                GeneratedApiKey: false);
        }

        apiKey = ApiSecurity.GenerateHexToken(32);
        var updatedPersisted = await PersistAsync(persistedSettings with { ModelApiKey = apiKey }, cancellationToken);
        return new AppSettingsEnsureApiKeyResult(
            targetSettings with { ModelApiKey = apiKey },
            updatedPersisted,
            GeneratedApiKey: true);
    }

    public async Task<AppSettings> PersistAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var persisted = settings with { WorkspaceRoot = _workspaceRoot };
        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(persisted.ModelsRoot);
        Directory.CreateDirectory(persisted.RuntimeRoot);
        Directory.CreateDirectory(persisted.CacheRoot);
        await _stateStore.SaveAppSettingsAsync(persisted);
        return persisted;
    }
}
