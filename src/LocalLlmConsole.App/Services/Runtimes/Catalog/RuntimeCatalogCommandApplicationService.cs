namespace LocalLlmConsole.Services;

public enum RuntimeCatalogCommandOutcome
{
    Unchanged,
    Cancelled,
    Failed,
    Applied
}

public sealed record RuntimeCatalogPreferenceCommandResult(
    RuntimeCatalogCommandOutcome Outcome,
    AppSettings Settings,
    string Preference,
    string StatusMessage);

public sealed record RuntimeCatalogCustomRepositoryCommandResult(
    RuntimeCatalogCommandOutcome Outcome,
    RuntimeCustomRepositoryResult? RepositoryResult = null);

public sealed record RuntimeCatalogPreferenceApplicationActions(
    Func<AppSettings, Task<AppSettings>> PersistSettingsAsync,
    Action ClearRuntimePackageUpdateStates,
    Func<Task> RefreshRuntimesAsync,
    Action<string> SetStatus);

public sealed record RuntimeCatalogCustomRepositoryApplicationActions(
    Func<Task> RefreshRuntimesAsync,
    Action<string> SetStatus,
    Action<string> ReportFailure);

public sealed class RuntimeCatalogCommandApplicationService
{
    private readonly RuntimeCustomRepositoryService _customRepositories;

    public RuntimeCatalogCommandApplicationService(RuntimeCustomRepositoryService customRepositories)
    {
        _customRepositories = customRepositories ?? throw new ArgumentNullException(nameof(customRepositories));
    }

    public async Task<RuntimeCatalogPreferenceCommandResult> ChangeCudaPackagePreferenceAsync(
        AppSettings settings,
        string selectedPreference,
        RuntimeCatalogPreferenceApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(actions);

        var preference = AppPreferenceService.CudaPackagePreference(selectedPreference);
        if (string.Equals(preference, AppPreferenceService.CudaPackagePreference(settings.CudaPackagePreference), StringComparison.OrdinalIgnoreCase))
            return new RuntimeCatalogPreferenceCommandResult(RuntimeCatalogCommandOutcome.Unchanged, settings, preference, "");

        var updated = settings with { CudaPackagePreference = preference };
        actions.ClearRuntimePackageUpdateStates();
        var persisted = await actions.PersistSettingsAsync(updated);
        await actions.RefreshRuntimesAsync();

        var status = $"CUDA downloads set to {AppPreferenceService.CudaPackagePreferenceLabel(preference)}.";
        actions.SetStatus(status);
        return new RuntimeCatalogPreferenceCommandResult(RuntimeCatalogCommandOutcome.Applied, persisted, preference, status);
    }

    public async Task<RuntimeCatalogCustomRepositoryCommandResult> AddCustomRepositoryAsync(
        string runtimeRoot,
        RuntimeCustomRepositoryDraft? draft,
        RuntimeCatalogCustomRepositoryApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        Validate(actions);

        if (draft is null)
            return new RuntimeCatalogCustomRepositoryCommandResult(RuntimeCatalogCommandOutcome.Cancelled);

        var result = await _customRepositories.AddAsync(runtimeRoot, draft, cancellationToken);
        if (!result.Success)
        {
            actions.ReportFailure(result.StatusMessage);
            return new RuntimeCatalogCustomRepositoryCommandResult(RuntimeCatalogCommandOutcome.Failed, result);
        }

        await actions.RefreshRuntimesAsync();
        actions.SetStatus(result.StatusMessage);
        return new RuntimeCatalogCustomRepositoryCommandResult(RuntimeCatalogCommandOutcome.Applied, result);
    }

    private static void Validate(RuntimeCatalogPreferenceApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.PersistSettingsAsync);
        ArgumentNullException.ThrowIfNull(actions.ClearRuntimePackageUpdateStates);
        ArgumentNullException.ThrowIfNull(actions.RefreshRuntimesAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }

    private static void Validate(RuntimeCatalogCustomRepositoryApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RefreshRuntimesAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
        ArgumentNullException.ThrowIfNull(actions.ReportFailure);
    }
}
