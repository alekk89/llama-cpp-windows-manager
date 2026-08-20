namespace LocalLlmConsole.Services;

public sealed record RuntimeDashboardSelectionRequest(
    ModelRecord? SelectedOverviewModel,
    bool SelectedOverviewModelIsActive,
    bool SelectedOverviewModelIsLoaded,
    LoadedModelSessionSnapshot? SelectedOverviewModelSession,
    LoadedModelSessionSnapshot? SelectedSession,
    AppSettings? ActiveSessionSettings,
    AppSettings? ActiveRuntimeSettings,
    AppSettings DefaultSettings,
    string ActiveModelId,
    string ActiveRuntimeId);

public sealed record RuntimeDashboardSelectionResult(
    bool SelectedOverviewModelHasNoRunningSession,
    bool SelectSelectedOverviewModel,
    LoadedModelSessionSnapshot? Session,
    AppSettings MetricsSettings,
    string RuntimeKey);

public sealed class RuntimeDashboardSelectionService
{
    public RuntimeDashboardSelectionResult Select(RuntimeDashboardSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.DefaultSettings);

        var selectSelectedOverviewModel = request.SelectedOverviewModel is not null
            && !request.SelectedOverviewModelIsActive
            && request.SelectedOverviewModelIsLoaded;
        var selectedOverviewModelHasNoRunningSession = request.SelectedOverviewModel is not null
            && request.SelectedOverviewModelSession is not { IsRunning: true };

        var session = request.SelectedOverviewModel is null
            ? request.SelectedSession
            : request.SelectedOverviewModelSession;
        var metricsSettings = session?.LaunchSettings
            ?? request.ActiveSessionSettings
            ?? request.ActiveRuntimeSettings
            ?? request.DefaultSettings;
        var runtimeKey = session is null
            ? CurrentRuntimeMetricKey(request.ActiveModelId, request.ActiveRuntimeId, metricsSettings)
            : RuntimeMetricIdentity.RuntimeKey(session);

        return new RuntimeDashboardSelectionResult(
            selectedOverviewModelHasNoRunningSession,
            selectSelectedOverviewModel,
            session,
            metricsSettings,
            runtimeKey);
    }

    public static string CurrentRuntimeMetricKey(string activeModelId, string activeRuntimeId, AppSettings metricsSettings)
    {
        ArgumentNullException.ThrowIfNull(metricsSettings);
        return $"{activeModelId}|{activeRuntimeId}|{metricsSettings.Port}";
    }
}
