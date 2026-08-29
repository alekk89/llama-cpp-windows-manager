using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace LocalLlmConsole;

public partial class MainWindow
{
    private Task<object> ExecuteControlOperationAsync(
        string operation,
        JsonObject? body,
        CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(() => ExecuteControlOperationOnUiAsync(
            operation,
            body ?? new JsonObject(),
            cancellationToken)).Task.Unwrap();

    private async Task<object> ExecuteControlOperationOnUiAsync(
        string operation,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dryRun = ControlBool(body, "dryRun");
        var confirm = ControlBool(body, "confirm");
        return operation.ToLowerInvariant() switch
        {
            "app.refresh" => await ControlRefreshAsync(cancellationToken),
            "app.shutdown" => ControlShutdown(dryRun, confirm),
            "ui.navigate" => ControlNavigate(ControlRequired(body, "page"), dryRun),
            "gateway.restart" => await ControlGatewayAsync(restart: true, dryRun),
            "gateway.stop" => await ControlGatewayAsync(restart: false, dryRun),
            _ when ControlRuntimeOperationApplicationService.CanHandle(operation)
                => await (_controlRuntimeOperations ?? throw new InvalidOperationException("Control runtime operations are not ready."))
                    .ExecuteAsync(operation, body, dryRun, cancellationToken),
            _ when ControlNonRuntimeOperationApplicationService.CanHandle(operation)
                => await ControlNonRuntimeOperations().ExecuteAsync(operation, body, dryRun, confirm, cancellationToken),
            _ => throw new KeyNotFoundException($"Control operation '{operation}' was not found.")
        };
    }

    private ControlNonRuntimeOperationApplicationService ControlNonRuntimeOperations()
        => new(
            new ControlNonRuntimeOperationDependencies(
                AppServices.CacheClearWorkflow,
                AppServices.HuggingFace,
                AppServices.LogPageApplication,
                AppServices.LogPageWorkflow,
                AppServices.LifetimeMetricsApplication,
                AppServices.DownloadHistoryApplication,
                _coreServices.Environment.WindowsToolSetupWorkflow,
                _coreServices.Environment.WindowsToolSetupApplication,
                _infrastructureServices.WslEnvironment,
                _coreServices.Environment.WslPageWorkflow,
                _coreServices.Environment.WslToolSetupWorkflow,
                _coreServices.Environment.WslToolSetupApplication,
                _coreServices.App.AppUpdateWorkflow),
            new ControlNonRuntimeOperationActions(
                () => _settings,
                _sessions.Snapshots,
                ApplyControlSettingsOnUiAsync,
                session => ResetLifetimeCounters(session),
                ControlRunBusyAsync,
                SetStatus,
                update => RunBackground(() => InstallAppUpdateAsync(update, confirm: false), "Controlled app update failed")));

    private async Task<object> ControlRefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RefreshAllAsync();
        return new { refreshed = true };
    }

    private object ControlShutdown(bool dryRun, bool confirm)
    {
        if (dryRun) return new { wouldClose = true, runningSessions = _sessions.Snapshots().Count(session => session.IsRunning) };
        if (!confirm) throw new InvalidOperationException("Application shutdown requires confirm=true.");
        Interlocked.Exchange(ref _controlShutdownConfirmed, 1);
        Dispatcher.BeginInvoke(Close, DispatcherPriority.ApplicationIdle);
        return new { scheduled = true };
    }

    private object ControlNavigate(string page, bool dryRun)
    {
        var normalized = page.Trim().ToLowerInvariant();
        var allowed = new[] { "overview", "models", "runtimes", "windows", "wsl", "settings", "lifetime", "logs", "updates", "help" };
        if (!allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Page '{page}' is unavailable.");
        if (dryRun) return new { page = normalized, wouldNavigate = true };
        var navigation = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
        {
            ["overview"] = ShowOverview,
            ["models"] = ShowModels,
            ["runtimes"] = ShowRuntimes,
            ["windows"] = ShowWindows,
            ["wsl"] = ShowWslLinux,
            ["settings"] = ShowSettings,
            ["lifetime"] = ShowLifetime,
            ["logs"] = ShowLogs,
            ["updates"] = ShowUpdates,
            ["help"] = ShowHelp
        };
        navigation[normalized]();
        return new { page = normalized, navigated = true };
    }

    private async Task<object> ControlGatewayAsync(bool restart, bool dryRun)
    {
        if (dryRun) return new { action = restart ? "restart" : "stop", enabled = _settings.AutoLoadGatewayEnabled, port = _settings.AutoLoadGatewayPort };
        var result = restart ? await RestartModelGatewayAsync() : await StopGatewayForControlAsync();
        return new { action = restart ? "restart" : "stop", applied = result };
    }

    private async Task<bool> StopGatewayForControlAsync()
    {
        await StopModelGatewayAsync();
        return true;
    }

    private static async Task ControlRunBusyAsync(string _, Func<Task> action)
        => await action();

    private static string ControlRequired(JsonObject body, string name)
    {
        var value = ControlString(body, name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Operation parameter '{name}' is required.")
            : value;
    }

    private static string ControlString(JsonObject body, string name)
        => body[name]?.ToString()?.Trim() ?? "";

    private static bool ControlBool(JsonObject body, string name)
        => body[name] is JsonValue value && value.TryGetValue<bool>(out var result) && result;
}
