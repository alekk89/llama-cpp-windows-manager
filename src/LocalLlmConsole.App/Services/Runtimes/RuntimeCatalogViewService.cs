namespace LocalLlmConsole.Services;

public sealed record RuntimeCatalogViewRequest(
    IReadOnlyList<RuntimeRecord> Runtimes,
    IReadOnlyList<RuntimeSourceEntry> Sources,
    IReadOnlyList<RuntimeBuildPreset> BuildPresets,
    IReadOnlyList<RuntimePackagePreset> PackagePresets,
    IReadOnlyDictionary<string, List<string>> ModelsByRuntime,
    IReadOnlySet<string> ActiveRuntimeIds,
    IReadOnlyDictionary<string, RuntimeUpdateState> RuntimeUpdateStates,
    IReadOnlyDictionary<string, RuntimePackageUpdateState> RuntimePackageUpdateStates);

public sealed record RuntimeCatalogViewRows(
    IReadOnlyList<RuntimeCatalogRow> Runtimes,
    IReadOnlyList<RuntimeBuildPresetRow> BuildPresets,
    IReadOnlyList<RuntimePackagePresetRow> PackagePresets);

public sealed class RuntimeCatalogViewService
{
    private readonly RuntimePackageStatusService _packageStatus;

    public RuntimeCatalogViewService(RuntimePackageStatusService packageStatus)
    {
        _packageStatus = packageStatus ?? throw new ArgumentNullException(nameof(packageStatus));
    }

    public RuntimeCatalogViewRows BuildRows(RuntimeCatalogViewRequest request)
        => new(
            BuildRuntimeRows(request.Runtimes, request.Sources, request.ModelsByRuntime, request.ActiveRuntimeIds),
            BuildPresetRows(request.BuildPresets, request.Runtimes, request.Sources, request.RuntimeUpdateStates),
            BuildPackageRows(
                request.PackagePresets,
                request.BuildPresets,
                request.Runtimes,
                request.Sources,
                request.RuntimeUpdateStates,
                request.RuntimePackageUpdateStates));

    public static IReadOnlyList<RuntimeCatalogRow> BuildRuntimeRows(
        IReadOnlyList<RuntimeRecord> runtimes,
        IReadOnlyList<RuntimeSourceEntry> sources,
        IReadOnlyDictionary<string, List<string>> modelsByRuntime,
        IReadOnlySet<string> activeRuntimeIds)
    {
        var rows = new List<RuntimeCatalogRow>();
        foreach (var runtime in runtimes)
        {
            var availability = RuntimeAvailabilityService.Inspect(runtime);
            modelsByRuntime.TryGetValue(runtime.Id, out var modelNames);
            modelNames ??= [];
            var isActiveRuntime = activeRuntimeIds.Contains(runtime.Id);
            var provenance = RuntimeInstallationVerificationService.Describe(runtime);
            rows.Add(new RuntimeCatalogRow
            {
                Kind = RuntimeCatalogRowKind.Runtime,
                Name = runtime.Name,
                Backend = runtime.Backend.ToString(),
                State = availability.IsAvailable ? $"Built {runtime.Mode}" : "Missing executable",
                Location = runtime.ExecutablePath,
                Details = RuntimeDetails(availability, provenance, modelNames),
                Vendor = RuntimeInventoryFilterService.Vendor(runtime.Backend),
                Platform = RuntimeInventoryFilterService.Platform(runtime.Mode),
                CanBuild = false,
                VerifyAction = Loc.T("Runtimes.ActionBtn.Verify"),
                CanVerify = provenance.CanReverify,
                VerifyToolTip = provenance.CanReverify
                    ? "Re-hash the installed runtime files and compare them with the installation manifest."
                    : provenance.IsManaged
                        ? "Reinstall this managed runtime to create a file manifest before re-verification."
                        : "Custom runtimes are user-trusted and do not have a Manager verification manifest.",
                BuildToolTip = availability.IsAvailable
                    ? "This source has already been built."
                    : "The built runtime executable is missing. Rebuild or reinstall it.",
                CanDelete = !isActiveRuntime,
                DeleteToolTip = RuntimeDeleteToolTip(isActiveRuntime, modelNames),
                Runtime = runtime
            });
        }

        foreach (var source in sources.Where(source => !HasBuiltRuntimeForSource(source, runtimes)))
        {
            rows.Add(new RuntimeCatalogRow
            {
                Kind = RuntimeCatalogRowKind.Source,
                Name = source.Label,
                Backend = RuntimeBuildCatalogService.BackendLabel(source),
                State = "Downloaded",
                Location = source.SourceDir,
                Details = $"Downloaded source at {RuntimeMetadataService.ShortCommit(source.Commit)}. Build it before using it to launch models.",
                Vendor = RuntimeInventoryFilterService.Vendor(RuntimeBuildCatalogService.BuildBackend(source)),
                Platform = RuntimeInventoryFilterService.Platform(RuntimeBuildCatalogService.BuildMode(source)),
                BuildAction = "Build",
                BuildToolTip = "Build this downloaded llama.cpp source into a usable runtime.",
                CanBuild = true,
                CanDelete = true,
                DeleteToolTip = "Delete this downloaded runtime source.",
                Source = source
            });
        }

        return rows;
    }

