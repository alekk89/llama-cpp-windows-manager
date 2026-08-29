namespace LocalLlmConsole.Services;

public sealed record AppShutdownCleanupActions(
    Action StopDownloadHistoryRefreshTimer,
    Action StopRuntimeDashboardRefreshTimer,
    Action StopGpuEnergyTrackingTimer,
    Action CancelPendingUiWork,
    Action StopRuntimeReadinessMonitor,
    Action DisposeTrayIcon,
    Func<Task> PauseActiveDownloadsAsync,
    Func<Task> DisposeBenchmarkServiceAsync,
    Action KillTrackedProcesses,
    Func<Task> CleanupActiveWslBuildsAsync,
    Func<Task> DisposeGatewayAsync,
    Func<Task> DisposeLocalServiceAsync,
    Func<Task> DrainBackgroundTasksAsync,
    Func<Task> StopRuntimeSessionsAsync,
    Action DisposeSessions,
    Action DisposeHuggingFaceService,
    Action DisposeAppUpdateService,
    Action DisposeRuntimePackageClient,
    Action DisposeMetricsClient,
    Action DisposeRuntimeProbeClient,
    Action ClearActiveRuntimeSettings,
    Action ClearActiveRuntimeSession,
    Func<Task> DisposeStateStoreAsync);

public sealed record AppShutdownCleanupFailure(string Stage, Exception Exception);

public sealed record AppShutdownCleanupResult(IReadOnlyList<AppShutdownCleanupFailure> Failures)
{
    public bool CompletedWithWarnings => Failures.Count > 0;
}

public sealed class AppShutdownCleanupApplicationService
{
    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(15);
    private readonly TimeSpan _drainTimeout;

    public AppShutdownCleanupApplicationService(TimeSpan? drainTimeout = null)
    {
        _drainTimeout = drainTimeout ?? DefaultDrainTimeout;
        if (_drainTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(drainTimeout));
    }

    public async Task<AppShutdownCleanupResult> CleanupAsync(AppShutdownCleanupActions actions)
    {
        Validate(actions);
        var failures = new List<AppShutdownCleanupFailure>();

        Try("stop download refresh timer", actions.StopDownloadHistoryRefreshTimer, failures);
        Try("stop runtime dashboard timer", actions.StopRuntimeDashboardRefreshTimer, failures);
        Try("stop GPU energy timer", actions.StopGpuEnergyTrackingTimer, failures);
        Try("cancel pending UI work", actions.CancelPendingUiWork, failures);
        Try("stop runtime readiness monitor", actions.StopRuntimeReadinessMonitor, failures);
        Try("dispose tray icon", actions.DisposeTrayIcon, failures);
        await TryAsync("pause active downloads", actions.PauseActiveDownloadsAsync, failures);
        await TryAsync("dispose benchmark service", actions.DisposeBenchmarkServiceAsync, failures);

        // Runtime processes are safety-critical and must be stopped before the
        // control/gateway hosts and state services are torn down.
        var runtimeSessionsStopped = await TryAsync("stop runtime sessions", actions.StopRuntimeSessionsAsync, failures);
        Try("kill tracked processes", actions.KillTrackedProcesses, failures);
        await TryAsync("clean up active WSL builds", actions.CleanupActiveWslBuildsAsync, failures);
        await TryAsync("dispose gateway", actions.DisposeGatewayAsync, failures);
        await TryAsync("dispose local service", actions.DisposeLocalServiceAsync, failures);
        await TryBoundedAsync("drain background tasks", actions.DrainBackgroundTasksAsync, failures);

        Try("dispose sessions", actions.DisposeSessions, failures);
        Try("dispose Hugging Face service", actions.DisposeHuggingFaceService, failures);
        Try("dispose app update service", actions.DisposeAppUpdateService, failures);
        Try("dispose runtime package client", actions.DisposeRuntimePackageClient, failures);
        Try("dispose metrics client", actions.DisposeMetricsClient, failures);
        Try("dispose runtime probe client", actions.DisposeRuntimeProbeClient, failures);
        Try("clear active runtime settings", actions.ClearActiveRuntimeSettings, failures);
        if (runtimeSessionsStopped)
            Try("clear active runtime session", actions.ClearActiveRuntimeSession, failures);
        await TryAsync("dispose state store", actions.DisposeStateStoreAsync, failures);

        return new AppShutdownCleanupResult(failures);
    }

    private static void Try(
        string stage,
        Action action,
        ICollection<AppShutdownCleanupFailure> failures)
    {
        try { action(); }
        catch (Exception ex) { failures.Add(new AppShutdownCleanupFailure(stage, ex)); }
    }

    private static async Task<bool> TryAsync(
        string stage,
        Func<Task> action,
        ICollection<AppShutdownCleanupFailure> failures)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            failures.Add(new AppShutdownCleanupFailure(stage, ex));
            return false;
        }
    }

    private async Task TryBoundedAsync(
        string stage,
        Func<Task> action,
        ICollection<AppShutdownCleanupFailure> failures)
    {
        try { await action().WaitAsync(_drainTimeout); }
        catch (Exception ex) { failures.Add(new AppShutdownCleanupFailure(stage, ex)); }
    }

    private static void Validate(AppShutdownCleanupActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.StopDownloadHistoryRefreshTimer);
        ArgumentNullException.ThrowIfNull(actions.StopRuntimeDashboardRefreshTimer);
        ArgumentNullException.ThrowIfNull(actions.StopGpuEnergyTrackingTimer);
        ArgumentNullException.ThrowIfNull(actions.CancelPendingUiWork);
        ArgumentNullException.ThrowIfNull(actions.StopRuntimeReadinessMonitor);
        ArgumentNullException.ThrowIfNull(actions.DisposeTrayIcon);
        ArgumentNullException.ThrowIfNull(actions.PauseActiveDownloadsAsync);
        ArgumentNullException.ThrowIfNull(actions.DisposeBenchmarkServiceAsync);
        ArgumentNullException.ThrowIfNull(actions.KillTrackedProcesses);
        ArgumentNullException.ThrowIfNull(actions.CleanupActiveWslBuildsAsync);
        ArgumentNullException.ThrowIfNull(actions.DisposeGatewayAsync);
        ArgumentNullException.ThrowIfNull(actions.DisposeLocalServiceAsync);
        ArgumentNullException.ThrowIfNull(actions.DrainBackgroundTasksAsync);
        ArgumentNullException.ThrowIfNull(actions.StopRuntimeSessionsAsync);
        ArgumentNullException.ThrowIfNull(actions.DisposeSessions);
        ArgumentNullException.ThrowIfNull(actions.DisposeHuggingFaceService);
        ArgumentNullException.ThrowIfNull(actions.DisposeAppUpdateService);
        ArgumentNullException.ThrowIfNull(actions.DisposeRuntimePackageClient);
        ArgumentNullException.ThrowIfNull(actions.DisposeMetricsClient);
        ArgumentNullException.ThrowIfNull(actions.DisposeRuntimeProbeClient);
        ArgumentNullException.ThrowIfNull(actions.ClearActiveRuntimeSettings);
        ArgumentNullException.ThrowIfNull(actions.ClearActiveRuntimeSession);
        ArgumentNullException.ThrowIfNull(actions.DisposeStateStoreAsync);
    }
}
