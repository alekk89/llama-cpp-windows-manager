namespace LocalLlmConsole.Services;

public sealed record LocalControlApiResponse(int StatusCode, object Body);

public sealed record LocalControlRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Query,
    JsonObject? Body,
    IReadOnlyDictionary<string, string> Headers);

public sealed record LocalControlLoadRequest(
    string Model = "",
    string ProfileId = "",
    string ProfileName = "",
    string RuntimeId = "",
    JsonObject? Settings = null,
    bool Restart = false,
    bool UnloadOthers = false,
    bool WaitForReady = false,
    int TimeoutSeconds = 600,
    bool SaveProfile = false,
    string SaveProfileName = "");

public sealed record LocalControlProfileWriteRequest(
    string Id = "",
    string Name = "",
    bool IsDefault = false,
    JsonObject? Settings = null,
    bool Replace = false);

public sealed record LocalControlModelGroupWriteRequest(
    string Name = "",
    string RetentionMode = "Inherit",
    int IdleMinutes = ModelGroupService.DefaultIdleMinutes,
    string EvictionPriority = "Normal");

public sealed record LocalControlDownloadRequest(
    string Query = "",
    string Repo = "",
    string Path = "",
    string Revision = "",
    bool DryRun = false);

public sealed record LocalControlActions(
    Func<AppSettings> GetSettings,
    Func<AppSettings, CancellationToken, Task<AppSettings>> ApplySettingsAsync,
    Func<RuntimeRecord, ModelRecord, AppSettings, string, string, CancellationToken, Task<LoadedModelSessionSnapshot>> StartModelAsync,
    Func<ModelRecord, CancellationToken, Task> StopModelAsync,
    Func<CancellationToken, Task> RefreshAsync,
    Func<string, JsonObject?, CancellationToken, Task<object>>? ExecuteOperationAsync = null);

public sealed record LocalControlDependencies(
    string WorkspaceRoot,
    StateStore StateStore,
    LoadedModelSessionManager Sessions,
    ModelCatalogService ModelCatalog,
    ModelLaunchProfileService LaunchProfiles,
    RuntimeRegistryService RuntimeRegistry,
    HuggingFaceService HuggingFace,
    RuntimeTelemetryApplicationService RuntimeTelemetry,
    RuntimeLogTailService RuntimeLogTail,
    RuntimeEndpointProbeService RuntimeEndpointProbe,
    LogPageWorkflowService LogWorkflow,
    LocalControlActions Actions,
    ControlApiAuditLogService? AuditLog = null,
    ModelGroupService? ModelGroups = null,
    EndpointInspectionService? EndpointInspection = null);

public sealed record LocalControlDiscoveryDocument(
    int Version,
    int ProcessId,
    string BaseUrl,
    string ProtectedToken,
    string WorkspaceRoot,
    DateTimeOffset StartedAt);

public static class ControlProfileScope
{
    public static void EnsureCreateIdAvailable(NamedModelLaunchProfile? existing, ModelRecord model, string profileId)
    {
        if (existing is null) return;
        var owner = existing.ModelId.Equals(model.Id, StringComparison.OrdinalIgnoreCase)
            ? model.Name
            : existing.ModelId;
        throw new InvalidOperationException($"Launch profile id '{profileId}' already belongs to {owner}. Use PUT to update an existing profile.");
    }

    public static NamedModelLaunchProfile ResolveOwned(
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        ModelRecord model,
        string profileId)
        => profiles.FirstOrDefault(candidate =>
                candidate.ModelId.Equals(model.Id, StringComparison.OrdinalIgnoreCase)
                && candidate.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Launch profile '{profileId}' was not found for {model.Name}.");
}