    public IReadOnlyList<RuntimePackagePresetRow> BuildPackageRows(
        IReadOnlyList<RuntimePackagePreset> presets,
        IReadOnlyList<RuntimeBuildPreset> buildPresets,
        IReadOnlyList<RuntimeRecord> runtimes,
        IReadOnlyList<RuntimeSourceEntry> sources,
        IReadOnlyDictionary<string, RuntimeUpdateState> runtimeUpdateStates,
        IReadOnlyDictionary<string, RuntimePackageUpdateState> updateStates)
    {
        var rows = new List<RuntimePackagePresetRow>();
        var buildPresetsById = buildPresets.ToDictionary(preset => preset.Id, StringComparer.OrdinalIgnoreCase);
        var attachedBuildPresetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var preset in presets)
        {
            var row = _packageStatus.CreateRow(preset, _packageStatus.BuildInventory(preset, runtimes, updateStates));
            row.DeleteKind = row.CanDelete ? RuntimeDownloadDeleteKind.Package : RuntimeDownloadDeleteKind.None;
            row.Vendor = RuntimeInventoryFilterService.Vendor(preset.Backend);
            row.Platform = RuntimeInventoryFilterService.Platform(preset.Mode);
            if (buildPresetsById.TryGetValue(preset.SourcePresetId, out var sourcePreset))
            {
                attachedBuildPresetIds.Add(sourcePreset.Id);
                ApplySourceAction(row, sourcePreset, runtimes, sources, runtimeUpdateStates);
            }
            rows.Add(row);
        }

        foreach (var sourcePreset in buildPresets.Where(preset => !attachedBuildPresetIds.Contains(preset.Id)))
            rows.Add(CreateSourceOnlyRow(sourcePreset, runtimes, sources, runtimeUpdateStates));

