namespace LocalLlmConsole.Services;

public sealed record ModelRuntimeStartSessionPlan(
    bool SaveActiveRuntimeSessions,
    bool StartReadinessMonitor,
    bool StartRuntimeDashboardRefresh,
    bool UpdateLoadingStatus,
    bool RefreshOverview,
    bool RefreshOverviewModelSelector,
    TimeSpan InitialMetricsDelay,
    bool RefreshRuntimeMetrics);

public sealed record ModelRuntimeStartInitialMetricsPlan(
    bool StopRuntimeDashboardRefresh,
    bool UpdateActionButtons,
    bool StopLoadingTimer,
    bool SaveActiveRuntimeSessions,
    bool UpdateLoadingStatus,
    string StatusMessage);

public sealed class ModelRuntimeStartFollowupService
{
    private static readonly TimeSpan DefaultInitialMetricsDelay = TimeSpan.FromMilliseconds(750);

    public ModelRuntimeStartSessionPlan AfterSessionStarted()
        => new(
            SaveActiveRuntimeSessions: true,
            StartReadinessMonitor: true,
            StartRuntimeDashboardRefresh: true,
            UpdateLoadingStatus: true,
            RefreshOverview: true,
            RefreshOverviewModelSelector: true,
            InitialMetricsDelay: DefaultInitialMetricsDelay,
            RefreshRuntimeMetrics: true);

    public ModelRuntimeStartInitialMetricsPlan AfterInitialMetrics(
        string modelName,
        LlamaRuntimeState runtimeState,
        bool isOverviewPage)
    {
        var stopDashboardRefresh = !isOverviewPage;
        if (runtimeState == LlamaRuntimeState.Failed)
        {
            return new ModelRuntimeStartInitialMetricsPlan(
                stopDashboardRefresh,
                UpdateActionButtons: true,
                StopLoadingTimer: true,
                SaveActiveRuntimeSessions: true,
                UpdateLoadingStatus: false,
                StatusMessage: $"Failed to load {modelName}. Check the runtime log.");
        }

        return new ModelRuntimeStartInitialMetricsPlan(
            stopDashboardRefresh,
            UpdateActionButtons: true,
            StopLoadingTimer: false,
            SaveActiveRuntimeSessions: false,
            UpdateLoadingStatus: true,
            StatusMessage: "");
    }
}
