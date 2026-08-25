namespace LocalLlmConsole.Services;

public sealed record RuntimeReadinessCompletionRequest(
    RuntimeReadinessStatus Status,
    string ModelName,
    AppSettings LaunchSettings,
    bool ModelIsStillLoading,
    bool IsOverviewPage,
    string FailureReason = "");

public sealed record RuntimeReadinessCompletionPlan(
    bool StopLoadingStatus,
    bool ShowLoadedDuration,
    bool SelectLoadedOverviewModel,
    bool SaveActiveRuntimeSessions,
    bool UpdateActionButtons,
    bool RefreshRuntimeMetrics,
    string StatusMessage,
    bool StopUnsafeRuntime = false);

public sealed class RuntimeReadinessCompletionService
{
    public RuntimeReadinessCompletionPlan Build(RuntimeReadinessCompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Status switch
        {
            RuntimeReadinessStatus.NoLongerLoading => new RuntimeReadinessCompletionPlan(
                StopLoadingStatus: request.ModelIsStillLoading,
                ShowLoadedDuration: false,
                SelectLoadedOverviewModel: false,
                SaveActiveRuntimeSessions: false,
                UpdateActionButtons: false,
                RefreshRuntimeMetrics: false,
                StatusMessage: ""),
            RuntimeReadinessStatus.Loaded => new RuntimeReadinessCompletionPlan(
                StopLoadingStatus: request.ModelIsStillLoading,
                ShowLoadedDuration: request.ModelIsStillLoading,
                SelectLoadedOverviewModel: true,
                SaveActiveRuntimeSessions: true,
                UpdateActionButtons: true,
                RefreshRuntimeMetrics: request.IsOverviewPage,
                StatusMessage: $"Loaded {request.ModelName} at {RuntimeEndpointService.EndpointDisplay(request.LaunchSettings)}."),
            RuntimeReadinessStatus.AuthenticationFailed => new RuntimeReadinessCompletionPlan(
                StopLoadingStatus: request.ModelIsStillLoading,
                ShowLoadedDuration: false,
                SelectLoadedOverviewModel: false,
                SaveActiveRuntimeSessions: true,
                UpdateActionButtons: true,
                RefreshRuntimeMetrics: request.IsOverviewPage,
                StatusMessage: $"Stopped {request.ModelName}: {request.FailureReason}",
                StopUnsafeRuntime: true),
            _ => new RuntimeReadinessCompletionPlan(
                StopLoadingStatus: false,
                ShowLoadedDuration: false,
                SelectLoadedOverviewModel: false,
                SaveActiveRuntimeSessions: false,
                UpdateActionButtons: false,
                RefreshRuntimeMetrics: false,
                StatusMessage: "")
        };
    }
}
