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
        var hadStrongApiKey = new[]
        {
            targetSettings.ModelApiKey,
            targetSettings.ModelApiKeyBackup,
            persistedSettings.ModelApiKey,
            persistedSettings.ModelApiKeyBackup
        }.Any(ApiSecurity.IsStrongBearerSecret);
        var apiKey = ApiSecurity.StrongBearerSecretOrNew(
            targetSettings.ModelApiKey,
            targetSettings.ModelApiKeyBackup,
            persistedSettings.ModelApiKey,
            persistedSettings.ModelApiKeyBackup);
        var normalizedTarget = targetSettings with
        {
            RequireApiKeyAuth = true,
            ModelApiKey = apiKey,
            ModelApiKeyBackup = apiKey
        };
        var normalizedPersisted = persistedSettings with
        {
            RequireApiKeyAuth = true,
            ModelApiKey = apiKey,
            ModelApiKeyBackup = apiKey
        };
        if (normalizedPersisted != persistedSettings)
            normalizedPersisted = await PersistAsync(normalizedPersisted, cancellationToken);

        return new AppSettingsEnsureApiKeyResult(
            normalizedTarget,
            normalizedPersisted,
            GeneratedApiKey: !hadStrongApiKey);
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
