using System.Text.Json.Nodes;

namespace LocalLlmConsole.Services;

public sealed record ControlRuntimeOperationDependencies(
    StateStore StateStore,
    RuntimeCatalogDataService CatalogData,
    RuntimeCustomRepositoryService CustomRepositories,
    RuntimeBuildDeletionApplicationService DeletionApplication,
    RuntimePackageApplicationService PackageApplication,
    RuntimeSourceApplicationService SourceApplication,
    RuntimeBuildApplicationService BuildApplication,
    RuntimeBuildJobApplicationService BuildJobApplication,
    RuntimeCatalogSessionState CatalogState);

public sealed record ControlRuntimeOperationActions(
    Func<AppSettings> Settings,
    Func<long> MaxLogBytes,
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<Task> RefreshRuntimeCatalogAsync,
    Action<string> SetStatus,
    Func<string, Task> SetProgressStatusAsync);

public sealed class ControlRuntimeOperationApplicationService
{
    private static readonly HashSet<string> SupportedOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "runtime.catalog",
        "runtime-repository.add",
        "runtime.delete",
        "runtime-package.install",
        "runtime-package.check",
        "runtime-package.delete",
        "runtime-source.download",
        "runtime-source.check",
        "runtime-source.delete",
        "runtime-build.start",
        "runtime-build.delete",
        "runtime-job.cancel",
        "runtime-job.retry",
        "runtime-job.clear"
    };

    private readonly ControlRuntimeOperationDependencies _dependencies;
    private readonly ControlRuntimeOperationActions _actions;

    public ControlRuntimeOperationApplicationService(
        ControlRuntimeOperationDependencies dependencies,
        ControlRuntimeOperationActions actions)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public static bool CanHandle(string operation)
        => SupportedOperations.Contains(operation);

    public Task<object> ExecuteAsync(
        string operation,
        JsonObject body,
        bool dryRun,
        CancellationToken cancellationToken)
        => operation.ToLowerInvariant() switch
        {
            "runtime.catalog" => CatalogAsync(cancellationToken),
            "runtime-repository.add" => AddRepositoryAsync(body, dryRun, cancellationToken),
            "runtime.delete" => DeleteRuntimeAsync(Required(body, "runtime"), dryRun),
            "runtime-package.install" => RunPackageAsync("install", Required(body, "preset"), dryRun),
            "runtime-package.check" => RunPackageAsync("check", Required(body, "preset"), dryRun),
            "runtime-package.delete" => RunPackageAsync("delete", Required(body, "preset"), dryRun),
            "runtime-source.download" => RunSourceAsync("download", Required(body, "preset"), dryRun),
            "runtime-source.check" => RunSourceAsync("check", Required(body, "preset"), dryRun),
            "runtime-source.delete" => DeleteSourceAsync(Required(body, "source"), dryRun),
            "runtime-build.start" => BuildAsync(Required(body, "preset"), Bool(body, "update"), String(body, "source"), dryRun),
            "runtime-build.delete" => DeleteBuildAsync(Required(body, "preset"), dryRun),
            "runtime-job.cancel" => RunJobAsync("cancel", Required(body, "job"), dryRun),
            "runtime-job.retry" => RunJobAsync("retry", Required(body, "job"), dryRun),
            "runtime-job.clear" => RunJobAsync("clear", Required(body, "job"), dryRun),
            _ => throw new KeyNotFoundException($"Control runtime operation '{operation}' was not found.")
        };

    private async Task<object> CatalogAsync(CancellationToken cancellationToken)
    {
        var settings = _actions.Settings();
        var runtimes = await _dependencies.StateStore.ListRuntimesAsync();
        var sources = await _dependencies.CatalogData.LoadSourcesAsync(settings.RuntimeRoot, cancellationToken);
        return new
        {
            runtimes,
            packages = _dependencies.CatalogData.PackagePresets(),
            buildPresets = _dependencies.CatalogData.BuildPresets(settings.RuntimeRoot),
            sources
        };
    }

    private async Task<object> AddRepositoryAsync(JsonObject body, bool dryRun, CancellationToken cancellationToken)
    {
        var draft = new RuntimeCustomRepositoryDraft(
            Required(body, "label"),
            Required(body, "repo"),
            String(body, "branch"),
            Required(body, "backend"));
        var validation = _dependencies.CustomRepositories.BuildPreset(draft);
        if (!validation.Success || validation.Preset is null)
            throw new InvalidOperationException(validation.StatusMessage);
        if (dryRun) return new { validation.Preset, wouldAdd = true };

        var result = await _dependencies.CustomRepositories.AddAsync(
            _actions.Settings().RuntimeRoot,
            draft,
            cancellationToken);
        if (!result.Success) throw new InvalidOperationException(result.StatusMessage);
        await _actions.RefreshRuntimeCatalogAsync();
        return result;
    }

    private async Task<object> DeleteRuntimeAsync(string identifier, bool dryRun)
    {
        var runtime = (await _dependencies.StateStore.ListRuntimesAsync()).FirstOrDefault(candidate =>
            candidate.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase)
            || candidate.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Runtime '{identifier}' was not found.");
        if (dryRun) return new { runtime, wouldDelete = true };

        var outcome = await _dependencies.DeletionApplication.DeleteRuntimeAsync(
            runtime,
            _actions.Settings(),
            DeletionActions());
        return new { outcome = outcome.ToString(), runtime = runtime.Id };
    }

    private async Task<object> RunPackageAsync(string action, string identifier, bool dryRun)
    {
        var preset = ResolvePackage(identifier);
        if (dryRun) return new { action, preset, wouldExecute = true };

        var settings = _actions.Settings();
        var outcome = action switch
        {
            "install" => await _dependencies.PackageApplication.InstallAsync(preset, settings, _dependencies.CatalogState, _actions.MaxLogBytes(), PackageActions()),
            "check" => await _dependencies.PackageApplication.CheckUpdateAsync(preset, null, settings, _dependencies.CatalogState, _actions.MaxLogBytes(), PackageActions()),
            "delete" => await _dependencies.PackageApplication.DeleteBuildsAsync(preset, settings, _dependencies.CatalogState, PackageActions()),
            _ => throw new InvalidOperationException($"Unknown runtime package action '{action}'.")
        };
        return new { outcome = outcome.ToString(), preset = preset.Id };
    }

    private async Task<object> RunSourceAsync(string action, string identifier, bool dryRun)
    {
        var preset = ResolveBuild(identifier);
        if (dryRun) return new { action, preset, wouldExecute = true };

        var settings = _actions.Settings();
        var outcome = action == "download"
            ? await _dependencies.SourceApplication.DownloadAsync(preset, settings, _dependencies.CatalogState, _actions.MaxLogBytes(), SourceActions())
            : await _dependencies.SourceApplication.CheckUpdateAsync(preset, null, settings, _dependencies.CatalogState, _actions.MaxLogBytes(), SourceActions());
        return new { outcome = outcome.ToString(), preset = preset.Id };
    }

    private async Task<object> DeleteSourceAsync(string identifier, bool dryRun)
    {
        var source = ResolveSource(identifier);
        if (dryRun) return new { source, wouldDelete = true };
        var outcome = await _dependencies.DeletionApplication.DeleteSourceAsync(
            source,
            _actions.Settings(),
            DeletionActions());
        return new { outcome = outcome.ToString(), source = source.SourceDir };
    }

    private async Task<object> BuildAsync(string identifier, bool update, string sourceIdentifier, bool dryRun)
    {
        var preset = ResolveBuild(identifier);
        var source = string.IsNullOrWhiteSpace(sourceIdentifier) ? null : ResolveSource(sourceIdentifier);
        if (dryRun) return new { preset, source, update, wouldBuild = true };

        var outcome = await _dependencies.BuildApplication.BuildAsync(
            new RuntimeBuildApplicationRequest(preset, _actions.Settings(), update, source, _actions.MaxLogBytes()),
            BuildActions());
        return new { outcome = outcome.ToString(), preset = preset.Id, update };
    }

    private async Task<object> DeleteBuildAsync(string identifier, bool dryRun)
    {
        var preset = ResolveBuild(identifier);
        if (dryRun) return new { preset, wouldDelete = true };
        var outcome = await _dependencies.DeletionApplication.DeletePresetBuildsAsync(
            preset,
            _actions.Settings(),
            DeletionActions());
        return new { outcome = outcome.ToString(), preset = preset.Id };
    }

    private async Task<object> RunJobAsync(string action, string jobId, bool dryRun)
    {
        var job = (await _dependencies.StateStore.ListJobsAsync()).FirstOrDefault(candidate =>
            candidate.Id.Equals(jobId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Runtime job '{jobId}' was not found.");
        if (!job.Kind.Contains("runtime", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Job '{jobId}' is not a runtime job.");
        if (dryRun) return new { action, job.Id, job.Kind, status = job.Status.ToString(), wouldExecute = true };

        var settings = _actions.Settings();
        var outcome = action switch
        {
            "cancel" => await _dependencies.BuildJobApplication.CancelAsync(job, settings, _actions.MaxLogBytes(), BuildJobActions()),
            "retry" => await _dependencies.BuildJobApplication.RetryAsync(job, BuildJobActions()),
            "clear" => await _dependencies.BuildJobApplication.ClearAsync(job, BuildJobActions()),
            _ => throw new InvalidOperationException($"Unknown runtime job action '{action}'.")
        };
        return new { outcome = outcome.ToString(), job = job.Id };
    }

    private RuntimePackagePreset ResolvePackage(string identifier)
        => _dependencies.CatalogData.PackagePresets().FirstOrDefault(candidate =>
               candidate.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase)
               || candidate.Label.Equals(identifier, StringComparison.OrdinalIgnoreCase))
           ?? throw new KeyNotFoundException($"Runtime package preset '{identifier}' was not found.");

    private RuntimeBuildPreset ResolveBuild(string identifier)
    {
        var settings = _actions.Settings();
        return _dependencies.CatalogData.BuildPresets(settings.RuntimeRoot).FirstOrDefault(candidate =>
                   candidate.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase)
                   || candidate.Label.Equals(identifier, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"Runtime build preset '{identifier}' was not found.");
    }

    private RuntimeSourceEntry ResolveSource(string identifier)
    {
        var settings = _actions.Settings();
        return _dependencies.CatalogData.Sources(settings.RuntimeRoot)
                   .OrderByDescending(source => source.DownloadedAt)
                   .FirstOrDefault(source =>
                       source.SourceDir.Equals(identifier, StringComparison.OrdinalIgnoreCase)
                       || source.PresetId.Equals(identifier, StringComparison.OrdinalIgnoreCase)
                       || source.Label.Equals(identifier, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"Runtime source '{identifier}' was not found.");
    }

    private RuntimePackageApplicationActions PackageActions()
        => new(
            _actions.RunBusyAsync,
            NoOpAsync,
            NoOpAsync,
            NoOpAsync,
            () => { },
            _actions.SetStatus,
            (_, message) => _actions.SetStatus(message),
            _ => true,
            _ => true);

    private RuntimeSourceApplicationActions SourceActions()
        => new(
            _actions.RunBusyAsync,
            NoOpAsync,
            NoOpAsync,
            NoOpAsync,
            () => { },
            _actions.SetStatus,
            (_, message) => _actions.SetStatus(message));

    private RuntimeBuildApplicationActions BuildActions()
        => new(
            _actions.RunBusyAsync,
            NoOpAsync,
            NoOpAsync,
            _actions.SetStatus,
            (_, message) => _actions.SetStatus(message),
            _actions.SetProgressStatusAsync);

    private RuntimeBuildDeletionApplicationActions DeletionActions()
        => new(_ => true, _actions.RunBusyAsync, NoOpAsync, NoOpAsync, _actions.SetStatus);

    private RuntimeBuildJobApplicationActions BuildJobActions()
        => new(
            _ => true,
            _actions.RunBusyAsync,
            retry => _dependencies.BuildApplication.BuildAsync(
                new RuntimeBuildApplicationRequest(
                    retry.Preset!,
                    _actions.Settings(),
                    retry.Update,
                    retry.Source,
                    _actions.MaxLogBytes()),
                BuildActions()),
            _actions.SetStatus);

    private static Task NoOpAsync() => Task.CompletedTask;

    private static string Required(JsonObject body, string name)
    {
        var value = String(body, name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Operation parameter '{name}' is required.")
            : value;
    }

    private static string String(JsonObject body, string name)
        => body[name]?.ToString()?.Trim() ?? "";

    private static bool Bool(JsonObject body, string name)
        => body[name] is JsonValue value && value.TryGetValue<bool>(out var result) && result;
}
