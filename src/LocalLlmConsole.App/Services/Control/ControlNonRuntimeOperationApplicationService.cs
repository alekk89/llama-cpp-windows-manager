using System.Text.Json.Nodes;

namespace LocalLlmConsole.Services;

public sealed record ControlNonRuntimeOperationDependencies(
    CacheClearWorkflowService Cache,
    HuggingFaceService HuggingFace,
    LogPageApplicationService Logs,
    LogPageWorkflowService LogWorkflow,
    LifetimeMetricsApplicationService Lifetime,
    DownloadHistoryApplicationService Downloads,
    WindowsToolSetupWorkflowService WindowsSetupWorkflow,
    WindowsToolSetupApplicationService WindowsSetupApplication,
    WslEnvironmentService WslEnvironment,
    WslPageWorkflowService WslPageWorkflow,
    WslToolSetupWorkflowService WslSetupWorkflow,
    WslToolSetupApplicationService WslSetupApplication,
    AppUpdateWorkflowService Updates);

public sealed record ControlNonRuntimeOperationActions(
    Func<AppSettings> Settings,
    Func<IReadOnlyList<LoadedModelSessionSnapshot>> Sessions,
    Func<AppSettings, CancellationToken, Task<AppSettings>> ApplySettingsAsync,
    Action<LoadedModelSessionSnapshot?> ResetLifetimeCounters,
    Func<string, Func<Task>, Task> RunBusyAsync,
    Action<string> SetStatus,
    Action<AppUpdateInfo> ScheduleUpdateInstall);

