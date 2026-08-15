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
            "cache.plan" => await ControlCacheAsync(clear: false, dryRun: true, cancellationToken),
            "cache.clear" => await ControlCacheAsync(clear: true, dryRun, cancellationToken),
            "logs.delete" => await ControlLogsAsync(ControlRequired(body, "file"), all: false, dryRun, cancellationToken),
            "logs.delete-all" => await ControlLogsAsync("", all: true, dryRun, cancellationToken),
            "lifetime.list" => await ControlLifetimeAsync("list", "", dryRun, cancellationToken),
            "lifetime.delete" => await ControlLifetimeAsync("delete", ControlRequired(body, "model"), dryRun, cancellationToken),
            "lifetime.delete-all" => await ControlLifetimeAsync("delete-all", "", dryRun, cancellationToken),
            "downloads.delete" => await ControlDownloadDeleteAsync(ControlRequired(body, "job"), dryRun, cancellationToken),
            _ when ControlRuntimeOperationApplicationService.CanHandle(operation)
                => await (_controlRuntimeOperations ?? throw new InvalidOperationException("Control runtime operations are not ready."))
                    .ExecuteAsync(operation, body, dryRun, cancellationToken),
            "windows.status" => await _coreServices.Environment.WindowsToolSetupWorkflow.RefreshAsync(cancellationToken),
            "windows.setup" => ControlWindowsSetup(ControlRequired(body, "action"), dryRun, confirm),
            "wsl.status" => await _coreServices.Environment.WslPageWorkflow.RefreshAsync(_settings, cancellationToken),
            "wsl.select" => await ControlWslSelectAsync(ControlRequired(body, "distro"), dryRun, cancellationToken),
            "wsl.setup" => ControlWslSetup(ControlRequired(body, "action"), ControlString(body, "distro"), dryRun, confirm),
            "updates.check" => await _coreServices.App.AppUpdateWorkflow.CheckLatestAsync(manual: true, cancellationToken),
            "updates.install" => await ControlUpdateInstallAsync(dryRun, confirm, cancellationToken),
            _ => throw new KeyNotFoundException($"Control operation '{operation}' was not found.")
        };
    }

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

    private async Task<object> ControlCacheAsync(bool clear, bool dryRun, CancellationToken cancellationToken)
    {
        var workflow = AppServices.CacheClearWorkflow ?? throw new InvalidOperationException("Cache service is not ready.");
        var plan = await workflow.PlanAsync(_settings, AppServices.HuggingFace?.ActiveDownloadCount > 0, cancellationToken);
        var ready = string.Equals(plan.Status.ToString(), "Ready", StringComparison.Ordinal);
        if (!clear || dryRun || !ready)
            return new { plan, wouldClear = clear && ready };
        await workflow.ClearAsync(_settings, cancellationToken);
        SetStatus($"Cleared cache ({plan.DisplaySize}) through the control API.");
        return new { cleared = true, plan.SizeBytes };
    }

    private async Task<object> ControlLogsAsync(string file, bool all, bool dryRun, CancellationToken cancellationToken)
    {
        var application = AppServices.LogPageApplication ?? throw new InvalidOperationException("Log service is not ready.");
        LogDeleteCommandPlan plan;
        if (all)
        {
            plan = await application.BuildAllDeletionCommandAsync(_sessions.Snapshots(), cancellationToken);
        }
        else
        {
            var name = Path.GetFileName(file);
            if (!name.Equals(file, StringComparison.Ordinal))
                throw new InvalidOperationException("Log identifiers must be file names, not paths.");
            plan = application.BuildSingleDeletionCommand(Path.Combine(AppServices.LogPageWorkflow.LogRoot, name), _sessions.Snapshots());
        }
        if (dryRun || !plan.CanDelete) return new { plan.CanDelete, plan.StatusMessage, plan.ConfirmationMessage };
        var outcome = await application.DeleteAsync(plan, new LogPageDeleteApplicationActions(
            _ => true,
            ControlRunBusyAsync,
            () => { },
            () => Task.CompletedTask,
            SetStatus), cancellationToken);
        return new { outcome = outcome.ToString() };
    }

    private async Task<object> ControlLifetimeAsync(string action, string modelId, bool dryRun, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = AppServices.LifetimeMetricsApplication ?? throw new InvalidOperationException("Lifetime metrics are not ready.");
        var records = await service.ListAsync();
        if (action == "list") return records;
        if (action == "delete" && !records.Any(record => record.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase)))
            throw new KeyNotFoundException($"Lifetime metrics for model '{modelId}' were not found.");
        if (dryRun) return new { action, modelId, affected = action == "delete-all" ? records.Count : 1 };
        if (action == "delete")
        {
            await service.DeleteModelUsageAsync(modelId);
            ResetLifetimeCounters(_sessions.Snapshots().FirstOrDefault(session => session.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase)));
        }
        else
        {
            await service.DeleteAllUsageAsync();
            ResetLifetimeCounters();
        }
        return new { deleted = action == "delete-all" ? "all" : modelId };
    }

    private async Task<object> ControlDownloadDeleteAsync(string jobId, bool dryRun, CancellationToken cancellationToken)
    {
        var service = AppServices.DownloadHistoryApplication ?? throw new InvalidOperationException("Download history is not ready.");
        var job = (await service.ListJobsAsync()).FirstOrDefault(candidate => candidate.Id.Equals(jobId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Download job '{jobId}' was not found.");
        if (dryRun) return new { job.Id, job.Kind, status = job.Status.ToString(), wouldDelete = true };
        var outcome = await service.DeleteAsync(job, _settings, new DownloadHistoryDeleteApplicationActions(
            _ => true,
            new DownloadHistoryCommandApplicationActions(
                ControlRunBusyAsync,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                SetStatus,
                _ => { })), cancellationToken);
        return new { outcome = outcome.ToString(), job = job.Id };
    }

    private object ControlWindowsSetup(string actionName, bool dryRun, bool confirm)
    {
        if (!Enum.TryParse<WindowsToolSetupAction>(actionName, true, out var action))
            throw new InvalidOperationException($"Unknown Windows setup action '{actionName}'. Use CPU, CUDA, Vulkan, or SYCL.");
        var plan = _coreServices.Environment.WindowsToolSetupWorkflow.Plan(action);
        if (dryRun) return new { plan.Action, plan.Title, plan.ConfirmationMessage, plan.Elevated };
        var outcome = _coreServices.Environment.WindowsToolSetupApplication.Run(action, new WindowsToolSetupApplicationActions(_ => confirm, SetStatus));
        return new { outcome = outcome.ToString(), plan.StartedStatus };
    }

    private async Task<object> ControlWslSelectAsync(string distro, bool dryRun, CancellationToken cancellationToken)
    {
        var report = await _infrastructureServices.WslEnvironment.DetectAsync(cancellationToken);
        if (!report.Distros.Any(candidate => candidate.Name.Equals(distro, StringComparison.OrdinalIgnoreCase)))
            throw new KeyNotFoundException($"WSL distro '{distro}' was not found.");
        if (dryRun) return new { distro, wouldSelect = true };
        _settings = await ApplyControlSettingsOnUiAsync(_settings with { WslDistro = distro.Trim() }, cancellationToken);
        return new { selected = _settings.WslDistro };
    }

    private object ControlWslSetup(string actionName, string distro, bool dryRun, bool confirm)
    {
        if (!Enum.TryParse<WslToolSetupAction>(actionName, true, out var action))
            throw new InvalidOperationException($"Unknown WSL setup action '{actionName}'. Use a value returned by capabilities.");
        var selectedDistro = string.IsNullOrWhiteSpace(distro) ? _settings.WslDistro : distro.Trim();
        var workflow = _coreServices.Environment.WslToolSetupWorkflow;
        if (workflow.RequiresUbuntuDistro(action) && string.IsNullOrWhiteSpace(selectedDistro))
            throw new InvalidOperationException("This WSL action requires distro=<Ubuntu name>.");
        var plan = workflow.Plan(action, selectedDistro, AppDisplayName);
        if (dryRun) return new { plan.Action, launchKind = plan.LaunchKind.ToString(), plan.Title, plan.ConfirmationMessage, plan.IsWarning, plan.Elevated, distro = selectedDistro };
        var outcome = _coreServices.Environment.WslToolSetupApplication.Run(
            action,
            selectedDistro,
            AppDisplayName,
            new WslToolSetupApplicationActions(_ => confirm, SetStatus));
        return new { outcome = outcome.ToString(), plan.StartedStatus };
    }

    private async Task<object> ControlUpdateInstallAsync(bool dryRun, bool confirm, CancellationToken cancellationToken)
    {
        var check = await _coreServices.App.AppUpdateWorkflow.CheckLatestAsync(manual: true, cancellationToken);
        if (dryRun || !check.Update.IsAvailable)
            return new { check.Update, wouldInstall = check.Update.IsAvailable };
        if (!confirm) throw new InvalidOperationException("Application update installation requires confirm=true.");
        RunBackground(() => InstallAppUpdateAsync(check.Update, confirm: false), "Controlled app update failed");
        return new { scheduled = true, check.Update.LatestVersion };
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