        rows.Add(new RuntimePackagePresetRow
        {
            Label = "Add custom source repository",
            LocalStatus = "Custom",
            BuildSourceAction = "Add",
            BuildSourceToolTip = "Add a custom llama.cpp Git repository preset.",
            CanBuildSource = true,
            Vendor = RuntimeInventoryFilterService.All,
            Platform = RuntimeInventoryFilterService.All,
            SourceActionKind = RuntimeSourceRowActionKind.Add
        });
        return rows;
    }

    private static RuntimePackagePresetRow CreateSourceOnlyRow(
        RuntimeBuildPreset preset,
        IReadOnlyList<RuntimeRecord> runtimes,
        IReadOnlyList<RuntimeSourceEntry> sources,
        IReadOnlyDictionary<string, RuntimeUpdateState> updateStates)
    {
        var local = RuntimeCatalogDataService.BuildPresetLocalState(preset, runtimes, sources, updateStates);
        var row = new RuntimePackagePresetRow
        {
            Label = preset.Label,
            Backend = RuntimeBuildCatalogService.BackendLabel(preset),
            LocalStatus = RuntimeBuildCatalogService.LocalStatusLabel(local.DownloadedSources, local.InstalledRuntimes, local.CommitUnavailable),
            LatestRelease = RuntimeBuildCatalogService.LatestLocalCommitLabel(local.DownloadedSources, local.InstalledRuntimes),
            Assets = preset.RepoUrl,
            InstallAction = "Unavailable",
            InstallToolTip = "This repository provides source builds only.",
            CanInstall = false,
            CheckAction = "",
            CanCheck = false,
            DeleteAction = preset.Custom && local.LocalCount == 0 ? "Remove" : "Delete All",
            DeleteToolTip = preset.Custom && local.LocalCount == 0
                ? "Remove this custom repository preset."
                : "Delete local sources and built runtimes for this preset.",
            CanDelete = preset.Custom || local.LocalCount > 0,
            DeleteKind = RuntimeDownloadDeleteKind.Source,
            Vendor = RuntimeInventoryFilterService.Vendor(RuntimeBuildCatalogService.BuildBackend(preset)),
            Platform = RuntimeInventoryFilterService.Platform(RuntimeBuildCatalogService.BuildMode(preset)),
            SourcePreset = preset
        };
        ApplySourceAction(row, preset, runtimes, sources, updateStates);
        return row;
    }

    private static void ApplySourceAction(
        RuntimePackagePresetRow row,
        RuntimeBuildPreset preset,
        IReadOnlyList<RuntimeRecord> runtimes,
        IReadOnlyList<RuntimeSourceEntry> sources,
        IReadOnlyDictionary<string, RuntimeUpdateState> updateStates)
    {
        var local = RuntimeCatalogDataService.BuildPresetLocalState(preset, runtimes, sources, updateStates);
        row.SourcePreset = preset;
        row.DownloadedSource = local.DownloadedSources.FirstOrDefault();
        row.CanBuildSource = true;
        if (local.LocalCount > 0)
        {
            row.DeleteKind = RuntimeDownloadDeleteKind.Source;
            row.CanDelete = true;
            row.DeleteAction = "Delete Source";
            row.DeleteToolTip = "Delete the downloaded source and source-built runtimes for this row. Prebuilt installs are kept.";
        }
        if (row.DownloadedSource is not null)
        {
            row.SourceActionKind = RuntimeSourceRowActionKind.Build;
            row.BuildSourceAction = "Build";
            row.BuildSourceToolTip = "Build the downloaded source. The source folder is deleted automatically after a successful build.";
            return;
        }

        if (local.CanDownload)
        {
            row.SourceActionKind = RuntimeSourceRowActionKind.Download;
            row.BuildSourceAction = "Download";
            row.BuildSourceToolTip = "Download the source revision found by the most recent check.";
            return;
        }

        row.SourceActionKind = RuntimeSourceRowActionKind.Check;
        row.BuildSourceAction = "Check";
        row.BuildSourceToolTip = local.CommitUnavailable
            ? "The local commit is unavailable. Delete the local source/build before checking again."
            : "Check the source repository before downloading or updating it.";
        row.CanBuildSource = !local.CommitUnavailable;
    }

    public static IReadOnlyList<RuntimeBuildPresetRow> BuildPresetRows(
        IReadOnlyList<RuntimeBuildPreset> presets,
        IReadOnlyList<RuntimeRecord> runtimes,
        IReadOnlyList<RuntimeSourceEntry> sources,
        IReadOnlyDictionary<string, RuntimeUpdateState> updateStates)
    {
        var rows = new List<RuntimeBuildPresetRow>();
        foreach (var preset in presets)
        {
            var local = RuntimeCatalogDataService.BuildPresetLocalState(preset, runtimes, sources, updateStates);
            var latestLocal = RuntimeBuildCatalogService.LatestLocalCommitLabel(local.DownloadedSources, local.InstalledRuntimes);
            if (local.UpdateState is not null)
            {
                latestLocal = local.UpdateState.HasUpdate
                    ? $"update available {RuntimeMetadataService.DisplayCommit(local.LocalCommit)} -> {RuntimeMetadataService.DisplayCommit(local.UpdateState.RemoteCommit)}"
                    : $"current {RuntimeMetadataService.DisplayCommit(local.LocalCommit)} - checked {local.UpdateState.CheckedAt.ToLocalTime():g}";
            }

            rows.Add(new RuntimeBuildPresetRow
            {
                Label = preset.Label,
                Backend = RuntimeBuildCatalogService.BackendLabel(preset),
                LocalStatus = RuntimeBuildCatalogService.LocalStatusLabel(local.DownloadedSources, local.InstalledRuntimes, local.CommitUnavailable),
                LatestLocal = latestLocal,
                Source = preset.RepoUrl,
                DownloadAction = local.DownloadAction,
                CheckAction = "Check",
                DeleteAction = preset.Custom && local.LocalCount == 0 ? "Remove" : "Delete All",
                DownloadToolTip = local.CanDownload
                    ? "Download or refresh this llama.cpp source preset."
                    : "This preset is already downloaded or installed.",
                CheckToolTip = local.LocalCount > 0
                    ? "Check the remote repository for newer commits."
                    : "Download or build this preset before checking for updates.",
                DeleteToolTip = preset.Custom && local.LocalCount == 0
                    ? "Remove this custom repository preset."
                    : "Delete local sources and built runtimes for this preset.",
                CanDownload = local.CanDownload,
                CanCheck = local.LocalCount > 0,
                CanDelete = preset.Custom || local.LocalCount > 0,
                Preset = preset
            });
        }

        rows.Add(new RuntimeBuildPresetRow
        {
            Backend = "CPU Windows",
            LocalStatus = "Custom",
            DownloadAction = "Add",
            DownloadToolTip = "Add a custom llama.cpp Git repository preset.",
            CanDownload = true,
            IsCustomAdd = true
        });

        return rows;
    }

    private static string RuntimeDeleteToolTip(bool isActiveRuntime, IReadOnlyList<string> modelNames)
    {
        var modelList = string.Join(", ", modelNames);
        if (isActiveRuntime && modelNames.Count > 0)
            return $"Unload the running model before deleting this runtime. Saved model profiles using it: {modelList}.";
        if (isActiveRuntime)
            return "Unload the running model before deleting this runtime.";
        if (modelNames.Count > 0)
            return $"Delete this runtime and move saved launch settings that use it to another registered runtime. Used by: {modelList}.";
        return "Delete this runtime registration and local build files.";
    }

    private static string RuntimeDetails(
        RuntimeAvailability availability,
        RuntimeProvenance provenance,
        IReadOnlyList<string> modelNames)
    {
        var lines = new List<string> { provenance.Details };
        if (!availability.IsAvailable)
            lines.Add($"{availability.Reason} Repair or reinstall this runtime before loading a model.");
        else if (modelNames.Count == 0)
            lines.Add("No saved model launch settings use this runtime.");
        else
            lines.Add("Models using this runtime:" + Environment.NewLine + string.Join(Environment.NewLine, modelNames.Select(model => $"- {model}")));
        return string.Join(Environment.NewLine + Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    public static bool HasBuiltRuntimeForSource(RuntimeSourceEntry source, IReadOnlyList<RuntimeRecord> runtimes)
        => !string.IsNullOrWhiteSpace(source.Commit)
            && runtimes.Any(runtime => RuntimeAvailabilityService.IsAvailable(runtime)
                && string.Equals(RuntimeMetadataService.ManagedPresetId(runtime), source.PresetId, StringComparison.OrdinalIgnoreCase)
                && RuntimeMetadataService.CommitsMatch(RuntimeMetadataService.Commit(runtime), source.Commit));

}
