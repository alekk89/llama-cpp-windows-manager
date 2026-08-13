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
            "runtime.catalog" => await ControlRuntimeCatalogAsync(cancellationToken),
            "runtime-repository.add" => await ControlRuntimeRepositoryAddAsync(body, dryRun, cancellationToken),
            "runtime.delete" => await ControlRuntimeDeleteAsync(ControlRequired(body, "runtime"), dryRun),
            "runtime-package.install" => await ControlRuntimePackageAsync("install", ControlRequired(body, "preset"), dryRun),
            "runtime-package.check" => await ControlRuntimePackageAsync("check", ControlRequired(body, "preset"), dryRun),
            "runtime-package.delete" => await ControlRuntimePackageAsync("delete", ControlRequired(body, "preset"), dryRun),
            "runtime-source.download" => await ControlRuntimeSourceAsync("download", ControlRequired(body, "preset"), dryRun),
            "runtime-source.check" => await ControlRuntimeSourceAsync("check", ControlRequired(body, "preset"), dryRun),
            "runtime-source.delete" => await ControlRuntimeSourceDeleteAsync(ControlRequired(body, "source"), dryRun),
            "runtime-build.start" => await ControlRuntimeBuildAsync(ControlRequired(body, "preset"), ControlBool(body, "update"), ControlString(body, "source"), dryRun),
            "runtime-build.delete" => await ControlRuntimeBuildDeleteAsync(ControlRequired(body, "preset"), dryRun),
            "runtime-job.cancel" => await ControlRuntimeJobAsync("cancel", ControlRequired(body, "job"), dryRun),
            "runtime-job.retry" => await ControlRuntimeJobAsync("retry", ControlRequired(body, "job"), dryRun),
            "runtime-job.clear" => await ControlRuntimeJobAsync("clear", ControlRequired(body, "job"), dryRun),
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

    private async Task<object> ControlRuntimeCatalogAsync(CancellationToken cancellationToken)
    {
        var data = _coreServices.Runtime.RuntimeCatalogData;
        var runtimes = await AppServices.StateStore!.ListRuntimesAsync();
        var sources = await data.LoadSourcesAsync(_settings.RuntimeRoot, cancellationToken);
        return new
        {
            runtimes,
            packages = data.PackagePresets(),
            buildPresets = data.BuildPresets(_settings.RuntimeRoot),
            sources
        };
    }

    private async Task<object> ControlRuntimeDeleteAsync(string identifier, bool dryRun)
    {
        var runtime = (await AppServices.StateStore!.ListRuntimesAsync()).FirstOrDefault(candidate =>
            candidate.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase)
            || candidate.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Runtime '{identifier}' was not found.");
        if (dryRun) return new { runtime, wouldDelete = true };
        var service = RuntimeServices.RuntimeBuildDeletionApplication ?? throw new InvalidOperationException("Runtime deletion is not ready.");
        var outcome = await service.DeleteRuntimeAsync(runtime, _settings, ControlRuntimeDeletionActions());
        return new { outcome = outcome.ToString(), runtime = runtime.Id };
    }

    private async Task<object> ControlRuntimeRepositoryAddAsync(JsonObject body, bool dryRun, CancellationToken cancellationToken)
    {
        var draft = new RuntimeCustomRepositoryDraft(
            ControlRequired(body, "label"),
            ControlRequired(body, "repo"),
            ControlString(body, "branch"),
            ControlRequired(body, "backend"));
        var service = RuntimeServices.CustomRuntimeRepositories ?? throw new InvalidOperationException("Custom runtime repositories are not ready.");
        var validation = service.BuildPreset(draft);
        if (!validation.Success || validation.Preset is null)
            throw new InvalidOperationException(validation.StatusMessage);
        if (dryRun) return new { validation.Preset, wouldAdd = true };
        var result = await service.AddAsync(_settings.RuntimeRoot, draft, cancellationToken);
        if (!result.Success) throw new InvalidOperationException(result.StatusMessage);
        await RefreshRuntimesAsync();
        return result;
    }

    private async Task<object> ControlRuntimePackageAsync(string action, string identifier, bool dryRun)
    {
        var preset = ResolveRuntimePackagePreset(identifier);
        if (dryRun) return new { action, preset, wouldExecute = true };
        var service = RuntimeServices.RuntimePackageApplication ?? throw new InvalidOperationException("Runtime packages are not ready.");
        var actions = ControlRuntimePackageActions();
        var outcome = action switch
        {
            "install" => await service.InstallAsync(preset, _settings, _runtimeCatalogState, MaxLogBytes(), actions),
            "check" => await service.CheckUpdateAsync(preset, null, _settings, _runtimeCatalogState, MaxLogBytes(), actions),
            "delete" => await service.DeleteBuildsAsync(preset, _settings, _runtimeCatalogState, actions),
            _ => throw new InvalidOperationException($"Unknown runtime package action '{action}'.")
        };
        return new { outcome = outcome.ToString(), preset = preset.Id };
    }

    private async Task<object> ControlRuntimeSourceAsync(string action, string identifier, bool dryRun)
    {
        var preset = ResolveRuntimeBuildPreset(identifier);
        if (dryRun) return new { action, preset, wouldExecute = true };
        var service = RuntimeServices.RuntimeSourceApplication ?? throw new InvalidOperationException("Runtime sources are not ready.");
        var actions = ControlRuntimeSourceActions();
        var outcome = action == "download"
            ? await service.DownloadAsync(preset, _settings, _runtimeCatalogState, MaxLogBytes(), actions)
            : await service.CheckUpdateAsync(preset, null, _settings, _runtimeCatalogState, MaxLogBytes(), actions);
        return new { outcome = outcome.ToString(), preset = preset.Id };
    }

    private async Task<object> ControlRuntimeSourceDeleteAsync(string identifier, bool dryRun)
    {
        var source = ResolveRuntimeSource(identifier);
        if (dryRun) return new { source, wouldDelete = true };
        var service = RuntimeServices.RuntimeBuildDeletionApplication ?? throw new InvalidOperationException("Runtime source deletion is not ready.");
        var outcome = await service.DeleteSourceAsync(source, _settings, ControlRuntimeDeletionActions());
        return new { outcome = outcome.ToString(), source = source.SourceDir };
    }

    private async Task<object> ControlRuntimeBuildAsync(string identifier, bool update, string sourceIdentifier, bool dryRun)
    {
        var preset = ResolveRuntimeBuildPreset(identifier);
        var source = string.IsNullOrWhiteSpace(sourceIdentifier) ? null : ResolveRuntimeSource(sourceIdentifier);
        if (dryRun) return new { preset, source, update, wouldBuild = true };
        var service = RuntimeServices.RuntimeBuildApplication ?? throw new InvalidOperationException("Runtime builds are not ready.");
        var outcome = await service.BuildAsync(new RuntimeBuildApplicationRequest(
            preset,
            _settings,
            update,
            source,
            MaxLogBytes()), ControlRuntimeBuildActions());
        return new { outcome = outcome.ToString(), preset = preset.Id, update };
    }

    private async Task<object> ControlRuntimeBuildDeleteAsync(string identifier, bool dryRun)
    {
        var preset = ResolveRuntimeBuildPreset(identifier);
        if (dryRun) return new { preset, wouldDelete = true };
        var service = RuntimeServices.RuntimeBuildDeletionApplication ?? throw new InvalidOperationException("Runtime build deletion is not ready.");
        var outcome = await service.DeletePresetBuildsAsync(preset, _settings, ControlRuntimeDeletionActions());
        return new { outcome = outcome.ToString(), preset = preset.Id };
    }

    private async Task<object> ControlRuntimeJobAsync(string action, string jobId, bool dryRun)
    {
        var job = (await AppServices.StateStore!.ListJobsAsync()).FirstOrDefault(candidate => candidate.Id.Equals(jobId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Runtime job '{jobId}' was not found.");
        if (!job.Kind.Contains("runtime", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Job '{jobId}' is not a runtime job.");
        if (dryRun) return new { action, job.Id, job.Kind, status = job.Status.ToString(), wouldExecute = true };
        var service = RuntimeServices.RuntimeBuildJobApplication ?? throw new InvalidOperationException("Runtime job controls are not ready.");
        var actions = ControlRuntimeBuildJobActions();
        var outcome = action switch
        {
            "cancel" => await service.CancelAsync(job, _settings, MaxLogBytes(), actions),
            "retry" => await service.RetryAsync(job, actions),
            "clear" => await service.ClearAsync(job, actions),
            _ => throw new InvalidOperationException($"Unknown runtime job action '{action}'.")
        };
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

    private RuntimePackagePreset ResolveRuntimePackagePreset(string identifier)
        => _coreServices.Runtime.RuntimeCatalogData.PackagePresets().FirstOrDefault(candidate =>
               candidate.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase)
               || candidate.Label.Equals(identifier, StringComparison.OrdinalIgnoreCase))
           ?? throw new KeyNotFoundException($"Runtime package preset '{identifier}' was not found.");

    private RuntimeBuildPreset ResolveRuntimeBuildPreset(string identifier)
    {
        var presets = _coreServices.Runtime.RuntimeCatalogData.BuildPresets(_settings.RuntimeRoot);
        return presets.FirstOrDefault(candidate =>
                   candidate.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase)
                   || candidate.Label.Equals(identifier, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"Runtime build preset '{identifier}' was not found.");
    }

    private RuntimeSourceEntry ResolveRuntimeSource(string identifier)
        => _coreServices.Runtime.RuntimeCatalogData.Sources(_settings.RuntimeRoot)
               .OrderByDescending(source => source.DownloadedAt)
               .FirstOrDefault(source =>
                   source.SourceDir.Equals(identifier, StringComparison.OrdinalIgnoreCase)
                   || source.PresetId.Equals(identifier, StringComparison.OrdinalIgnoreCase)
                   || source.Label.Equals(identifier, StringComparison.OrdinalIgnoreCase))
           ?? throw new KeyNotFoundException($"Runtime source '{identifier}' was not found.");

    private RuntimePackageApplicationActions ControlRuntimePackageActions()
        => new(
            ControlRunBusyAsync,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => { },
            SetStatus,
            (_, message) => SetStatus(message),
            _ => true,
            _ => true);

    private RuntimeSourceApplicationActions ControlRuntimeSourceActions()
        => new(
            ControlRunBusyAsync,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => { },
            SetStatus,
            (_, message) => SetStatus(message));

    private RuntimeBuildApplicationActions ControlRuntimeBuildActions()
        => new(
            ControlRunBusyAsync,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            SetStatus,
            (_, message) => SetStatus(message),
            message => Dispatcher.InvokeAsync(() => SetStatus(message)).Task);

    private RuntimeBuildDeletionApplicationActions ControlRuntimeDeletionActions()
        => new(
            _ => true,
            ControlRunBusyAsync,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            SetStatus);

    private RuntimeBuildJobApplicationActions ControlRuntimeBuildJobActions()
        => new(
            _ => true,
            ControlRunBusyAsync,
            () => Task.CompletedTask,
            retry => RuntimeServices.RuntimeBuildApplication!.BuildAsync(
                new RuntimeBuildApplicationRequest(retry.Preset!, _settings, retry.Update, retry.Source, MaxLogBytes()),
                ControlRuntimeBuildActions()),
            SetStatus);

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
