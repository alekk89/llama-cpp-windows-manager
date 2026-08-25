namespace LocalLlmConsole.Services;

public sealed record AppShutdownCleanupActions(
    Action StopDownloadHistoryRefreshTimer,
    Action StopRuntimeDashboardRefreshTimer,
    Action StopGpuEnergyTrackingTimer,
    Action CancelLaunchSettingsRefresh,
    Action StopRuntimeReadinessMonitor,
    Action DisposeTrayIcon,
    Func<Task> PauseActiveDownloadsAsync,
    Action KillTrackedProcesses,
    Func<Task> CleanupActiveWslBuildsAsync,
    Func<Task> DisposeGatewayAsync,
    Func<Task> StopRuntimeSessionsAsync,
    Action DisposeSessions,
    Action DisposeRuntimePackageClient,
    Action DisposeMetricsClient,
    Action DisposeRuntimeProbeClient,
    Action ClearActiveRuntimeSettings,
    Action ClearActiveRuntimeSession,
    Func<Task> DisposeLocalServiceAsync,
    Func<Task> DisposeStateStoreAsync);

public sealed class AppShutdownCleanupApplicationService
{
    public async Task CleanupAsync(AppShutdownCleanupActions actions)
    {
        Validate(actions);

        actions.StopDownloadHistoryRefreshTimer();
        actions.StopRuntimeDashboardRefreshTimer();
        actions.StopGpuEnergyTrackingTimer();
        actions.CancelLaunchSettingsRefresh();
        actions.StopRuntimeReadinessMonitor();
        actions.DisposeTrayIcon();
        await actions.PauseActiveDownloadsAsync();
        actions.KillTrackedProcesses();
        await actions.CleanupActiveWslBuildsAsync();
        await actions.DisposeGatewayAsync();
        await actions.StopRuntimeSessionsAsync();
        actions.DisposeSessions();
        actions.DisposeRuntimePackageClient();
        actions.DisposeMetricsClient();
        actions.DisposeRuntimeProbeClient();
        actions.ClearActiveRuntimeSettings();
        actions.ClearActiveRuntimeSession();
        await actions.DisposeLocalServiceAsync();
        await actions.DisposeStateStoreAsync();
    }

    private static void Validate(AppShutdownCleanupActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.StopDownloadHistoryRefreshTimer);
        ArgumentNullException.ThrowIfNull(actions.StopRuntimeDashboardRefreshTimer);
        ArgumentNullException.ThrowIfNull(actions.StopGpuEnergyTrackingTimer);
        ArgumentNullException.ThrowIfNull(actions.CancelLaunchSettingsRefresh);
        ArgumentNullException.ThrowIfNull(actions.StopRuntimeReadinessMonitor);
        ArgumentNullException.ThrowIfNull(actions.DisposeTrayIcon);
        ArgumentNullException.ThrowIfNull(actions.PauseActiveDownloadsAsync);
        ArgumentNullException.ThrowIfNull(actions.KillTrackedProcesses);
        ArgumentNullException.ThrowIfNull(actions.CleanupActiveWslBuildsAsync);
        ArgumentNullException.ThrowIfNull(actions.DisposeGatewayAsync);
        ArgumentNullException.ThrowIfNull(actions.StopRuntimeSessionsAsync);
        ArgumentNullException.ThrowIfNull(actions.DisposeSessions);
        ArgumentNullException.ThrowIfNull(actions.DisposeRuntimePackageClient);
        ArgumentNullException.ThrowIfNull(actions.DisposeMetricsClient);
        ArgumentNullException.ThrowIfNull(actions.DisposeRuntimeProbeClient);
        ArgumentNullException.ThrowIfNull(actions.ClearActiveRuntimeSettings);
        ArgumentNullException.ThrowIfNull(actions.ClearActiveRuntimeSession);
        ArgumentNullException.ThrowIfNull(actions.DisposeLocalServiceAsync);
        ArgumentNullException.ThrowIfNull(actions.DisposeStateStoreAsync);
    }
}