public sealed class ControlNonRuntimeOperationApplicationService
{
    private static readonly HashSet<string> SupportedOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "cache.plan", "cache.clear", "logs.delete", "logs.delete-all",
        "lifetime.list", "lifetime.delete", "lifetime.delete-all", "downloads.delete",
        "windows.status", "windows.setup", "wsl.status", "wsl.select", "wsl.setup",
        "updates.check", "updates.install"
    };

    private readonly ControlNonRuntimeOperationDependencies _dependencies;
    private readonly ControlNonRuntimeOperationActions _actions;

    public ControlNonRuntimeOperationApplicationService(
        ControlNonRuntimeOperationDependencies dependencies,
        ControlNonRuntimeOperationActions actions)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public static bool CanHandle(string operation) => SupportedOperations.Contains(operation);

    public Task<object> ExecuteAsync(
        string operation,
        JsonObject body,
        bool dryRun,
        bool confirm,
        CancellationToken cancellationToken)
        => operation.ToLowerInvariant() switch
        {
            "cache.plan" => CacheAsync(clear: false, dryRun: true, cancellationToken),
            "cache.clear" => CacheAsync(clear: true, dryRun, cancellationToken),
            "logs.delete" => LogsAsync(Required(body, "file"), all: false, dryRun, cancellationToken),
            "logs.delete-all" => LogsAsync("", all: true, dryRun, cancellationToken),
            "lifetime.list" => LifetimeAsync("list", "", dryRun, cancellationToken),
            "lifetime.delete" => LifetimeAsync("delete", Required(body, "model"), dryRun, cancellationToken),
            "lifetime.delete-all" => LifetimeAsync("delete-all", "", dryRun, cancellationToken),
            "downloads.delete" => DownloadDeleteAsync(Required(body, "job"), dryRun, cancellationToken),
            "windows.status" => WindowsStatusAsync(cancellationToken),
            "windows.setup" => Task.FromResult(WindowsSetup(Required(body, "action"), dryRun, confirm)),
            "wsl.status" => WslStatusAsync(cancellationToken),
            "wsl.select" => WslSelectAsync(Required(body, "distro"), dryRun, cancellationToken),
            "wsl.setup" => Task.FromResult(WslSetup(Required(body, "action"), String(body, "distro"), dryRun, confirm)),
            "updates.check" => UpdateCheckAsync(cancellationToken),
            "updates.install" => UpdateInstallAsync(dryRun, confirm, cancellationToken),
            _ => throw new KeyNotFoundException($"Control operation '{operation}' was not found.")
        };

    private async Task<object> CacheAsync(bool clear, bool dryRun, CancellationToken cancellationToken)
    {
        var settings = _actions.Settings();
        var plan = await _dependencies.Cache.PlanAsync(settings, _dependencies.HuggingFace.ActiveDownloadCount > 0, cancellationToken);
        var ready = string.Equals(plan.Status.ToString(), "Ready", StringComparison.Ordinal);
        if (!clear || dryRun || !ready) return new { plan, wouldClear = clear && ready };
        await _dependencies.Cache.ClearAsync(settings, cancellationToken);
        _actions.SetStatus($"Cleared cache ({plan.DisplaySize}) through the control API.");
        return new { cleared = true, plan.SizeBytes };
    }

    private async Task<object> LogsAsync(string file, bool all, bool dryRun, CancellationToken cancellationToken)
    {
        var sessions = _actions.Sessions();
        LogDeleteCommandPlan plan;
        if (all)
        {
            plan = await _dependencies.Logs.BuildAllDeletionCommandAsync(sessions, cancellationToken);
        }
        else
        {
            var name = Path.GetFileName(file);
            if (!name.Equals(file, StringComparison.Ordinal))
                throw new InvalidOperationException("Log identifiers must be file names, not paths.");
            plan = _dependencies.Logs.BuildSingleDeletionCommand(Path.Combine(_dependencies.LogWorkflow.LogRoot, name), sessions);
        }
        if (dryRun || !plan.CanDelete) return new { plan.CanDelete, plan.StatusMessage, plan.ConfirmationMessage };
        var outcome = await _dependencies.Logs.DeleteAsync(plan, new LogPageDeleteApplicationActions(
            _ => true, _actions.RunBusyAsync, () => { }, () => Task.CompletedTask, _actions.SetStatus), cancellationToken);
        return new { outcome = outcome.ToString() };
    }

    private async Task<object> LifetimeAsync(string action, string modelId, bool dryRun, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var records = await _dependencies.Lifetime.ListAsync();
        if (action == "list") return records;
        if (action == "delete" && !records.Any(record => record.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase)))
            throw new KeyNotFoundException($"Lifetime metrics for model '{modelId}' were not found.");
        if (dryRun) return new { action, modelId, affected = action == "delete-all" ? records.Count : 1 };
        if (action == "delete")
        {
            await _dependencies.Lifetime.DeleteModelUsageAsync(modelId);
            _actions.ResetLifetimeCounters(_actions.Sessions().FirstOrDefault(session => session.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase)));
        }
        else
        {
            await _dependencies.Lifetime.DeleteAllUsageAsync();
            _actions.ResetLifetimeCounters(null);
        }
        return new { deleted = action == "delete-all" ? "all" : modelId };
    }

    private async Task<object> DownloadDeleteAsync(string jobId, bool dryRun, CancellationToken cancellationToken)
    {
        var job = (await _dependencies.Downloads.ListJobsAsync()).FirstOrDefault(candidate => candidate.Id.Equals(jobId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Download job '{jobId}' was not found.");
        if (dryRun) return new { job.Id, job.Kind, status = job.Status.ToString(), wouldDelete = true };
        var outcome = await _dependencies.Downloads.DeleteAsync(job, _actions.Settings(), new DownloadHistoryDeleteApplicationActions(
            _ => true,
            new DownloadHistoryCommandApplicationActions(_actions.RunBusyAsync, () => Task.CompletedTask, _actions.SetStatus, _ => { })), cancellationToken);
        return new { outcome = outcome.ToString(), job = job.Id };
    }

    private object WindowsSetup(string actionName, bool dryRun, bool confirm)
    {
        if (!Enum.TryParse<WindowsToolSetupAction>(actionName, true, out var action))
            throw new InvalidOperationException($"Unknown Windows setup action '{actionName}'. Use CPU, CUDA, Vulkan, or SYCL.");
        var plan = _dependencies.WindowsSetupWorkflow.Plan(action);
        if (dryRun) return new { plan.Action, plan.Title, plan.ConfirmationMessage, plan.Elevated };
        var outcome = _dependencies.WindowsSetupApplication.Run(action, new WindowsToolSetupApplicationActions(_ => confirm, _actions.SetStatus));
        return new { outcome = outcome.ToString(), plan.StartedStatus };
    }

    private async Task<object> WindowsStatusAsync(CancellationToken cancellationToken)
        => await _dependencies.WindowsSetupWorkflow.RefreshAsync(cancellationToken);

    private async Task<object> WslStatusAsync(CancellationToken cancellationToken)
        => await _dependencies.WslPageWorkflow.RefreshAsync(_actions.Settings(), cancellationToken);

    private async Task<object> WslSelectAsync(string distro, bool dryRun, CancellationToken cancellationToken)
    {
        var report = await _dependencies.WslEnvironment.DetectAsync(cancellationToken);
        if (!report.Distros.Any(candidate => candidate.Name.Equals(distro, StringComparison.OrdinalIgnoreCase)))
            throw new KeyNotFoundException($"WSL distro '{distro}' was not found.");
        if (dryRun) return new { distro, wouldSelect = true };
        var settings = await _actions.ApplySettingsAsync(_actions.Settings() with { WslDistro = distro.Trim() }, cancellationToken);
        return new { selected = settings.WslDistro };
    }

    private object WslSetup(string actionName, string distro, bool dryRun, bool confirm)
    {
        if (!Enum.TryParse<WslToolSetupAction>(actionName, true, out var action))
            throw new InvalidOperationException($"Unknown WSL setup action '{actionName}'. Use a value returned by capabilities.");
        var selectedDistro = string.IsNullOrWhiteSpace(distro) ? _actions.Settings().WslDistro : distro.Trim();
        if (_dependencies.WslSetupWorkflow.RequiresUbuntuDistro(action) && string.IsNullOrWhiteSpace(selectedDistro))
            throw new InvalidOperationException("This WSL action requires distro=<Ubuntu name>.");
        var plan = _dependencies.WslSetupWorkflow.Plan(action, selectedDistro, "llama.cpp Windows Manager");
        if (dryRun) return new { plan.Action, launchKind = plan.LaunchKind.ToString(), plan.Title, plan.ConfirmationMessage, plan.IsWarning, plan.Elevated, distro = selectedDistro };
        var outcome = _dependencies.WslSetupApplication.Run(
            action, selectedDistro, "llama.cpp Windows Manager", new WslToolSetupApplicationActions(_ => confirm, _actions.SetStatus));
        return new { outcome = outcome.ToString(), plan.StartedStatus };
    }

    private async Task<object> UpdateCheckAsync(CancellationToken cancellationToken)
        => await _dependencies.Updates.CheckLatestAsync(manual: true, cancellationToken);

    private async Task<object> UpdateInstallAsync(bool dryRun, bool confirm, CancellationToken cancellationToken)
    {
        var check = await _dependencies.Updates.CheckLatestAsync(manual: true, cancellationToken);
        if (dryRun || !check.Update.IsAvailable) return new { check.Update, wouldInstall = check.Update.IsAvailable };
        if (!confirm) throw new InvalidOperationException("Application update installation requires confirm=true.");
        _actions.ScheduleUpdateInstall(check.Update);
        return new { scheduled = true, check.Update.LatestVersion };
    }

    private static string Required(JsonObject body, string name)
    {
        var value = String(body, name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Operation parameter '{name}' is required.")
            : value;
    }

    private static string String(JsonObject body, string name) => body[name]?.ToString()?.Trim() ?? "";
}
