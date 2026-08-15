using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using LocalLlmConsole.Localization;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void RuntimeMetadataServiceReadsManagedRuntimeMetadataAndCommits()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtime", "bin");
        Directory.CreateDirectory(runtimeRoot);
        var runtime = new RuntimeRecord(
            "runtime-1",
            "llama.cpp CUDA",
            RuntimeMode.Wsl,
            RuntimeBackend.Cuda,
            Path.Combine(runtimeRoot, "llama-server"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = Path.Combine(root, "runtime"),
                runtimeMetadata = new
                {
                    repoUrl = "https://github.com/ggml-org/llama.cpp",
                    commit = "abcdef1234567890",
                    assets = new[]
                    {
                        new { name = "llama-b9354-bin-win-cuda-13.1-x64.zip" },
                        new { name = "cudart-llama-bin-win-cuda-13.1-x64.zip" }
                    }
                }
            }),
            DateTimeOffset.UtcNow);
        var sourceDir = Path.Combine(root, "source");
        var refDir = Path.Combine(sourceDir, ".git", "refs", "heads");
        Directory.CreateDirectory(refDir);
        File.WriteAllText(Path.Combine(sourceDir, ".git", "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(refDir, "main"), "fedcba9876543210");

        Assert.Equal("official-cuda", RuntimeMetadataService.ManagedPresetId(runtime));
        Assert.Equal("official-vulkan", RuntimeMetadataService.ManagedPresetId(runtime with { Name = "llama.cpp Vulkan", Backend = RuntimeBackend.Vulkan }));
        Assert.Equal("official-sycl", RuntimeMetadataService.ManagedPresetId(runtime with { Name = "llama.cpp SYCL", Backend = RuntimeBackend.Sycl }));
        Assert.Equal("official-windows-cuda", RuntimeMetadataService.ManagedPresetId(runtime with { Mode = RuntimeMode.Native, ExecutablePath = Path.Combine(runtimeRoot, "llama-server.exe") }));
        Assert.Equal("official-windows-sycl", RuntimeMetadataService.ManagedPresetId(runtime with { Mode = RuntimeMode.Native, Backend = RuntimeBackend.Sycl, ExecutablePath = Path.Combine(runtimeRoot, "llama-server.exe") }));
        Assert.Equal("atomic-turboquant-cuda", RuntimeMetadataService.ManagedPresetId(runtime with { Name = "Atomic llama.cpp", MetadataJson = runtime.MetadataJson.Replace("ggml-org/llama.cpp", "AtomicBot-ai/atomic-llama-cpp-turboquant", StringComparison.Ordinal) }));
        Assert.Equal("atomic-windows-turboquant-cuda", RuntimeMetadataService.ManagedPresetId(runtime with { Name = "Atomic llama.cpp", Mode = RuntimeMode.Native, ExecutablePath = Path.Combine(runtimeRoot, "llama-server.exe"), MetadataJson = runtime.MetadataJson.Replace("ggml-org/llama.cpp", "AtomicBot-ai/atomic-llama-cpp-turboquant", StringComparison.Ordinal) }));
        Assert.Equal(Path.Combine(root, "runtime"), RuntimeMetadataService.Folder(runtime));
        Assert.Equal("abcdef1234567890", RuntimeMetadataService.Commit(runtime));
        Assert.Equal("llama-b9354-bin-win-cuda-13.1-x64.zip, cudart-llama-bin-win-cuda-13.1-x64.zip", RuntimeMetadataService.PackageAssetSummary(runtime));
        Assert.True(RuntimeMetadataService.CommitsMatch("abcdef12", "abcdef1234567890"));
        Assert.Equal("abcdef123456", RuntimeMetadataService.ShortCommit("abcdef1234567890"));
        Assert.Equal("commit unavailable", RuntimeMetadataService.DisplayCommit(""));
        Assert.Equal("fedcba9876543210", RuntimeMetadataService.TryReadGitHeadCommit(sourceDir));
        Assert.Equal("123456789abcdef", RuntimeMetadataService.InferCommitFromText("build-123456789abcdef-path"));
    }

    [Fact]
    public async Task RuntimeBuildCatalogServicePersistsCustomPresetsAndReadsSources()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var custom = new RuntimeBuildPreset("", "My Runtime", "https://example.com/runtime.git", "main", true, Custom: true);

        await RuntimeBuildCatalogService.SaveCustomPresetsAsync(runtimeRoot, [custom], TestContext.Current.CancellationToken);
        var loaded = Assert.Single(RuntimeBuildCatalogService.ReadCustomPresets(runtimeRoot));
        var sourceDir = RuntimeBuildCatalogService.SourceDir(runtimeRoot, loaded);
        Directory.CreateDirectory(Path.Combine(sourceDir, ".git"));
        File.WriteAllText(Path.Combine(sourceDir, ".git", "HEAD"), "abc123def4567890");
        var source = new RuntimeSourceEntry(loaded.Id, loaded.Label, loaded.RepoUrl, loaded.Branch, loaded.Cuda, sourceDir, "unknown", DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(
            RuntimeBuildCatalogService.SourceMetadataPath(sourceDir),
            System.Text.Json.JsonSerializer.Serialize(source),
            TestContext.Current.CancellationToken);

        var sources = RuntimeBuildCatalogService.Sources(runtimeRoot).ToList();
        var rows = RuntimeBuildCatalogService.PresetRows(runtimeRoot);

        Assert.True(loaded.Custom);
        Assert.Equal(RuntimeMode.Wsl, RuntimeBuildCatalogService.BuildMode(loaded));
        Assert.StartsWith("custom-my-runtime-cuda-", loaded.Id, StringComparison.Ordinal);
        Assert.Equal(
            ["official-windows-cuda", "official-cuda", "official-windows-vulkan", "official-vulkan", "official-windows-sycl", "official-sycl"],
            rows.Take(6).Select(preset => preset.Id).ToArray());
        Assert.Contains(rows, preset => preset.Id == "official-cuda");
        Assert.Contains(rows, preset => preset.Id == "official-vulkan" && RuntimeBuildCatalogService.BuildBackend(preset) == RuntimeBackend.Vulkan);
        Assert.Contains(rows, preset => preset.Id == "official-sycl" && RuntimeBuildCatalogService.BuildBackend(preset) == RuntimeBackend.Sycl);
        Assert.Contains(rows, preset => preset.Id == "official-windows-cpu" && RuntimeBuildCatalogService.BuildMode(preset) == RuntimeMode.Native);
        Assert.Contains(rows, preset => preset.Id == "official-windows-cuda" && RuntimeBuildCatalogService.BuildMode(preset) == RuntimeMode.Native);
        Assert.Contains(rows, preset => preset.Id == "official-windows-vulkan" && RuntimeBuildCatalogService.BuildBackend(preset) == RuntimeBackend.Vulkan && RuntimeBuildCatalogService.BuildMode(preset) == RuntimeMode.Native);
        Assert.Contains(rows, preset => preset.Id == "official-windows-sycl" && RuntimeBuildCatalogService.BuildBackend(preset) == RuntimeBackend.Sycl && RuntimeBuildCatalogService.BuildMode(preset) == RuntimeMode.Native);
        Assert.Equal("Vulkan WSL", RuntimeBuildCatalogService.BackendLabel(rows.Single(preset => preset.Id == "official-vulkan")));
        Assert.Equal("Vulkan Windows", RuntimeBuildCatalogService.BackendLabel(rows.Single(preset => preset.Id == "official-windows-vulkan")));
        Assert.Equal("SYCL WSL", RuntimeBuildCatalogService.BackendLabel(rows.Single(preset => preset.Id == "official-sycl")));
        Assert.Equal("SYCL Windows", RuntimeBuildCatalogService.BackendLabel(rows.Single(preset => preset.Id == "official-windows-sycl")));
        Assert.Contains(rows, preset => preset.Id == loaded.Id);
        Assert.Equal("abc123def4567890", RuntimeBuildCatalogService.SourceCommit(Assert.Single(sources)));
        Assert.StartsWith("custom-my-runtime-windows-cuda-", RuntimeBuildCatalogService.CustomPresetId("My Runtime", "https://example.com/runtime.git", "main", "cuda", RuntimeMode.Native), StringComparison.Ordinal);
        Assert.True(RuntimeBuildCatalogService.IsAllowedGitSource("https://example.com/repo.git"));
        Assert.True(RuntimeBuildCatalogService.IsAllowedGitSource("ssh://git@example.com/repo.git"));
        Assert.True(RuntimeBuildCatalogService.IsAllowedGitSource(Path.GetTempPath()));
        Assert.False(RuntimeBuildCatalogService.IsAllowedGitSource("http://example.com/repo.git"));
        Assert.True(RuntimeBuildCatalogService.IsHttpsGitSource("https://example.com/repo.git"));
        Assert.False(RuntimeBuildCatalogService.IsHttpsGitSource("https://user:token@example.com/repo.git"));
        Assert.False(RuntimeBuildCatalogService.IsHttpsGitSource("ssh://git@example.com/repo.git"));
        Assert.False(RuntimeBuildCatalogService.IsHttpsGitSource(Path.GetTempPath()));
        Assert.True(RuntimeBuildCatalogService.IsSafeUiCustomPreset(custom));
        Assert.False(RuntimeBuildCatalogService.IsSafeUiCustomPreset(custom with { RepoUrl = "ssh://git@example.com/repo.git" }));
        Assert.True(RuntimeBuildCatalogService.IsSafeGitRefName("feature/runtime-build"));
        Assert.False(RuntimeBuildCatalogService.IsSafeGitRefName("bad branch"));
        Assert.Equal(["refs/heads/main", "main"], RuntimeBuildCatalogService.RemoteRefs(loaded));
        Assert.Equal("abcdef123", RuntimeBuildCatalogService.FirstLsRemoteCommit("abcdef123\trefs/heads/main\n"));
        Assert.StartsWith("custom-my-runtime-windows-sycl-", RuntimeBuildCatalogService.CustomPresetId("My Runtime", "https://example.com/runtime.git", "main", "sycl", RuntimeMode.Native), StringComparison.Ordinal);

        var legacySourcePath = Path.Combine(runtimeRoot, "runtime-sources", "legacy", "local-llm-runtime-source.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacySourcePath)!);
        await File.WriteAllTextAsync(
            legacySourcePath,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                PresetId = "legacy",
                Label = "Legacy",
                RepoUrl = "https://example.com/legacy.git",
                Branch = "main",
                Cuda = false,
                SourceDir = Path.GetDirectoryName(legacySourcePath),
                Commit = "abc",
                DownloadedAt = DateTimeOffset.UtcNow,
                Backend = "cpu"
            }),
            TestContext.Current.CancellationToken);
        var legacySource = RuntimeBuildCatalogService.Sources(runtimeRoot).Single(source => source.PresetId == "legacy");
        Assert.Equal(RuntimeMode.Wsl, RuntimeBuildCatalogService.BuildMode(legacySource));
    }


    [Fact]
    public async Task RuntimeCatalogDataServiceOwnsCatalogSnapshotAndPresetLocalState()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var now = DateTimeOffset.UtcNow;
        var service = new RuntimeCatalogDataService();
        var preset = RuntimeBuildCatalogService.DefaultPresets.Single(candidate => candidate.Id == "official-cuda");
        var sourceDir = RuntimeBuildCatalogService.SourceDir(runtimeRoot, preset);
        Directory.CreateDirectory(sourceDir);
        var source = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, sourceDir, "abcdef1234567890", now);
        await File.WriteAllTextAsync(
            RuntimeBuildCatalogService.SourceMetadataPath(sourceDir),
            System.Text.Json.JsonSerializer.Serialize(source),
            TestContext.Current.CancellationToken);
        var olderRuntime = new RuntimeRecord(
            "runtime-old",
            "llama.cpp CUDA",
            RuntimeMode.Wsl,
            RuntimeBackend.Cuda,
            Path.Combine(runtimeRoot, "official-cuda", "bin", "llama-server"),
            System.Text.Json.JsonSerializer.Serialize(new { folder = Path.Combine(runtimeRoot, "official-cuda"), runtimeMetadata = new { managedPresetId = preset.Id, commit = "older" } }),
            now.AddMinutes(-10));
        CreateRuntimeExecutable(runtimeRoot, "official-cuda", "bin", "llama-server");
        var newerRuntime = olderRuntime with { Id = "runtime-new", UpdatedAt = now };
        var updateState = new RuntimeUpdateState(true, source.Commit, "abcdef9999999999", now);
        var staleState = updateState with { LocalCommit = "0000000" };

        var loadedSources = await service.LoadSourcesAsync(runtimeRoot, TestContext.Current.CancellationToken);
        var local = RuntimeCatalogDataService.BuildPresetLocalState(
            preset,
            [olderRuntime, newerRuntime],
            loadedSources,
            new Dictionary<string, RuntimeUpdateState> { [preset.Id] = updateState });
        var staleLocal = RuntimeCatalogDataService.BuildPresetLocalState(
            preset,
            [newerRuntime],
            loadedSources,
            new Dictionary<string, RuntimeUpdateState> { [preset.Id] = staleState });
        var snapshot = service.BuildViewRequest(new RuntimeCatalogDataRequest(
            runtimeRoot,
            [newerRuntime],
            loadedSources,
            new Dictionary<string, List<string>> { [newerRuntime.Id] = ["Qwen Test"] },
            [
                new LoadedModelSessionSnapshot(
                    "session",
                    "model",
                    "Qwen Test",
                    newerRuntime.Id,
                    newerRuntime.Name,
                    newerRuntime.Mode,
                    newerRuntime.Backend,
                    AppSettings.CreateDefault(root),
                    Path.Combine(root, "runtime.log"),
                    now,
                    "",
                    0,
                    LoadedModelSessionStatus.Running,
                    true,
                    true)
            ],
            new Dictionary<string, RuntimeUpdateState> { [preset.Id] = updateState },
            new Dictionary<string, RuntimePackageUpdateState>()));

        Assert.Single(loadedSources);
        Assert.Equal(source.Commit, local.LocalCommit);
        Assert.Equal(updateState, local.UpdateState);
        Assert.False(local.CanDownload);
        Assert.Equal("Downloaded", local.DownloadAction);
        Assert.Single(local.InstalledRuntimes);
        Assert.Equal("runtime-new", local.InstalledRuntimes[0].Id);
        Assert.Null(staleLocal.UpdateState);
        Assert.False(staleLocal.CanDownload);
        Assert.Contains(snapshot.BuildPresets, candidate => candidate.Id == preset.Id);
        Assert.NotEmpty(snapshot.PackagePresets);
        Assert.Contains(newerRuntime.Id, snapshot.ActiveRuntimeIds);
    }

    [Fact]
    public async Task RuntimeCatalogApplicationServiceOwnsRefreshScanAndRegistrationDelete()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var sessions = CreateLoadedModelSessionManager();
        var registry = new RuntimeRegistryService(store);
        var launchProfiles = new ModelLaunchProfileService(store, sessions);
        var deletion = new RuntimeDeletionPlanner(store, launchProfiles, sessions);
        var data = new RuntimeCatalogDataService();
        var service = new RuntimeCatalogApplicationService(
            store,
            registry,
            deletion,
            data,
            new RuntimeCatalogViewService(new RuntimePackageStatusService()));
        var runtimeFolder = Path.Combine(settings.RuntimeRoot, "manual-runtime");
        var runtime = new RuntimeRecord(
            "manual-runtime",
            "Manual Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(runtimeFolder, "llama-server.exe"),
            System.Text.Json.JsonSerializer.Serialize(new { folder = runtimeFolder }),
            DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);

        var refresh = await service.RefreshAsync(new RuntimeCatalogRefreshApplicationRequest(
            settings,
            [],
            new Dictionary<string, RuntimeUpdateState>(),
            new Dictionary<string, RuntimePackageUpdateState>()));

        var statuses = new List<string>();
        var runtimeRefreshes = 0;
        var overviewRefreshes = 0;
        RuntimeCatalogDeleteRegistrationActions DeleteActions(bool confirm) => new(
            _ => confirm,
            () =>
            {
                runtimeRefreshes++;
                return Task.CompletedTask;
            },
            () =>
            {
                overviewRefreshes++;
                return Task.CompletedTask;
            });

        var noSelection = await service.DeleteRegistrationAsync(null, DeleteActions(confirm: true));
        var cancelled = await service.DeleteRegistrationAsync(runtime, DeleteActions(confirm: false));
        var stillRegistered = await store.ListRuntimesAsync();
        var deleted = await service.DeleteRegistrationAsync(runtime, DeleteActions(confirm: true));
        var afterDelete = await store.ListRuntimesAsync();
        var scanState = new RuntimeCatalogSessionState();
        Directory.CreateDirectory(runtimeFolder);
        await File.WriteAllTextAsync(Path.Combine(runtimeFolder, "llama-server.exe"), "", TestContext.Current.CancellationToken);
        var busyMessages = new List<string>();
        var scanRefreshes = new List<string>();
        await service.DetectAndRefreshAsync(
            settings,
            scanState,
            new RuntimeCatalogScanApplicationActions(
                async (message, action) =>
                {
                    busyMessages.Add(message);
                    await action();
                },
                () =>
                {
                    scanRefreshes.Add("runtimes");
                    return Task.CompletedTask;
                },
                () =>
                {
                    scanRefreshes.Add("jobs");
                    return Task.CompletedTask;
                },
                () =>
                {
                    scanRefreshes.Add("overview");
                    return Task.CompletedTask;
                }));

        Assert.Contains(refresh.Runtimes, candidate => candidate.Id == runtime.Id);
        Assert.Contains(refresh.Rows.Runtimes, row => row.Runtime?.Id == runtime.Id);
        Assert.NotEmpty(refresh.Rows.BuildPresets);
        Assert.NotEmpty(refresh.Rows.PackagePresets);
        Assert.Equal(RuntimeCatalogApplicationOutcome.NoSelection, noSelection);
        Assert.Equal(RuntimeCatalogApplicationOutcome.Cancelled, cancelled);
        Assert.Contains(stillRegistered, candidate => candidate.Id == runtime.Id);
        Assert.Equal(RuntimeCatalogApplicationOutcome.Applied, deleted);
        Assert.DoesNotContain(afterDelete, candidate => candidate.Id == runtime.Id);
        Assert.Equal(1, runtimeRefreshes);
        Assert.Equal(1, overviewRefreshes);
        Assert.Equal(["Detecting installed runtimes..."], busyMessages);
        Assert.Equal(["runtimes", "jobs", "overview"], scanRefreshes);
        Assert.False(scanState.TryMarkRuntimeRootScanned(settings.RuntimeRoot, out _));
        Assert.Empty(statuses);
    }


    [Fact]
    public async Task RuntimeCatalogCommandApplicationServiceOwnsPreferenceAndCustomRepositoryWorkflows()
    {
        Loc.LoadLanguage("en");
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { CudaPackagePreference = "latest" };
        var service = new RuntimeCatalogCommandApplicationService(new RuntimeCustomRepositoryService());
        var calls = new List<string>();

        RuntimeCatalogPreferenceApplicationActions PreferenceActions()
            => new(
                updated =>
                {
                    calls.Add($"persist:{updated.CudaPackagePreference}");
                    return Task.FromResult(updated);
                },
                () => calls.Add("clear-package-states"),
                () =>
                {
                    calls.Add("refresh-runtimes");
                    return Task.CompletedTask;
                },
                status => calls.Add($"status:{status}"));

        var unchanged = await service.ChangeCudaPackagePreferenceAsync(settings, "Latest", PreferenceActions());

        Assert.Equal(RuntimeCatalogCommandOutcome.Unchanged, unchanged.Outcome);
        Assert.Empty(calls);

        var changed = await service.ChangeCudaPackagePreferenceAsync(settings, "Compatibility", PreferenceActions());

        Assert.Equal(RuntimeCatalogCommandOutcome.Applied, changed.Outcome);
        Assert.Equal("compatibility", changed.Settings.CudaPackagePreference);
        Assert.Equal([
            "clear-package-states",
            "persist:compatibility",
            "refresh-runtimes",
            $"status:CUDA downloads set to {Loc.T("Pref.Compatibility")}."
        ], calls);

        calls.Clear();
        RuntimeCatalogCustomRepositoryApplicationActions CustomRepositoryActions()
            => new(
                () =>
                {
                    calls.Add("refresh-runtimes");
                    return Task.CompletedTask;
                },
                status => calls.Add($"status:{status}"),
                failure => calls.Add($"failure:{failure}"));

        var validDraft = new RuntimeCustomRepositoryDraft("My Runtime", "https://example.com/runtime.git", "main", "CUDA WSL");
        var cancelled = await service.AddCustomRepositoryAsync(settings.RuntimeRoot, null, CustomRepositoryActions(), TestContext.Current.CancellationToken);
        var invalid = await service.AddCustomRepositoryAsync(settings.RuntimeRoot, validDraft with { Label = "" }, CustomRepositoryActions(), TestContext.Current.CancellationToken);
        var added = await service.AddCustomRepositoryAsync(settings.RuntimeRoot, validDraft, CustomRepositoryActions(), TestContext.Current.CancellationToken);
        var duplicate = await service.AddCustomRepositoryAsync(settings.RuntimeRoot, validDraft, CustomRepositoryActions(), TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeCatalogCommandOutcome.Cancelled, cancelled.Outcome);
        Assert.Equal(RuntimeCatalogCommandOutcome.Failed, invalid.Outcome);
        Assert.Equal(RuntimeCatalogCommandOutcome.Applied, added.Outcome);
        Assert.Equal(RuntimeCatalogCommandOutcome.Failed, duplicate.Outcome);
        Assert.Contains(calls, call => call.Contains("failure:Enter a display name", StringComparison.Ordinal));
        Assert.Contains("status:Added custom runtime repository: My Runtime", calls);
        Assert.Contains(calls, call => call.Contains("failure:That repository is already listed as My Runtime.", StringComparison.Ordinal));
        Assert.Equal(1, calls.Count(call => call == "refresh-runtimes"));
        Assert.True(calls.IndexOf("refresh-runtimes") < calls.IndexOf("status:Added custom runtime repository: My Runtime"));
    }


    [Fact]
    public void RuntimeCatalogSessionStateOwnsMainWindowCatalogBookkeeping()
    {
        var mainWindow = ReadMainWindowSources();
        var stateSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeCatalogSessionState.cs"));
        var runtimeCatalogApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeCatalogApplicationService.cs"));
        var runtimePackageApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimePackageApplicationService.cs"));
        var runtimeSourceApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeSourceApplicationService.cs"));
        var state = new RuntimeCatalogSessionState();
        var runtimeRoot = Path.Combine(CreateTempRoot(), "runtimes");
        var now = DateTimeOffset.UtcNow;
        var runtimeUpdate = new RuntimeUpdateState(true, "abcdef1234567890", "abcdef9999999999", now);
        var packageUpdate = new RuntimePackageUpdateState(true, "b9354", "b9355", "https://example.com/release", "llama-b9355.zip", now);

        Assert.True(state.TryMarkRuntimeRootScanned(runtimeRoot, out var fullPath));
        Assert.Equal(Path.GetFullPath(runtimeRoot), fullPath);
        Assert.False(state.TryMarkRuntimeRootScanned(runtimeRoot, out _));

        Assert.Equal(runtimeUpdate, state.SetRuntimeUpdateState("preset", runtimeUpdate));
        Assert.True(state.RuntimeUpdateStates.TryGetValue("PRESET", out var readRuntimeUpdate));
        Assert.Equal(runtimeUpdate, readRuntimeUpdate);

        Assert.Equal(packageUpdate, state.SetRuntimePackageUpdateState("package", packageUpdate));
        Assert.True(state.RuntimePackageUpdateStates.TryGetValue("PACKAGE", out var readPackageUpdate));
        Assert.Equal(packageUpdate, readPackageUpdate);
        Assert.True(state.RemoveRuntimePackageUpdateState("package"));
        Assert.Empty(state.RuntimePackageUpdateStates);

        state.SetRuntimePackageUpdateState("package", packageUpdate);
        state.ClearRuntimePackageUpdateStates();
        Assert.Empty(state.RuntimePackageUpdateStates);

        Assert.Contains("private readonly RuntimeCatalogSessionState _runtimeCatalogState;", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_runtimeCatalogState = uiState.RuntimeCatalogState", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly RuntimeCatalogSessionState _runtimeCatalogState = new();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("sessionState.TryMarkRuntimeRootScanned", runtimeCatalogApplication, StringComparison.Ordinal);
        Assert.Contains("sessionState.MarkRuntimeRootScanned", runtimeCatalogApplication, StringComparison.Ordinal);
        Assert.Contains("_runtimeCatalogState.RuntimeUpdateStates", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_runtimeCatalogState.RuntimePackageUpdateStates", mainWindow, StringComparison.Ordinal);
        Assert.Contains("sessionState.SetRuntimeUpdateState", runtimeSourceApplication, StringComparison.Ordinal);
        Assert.Contains("sessionState.SetRuntimePackageUpdateState", runtimePackageApplication, StringComparison.Ordinal);
        Assert.Contains("_runtimeCatalogState.ClearRuntimePackageUpdateStates", mainWindow, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyDictionary<string, RuntimeUpdateState> RuntimeUpdateStates", stateSource, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyDictionary<string, RuntimePackageUpdateState> RuntimePackageUpdateStates", stateSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_autoScannedRuntimeRoots", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimePackageUpdateStates", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeUpdateStates", mainWindow, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeCustomRepositoryServiceValidatesSavesAndRejectsDuplicates()
    {
        var root = CreateTempRoot();
        var runtimeRoot = AppSettings.CreateDefault(root).RuntimeRoot;
        var service = new RuntimeCustomRepositoryService();
        var validDraft = new RuntimeCustomRepositoryDraft("My Runtime", "https://example.com/runtime.git", "feature/runtime", "SYCL Windows");

        var invalidName = service.BuildPreset(validDraft with { Label = "" });
        var invalidRepo = service.BuildPreset(validDraft with { RepoUrl = "ssh://git@example.com/runtime.git" });
        var built = service.BuildPreset(validDraft);
        var added = await service.AddAsync(runtimeRoot, validDraft, TestContext.Current.CancellationToken);
        var duplicate = await service.AddAsync(runtimeRoot, validDraft, TestContext.Current.CancellationToken);
        var saved = Assert.Single(RuntimeBuildCatalogService.ReadCustomPresets(runtimeRoot));

        Assert.False(invalidName.Success);
        Assert.Contains("display name", invalidName.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(invalidRepo.Success);
        Assert.Contains("HTTPS", invalidRepo.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(built.Success);
        Assert.Equal(RuntimeBackend.Sycl, RuntimeBuildCatalogService.BuildBackend(built.Preset!));
        Assert.Equal(RuntimeMode.Native, RuntimeBuildCatalogService.BuildMode(built.Preset!));
        Assert.StartsWith("custom-my-runtime-windows-sycl-", built.Preset!.Id, StringComparison.Ordinal);
        Assert.True(added.Success);
        Assert.Contains("Added custom runtime repository", added.StatusMessage, StringComparison.Ordinal);
        Assert.False(duplicate.Success);
        Assert.Equal(saved.Id, duplicate.ExistingPreset?.Id);
        Assert.Equal("feature/runtime", saved.Branch);
        Assert.True(saved.Custom);
    }


    [Fact]
    public void RuntimeCustomRepositoryDialogLivesOutsideMainWindow()
    {
        var source = ReadMainWindowSources();
        var runtimeCatalog = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimeCatalog.cs"));
        var dialogFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Runtimes", "RuntimeCustomRepositoryDialogFactory.cs"));

        Assert.Contains("RuntimeCustomRepositoryDialogFactory.Show", runtimeCatalog, StringComparison.Ordinal);
        Assert.Contains("ValidateDraft", dialogFactory, StringComparison.Ordinal);
        Assert.Contains("ShowValidationWarning", dialogFactory, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.Dialogs.Notify(owner, message, \"Custom repository\", MessageBoxImage.Warning)", runtimeCatalog, StringComparison.Ordinal);
        Assert.Contains("RuntimeCustomRepositoryService.BackendOptions", dialogFactory, StringComparison.Ordinal);
        Assert.Contains("request.ShowValidationWarning(dialog, validation.StatusMessage)", dialogFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("ThemedMessageBox.Show", dialogFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("new Window", runtimeCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogTextBox", runtimeCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("AddDialogRow", runtimeCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("Custom runtime repository service is not initialized.", source, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeFileServiceRestrictsRuntimeDeletionToSafeFolders()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var managed = Path.Combine(runtimeRoot, "managed-runtime");
        var external = Path.Combine(root, "external-runtime");
        var packaged = Path.Combine(root, "packaged-runtime");
        Directory.CreateDirectory(Path.Combine(managed, "bin"));
        Directory.CreateDirectory(packaged);
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(managed, "bin", "llama-server.exe"), "");
        File.WriteAllText(Path.Combine(packaged, "llama-server.exe"), "");
        File.WriteAllText(Path.Combine(packaged, "local-llm-runtime.json"), """{"managedPresetId":"official-cpu"}""");
        var now = DateTimeOffset.UtcNow;
        var managedRuntime = new RuntimeRecord("managed", "Managed", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(managed, "bin", "llama-server.exe"), "{}", now);
        var externalRuntime = new RuntimeRecord("external", "External", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(external, "llama-server.exe"), "{}", now);

        Assert.True(RuntimeFileService.CanDeleteRuntimeFiles(managedRuntime, runtimeRoot, out var managedFolder, out _));
        Assert.Equal(managed, managedFolder);
        Assert.False(RuntimeFileService.CanDeleteRuntimeFiles(externalRuntime, runtimeRoot, out _, out var reason));
        Assert.Contains("outside the app runtimes folder", reason, StringComparison.Ordinal);
        Assert.True(RuntimeFileService.IsPackagedRuntimeFolderSafeToDelete(packaged));

        RuntimeFileService.DeleteRuntimeFiles(runtimeRoot, managed);

        Assert.False(Directory.Exists(managed));
        Assert.Throws<InvalidOperationException>(() => RuntimeFileService.DeleteRuntimeFiles(runtimeRoot, external));
    }


    [Fact]
    public async Task RuntimeDeletionPlannerBlocksActiveAndReassignsModelReferencedRuntimes()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var settings = AppSettings.CreateDefault(root);
        var launchProfiles = new ModelLaunchProfileService(store, sessions);
        var planner = new RuntimeDeletionPlanner(store, launchProfiles, sessions);
        var executor = new RuntimeDeletionExecutorService(store);
        var activeRuntime = new RuntimeRecord("runtime-active", "Active Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "active", "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var referencedFolder = Path.Combine(settings.RuntimeRoot, "referenced");
        var replacementFolder = Path.Combine(settings.RuntimeRoot, "replacement");
        Directory.CreateDirectory(referencedFolder);
        Directory.CreateDirectory(replacementFolder);
        File.WriteAllText(Path.Combine(referencedFolder, "llama-server.exe"), "");
        File.WriteAllText(Path.Combine(replacementFolder, "llama-server.exe"), "");
        var referencedRuntime = new RuntimeRecord("runtime-referenced", "Referenced Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(referencedFolder, "llama-server.exe"), $$"""{"folder":"{{referencedFolder.Replace("\\", "\\\\")}}"}""", DateTimeOffset.UtcNow);
        var replacementRuntime = new RuntimeRecord("runtime-replacement", "Replacement Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(replacementFolder, "llama-server.exe"), $$"""{"folder":"{{replacementFolder.Replace("\\", "\\\\")}}"}""", DateTimeOffset.UtcNow);
        var activeModel = new ModelRecord("model-active", "Active Model", Path.Combine(root, "active.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var referencedModel = new ModelRecord("model-referenced", "Referenced Model", Path.Combine(root, "referenced.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(referencedRuntime);
        await store.UpsertRuntimeAsync(replacementRuntime);
        await store.UpsertModelAsync(referencedModel);
        await launchProfiles.SaveAsync(referencedModel, ModelLaunchSettings.FromAppSettings(settings) with { RuntimeId = referencedRuntime.Id });
        sessions.AttachExisting(activeRuntime, activeModel, settings, "active.log", LlamaRuntimeState.Loaded, "", "active-session", DateTimeOffset.UtcNow);

        var activePlan = await planner.PlanRuntimeDeletionAsync(activeRuntime, settings.RuntimeRoot);
        var referencedPlan = await planner.PlanRuntimeDeletionAsync(referencedRuntime, settings.RuntimeRoot);
        var usage = await planner.ModelsByRuntimeAsync();

        Assert.Equal(RuntimeDeletionPlanKind.Blocked, activePlan.Kind);
        Assert.Contains("Unload", activePlan.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RuntimeDeletionPlanKind.DeleteFiles, referencedPlan.Kind);
        var reassignment = Assert.Single(referencedPlan.Reassignments);
        Assert.Equal("Referenced Model", reassignment.ModelName);
        Assert.Equal(replacementRuntime.Id, reassignment.ReplacementRuntimeId);
        Assert.Equal(["Referenced Model"], usage[referencedRuntime.Id]);

        await executor.DeleteRuntimeAsync(referencedPlan, settings.RuntimeRoot);
        var updatedProfile = await launchProfiles.ReadAsync(referencedModel);
        Assert.NotNull(updatedProfile);
        Assert.Equal(replacementRuntime.Id, updatedProfile.RuntimeId);
        Assert.DoesNotContain(await store.ListRuntimesAsync(), runtime => runtime.Id == referencedRuntime.Id);
    }


    [Fact]
    public async Task RuntimeDeletionPlannerDistinguishesFileDeletionFromRegistrationOnly()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var settings = AppSettings.CreateDefault(root);
        var launchProfiles = new ModelLaunchProfileService(store, sessions);
        var planner = new RuntimeDeletionPlanner(store, launchProfiles, sessions);
        var executor = new RuntimeDeletionExecutorService(store);
        var safeFolder = Path.Combine(settings.RuntimeRoot, "safe-runtime");
        var externalFolder = Path.Combine(root, "external-runtime");
        Directory.CreateDirectory(safeFolder);
        Directory.CreateDirectory(externalFolder);
        File.WriteAllText(Path.Combine(safeFolder, "llama-server.exe"), "");
        File.WriteAllText(Path.Combine(externalFolder, "llama-server.exe"), "");
        var safeRuntime = new RuntimeRecord("runtime-safe", "Safe Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(safeFolder, "llama-server.exe"), $$"""{"folder":"{{safeFolder.Replace("\\", "\\\\")}}"}""", DateTimeOffset.UtcNow);
        var externalRuntime = new RuntimeRecord("runtime-external", "External Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(externalFolder, "llama-server.exe"), $$"""{"folder":"{{externalFolder.Replace("\\", "\\\\")}}"}""", DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(safeRuntime);
        await store.UpsertRuntimeAsync(externalRuntime);

        var safePlan = await planner.PlanRuntimeDeletionAsync(safeRuntime, settings.RuntimeRoot);
        var externalPlan = await planner.PlanRuntimeDeletionAsync(externalRuntime, settings.RuntimeRoot);
        await executor.DeleteRuntimeAsync(safePlan, settings.RuntimeRoot);
        await executor.DeleteRuntimeAsync(externalPlan, settings.RuntimeRoot);

        Assert.Equal(RuntimeDeletionPlanKind.DeleteFiles, safePlan.Kind);
        Assert.Equal([safeFolder], safePlan.Folders);
        Assert.Equal(RuntimeDeletionPlanKind.RegistrationOnly, externalPlan.Kind);
        Assert.Contains("outside the app runtimes folder", externalPlan.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await store.ListRuntimesAsync());
        Assert.False(Directory.Exists(safeFolder));
        Assert.True(Directory.Exists(externalFolder));
    }


    [Fact]
    public async Task RuntimeDeletionPlannerPlansPackageDeletionAndModelReferenceBlocks()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var settings = AppSettings.CreateDefault(root);
        var launchProfiles = new ModelLaunchProfileService(store, sessions);
        var planner = new RuntimeDeletionPlanner(store, launchProfiles, sessions);
        var executor = new RuntimeDeletionExecutorService(store);
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cuda");
        var packageFolder = Path.Combine(settings.RuntimeRoot, "official-prebuilt-windows-cuda-b9354");
        Directory.CreateDirectory(packageFolder);
        File.WriteAllText(Path.Combine(packageFolder, "llama-server.exe"), "");
        var metadata = $$"""{"folder":"{{packageFolder.Replace("\\", "\\\\")}}","managedPackageId":"{{preset.Id}}"}""";
        var runtime = new RuntimeRecord("package-runtime", "Package Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(packageFolder, "llama-server.exe"), metadata, DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);

        var deletePlan = await planner.PlanPackageDeletionAsync(preset, settings.RuntimeRoot);
        var model = new ModelRecord("model-package", "Package Model", Path.Combine(root, "package.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(model);
        await launchProfiles.SaveAsync(model, ModelLaunchSettings.FromAppSettings(settings) with { RuntimeId = runtime.Id });
        var blockedPlan = await planner.PlanPackageDeletionAsync(preset, settings.RuntimeRoot);
        await executor.DeletePackageAsync(deletePlan, settings.RuntimeRoot);

        Assert.Equal(RuntimeDeletionPlanKind.DeleteFiles, deletePlan.Kind);
        Assert.Equal([runtime], deletePlan.Runtimes);
        Assert.Equal([packageFolder], deletePlan.Folders);
        Assert.Empty(await store.ListRuntimesAsync());
        Assert.False(Directory.Exists(packageFolder));
        Assert.Equal(RuntimeDeletionPlanKind.Blocked, blockedPlan.Kind);
        Assert.Contains("Package Model", blockedPlan.StatusMessage, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeDeletionPlannerPlansAndExecutesBuildPresetDeletion()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var settings = AppSettings.CreateDefault(root);
        var launchProfiles = new ModelLaunchProfileService(store, sessions);
        var planner = new RuntimeDeletionPlanner(store, launchProfiles, sessions);
        var executor = new RuntimeDeletionExecutorService(store);
        var preset = new RuntimeBuildPreset("custom-cleanup-cpu", "Cleanup CPU", "https://example.com/repo.git", "main", false, Custom: true, Mode: RuntimeMode.Native);
        await RuntimeBuildCatalogService.SaveCustomPresetsAsync(settings.RuntimeRoot, [preset], TestContext.Current.CancellationToken);
        var runtimeFolder = Path.Combine(settings.RuntimeRoot, "custom-cleanup-cpu-build");
        var sourceFolder = Path.Combine(RuntimeBuildCatalogService.SourceRoot(settings.RuntimeRoot), "custom-cleanup-cpu-downloaded");
        var partialSourceFolder = RuntimeBuildCatalogService.SourceDir(settings.RuntimeRoot, preset);
        Directory.CreateDirectory(runtimeFolder);
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(partialSourceFolder);
        File.WriteAllText(Path.Combine(runtimeFolder, "llama-server.exe"), "");
        var runtime = new RuntimeRecord(
            "runtime-cleanup",
            "Cleanup Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(runtimeFolder, "llama-server.exe"),
            $$"""{"folder":"{{runtimeFolder.Replace("\\", "\\\\")}}","managedPresetId":"{{preset.Id}}"}""",
            DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);
        var source = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, sourceFolder, "abcdef123456", DateTimeOffset.UtcNow, Mode: RuntimeMode.Native);

        var plan = await planner.PlanBuildPresetDeletionAsync(preset, settings.RuntimeRoot, [source]);
        await executor.DeleteBuildPresetAsync(plan, settings.RuntimeRoot);

        Assert.Equal(RuntimeBuildPresetDeletionPlanKind.DeleteBuildsAndSources, plan.Kind);
        Assert.True(plan.RemoveCustomRepository);
        Assert.True(plan.HasPartialSourceCache);
        Assert.Equal([runtime], plan.Runtimes);
        Assert.Equal([source], plan.Sources);
        Assert.Contains(runtimeFolder, plan.RuntimeFolders, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(sourceFolder, plan.SourceFolders, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(partialSourceFolder, plan.SourceFolders, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(await store.ListRuntimesAsync());
        Assert.False(Directory.Exists(runtimeFolder));
        Assert.False(Directory.Exists(sourceFolder));
        Assert.False(Directory.Exists(partialSourceFolder));
        Assert.DoesNotContain(RuntimeBuildCatalogService.ReadCustomPresets(settings.RuntimeRoot), candidate => candidate.Id == preset.Id);
    }


    [Fact]
    public async Task RuntimeDeletionPlannerBlocksBuildPresetDeletionForActiveAndReferencedRuntime()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var preset = new RuntimeBuildPreset("official-windows-cpu", "Official CPU", "https://example.com/repo.git", "main", false, Mode: RuntimeMode.Native);
        var activeRuntime = new RuntimeRecord("runtime-active-build", "Active Build", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(settings.RuntimeRoot, "active", "llama-server.exe"), $$"""{"folder":"{{Path.Combine(settings.RuntimeRoot, "active").Replace("\\", "\\\\")}}","managedPresetId":"{{preset.Id}}"}""", DateTimeOffset.UtcNow);
        var referencedRuntime = new RuntimeRecord("runtime-referenced-build", "Referenced Build", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(settings.RuntimeRoot, "referenced", "llama-server.exe"), $$"""{"folder":"{{Path.Combine(settings.RuntimeRoot, "referenced").Replace("\\", "\\\\")}}","managedPresetId":"{{preset.Id}}"}""", DateTimeOffset.UtcNow);
        var activeModel = new ModelRecord("model-active-build", "Active Build Model", Path.Combine(root, "active.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var referencedModel = new ModelRecord("model-referenced-build", "Referenced Build Model", Path.Combine(root, "referenced.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(activeRuntime);
        await store.UpsertRuntimeAsync(referencedRuntime);
        await store.UpsertModelAsync(referencedModel);

        using var activeSessions = CreateLoadedModelSessionManager();
        var activeProfiles = new ModelLaunchProfileService(store, activeSessions);
        var activePlanner = new RuntimeDeletionPlanner(store, activeProfiles, activeSessions);
        activeSessions.AttachExisting(activeRuntime, activeModel, settings, "active.log", LlamaRuntimeState.Loaded, "", "active-build-session", DateTimeOffset.UtcNow);
        var activePlan = await activePlanner.PlanBuildPresetDeletionAsync(preset, settings.RuntimeRoot, []);

        using var idleSessions = CreateLoadedModelSessionManager();
        var idleProfiles = new ModelLaunchProfileService(store, idleSessions);
        await idleProfiles.SaveAsync(referencedModel, ModelLaunchSettings.FromAppSettings(settings) with { RuntimeId = referencedRuntime.Id });
        var idlePlanner = new RuntimeDeletionPlanner(store, idleProfiles, idleSessions);
        var referencedPlan = await idlePlanner.PlanBuildPresetDeletionAsync(preset, settings.RuntimeRoot, []);

        Assert.Equal(RuntimeBuildPresetDeletionPlanKind.Blocked, activePlan.Kind);
        Assert.Contains("Unload", activePlan.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RuntimeBuildPresetDeletionPlanKind.Blocked, referencedPlan.Kind);
        Assert.Contains("Referenced Build Model", referencedPlan.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(["Referenced Build Model"], referencedPlan.BlockingModelNames);
    }


    [Fact]
    public async Task RuntimeDeletionPlannerPlansAndExecutesRuntimeSourceDeletion()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var settings = AppSettings.CreateDefault(root);
        var planner = new RuntimeDeletionPlanner(store, new ModelLaunchProfileService(store, sessions), sessions);
        var executor = new RuntimeDeletionExecutorService(store);
        var sourceFolder = Path.Combine(RuntimeBuildCatalogService.SourceRoot(settings.RuntimeRoot), "downloaded-source");
        Directory.CreateDirectory(sourceFolder);
        var source = new RuntimeSourceEntry("preset", "Preset", "https://example.com/repo.git", "main", false, sourceFolder, "abcdef", DateTimeOffset.UtcNow);
        var external = source with { SourceDir = Path.Combine(root, "outside-source") };

        var plan = planner.PlanRuntimeSourceDeletion(source, settings.RuntimeRoot);
        var blocked = planner.PlanRuntimeSourceDeletion(external, settings.RuntimeRoot);
        await executor.DeleteRuntimeSourceAsync(plan, settings.RuntimeRoot);

        Assert.Equal(RuntimeSourceDeletionPlanKind.DeleteSourceFolder, plan.Kind);
        Assert.False(Directory.Exists(sourceFolder));
        Assert.Equal(RuntimeSourceDeletionPlanKind.Blocked, blocked.Kind);
        Assert.Contains("inside the configured runtimes folder", blocked.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task RuntimeBuildDeletionApplicationServiceCoordinatesRuntimeSourceAndPresetDeletion()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var settings = AppSettings.CreateDefault(root);
        var launchProfiles = new ModelLaunchProfileService(store, sessions);
        var service = new RuntimeBuildDeletionApplicationService(
            new RuntimeDeletionPlanner(store, launchProfiles, sessions),
            new RuntimeDeletionExecutorService(store),
            new RuntimeCatalogDataService());
        var statuses = new List<string>();
        var confirmations = new List<RuntimeBuildDeletionConfirmation>();
        var busyMessages = new List<string>();
        var runtimeRefreshes = 0;
        var overviewRefreshes = 0;
        var allowConfirm = false;
        RuntimeBuildDeletionApplicationActions Actions() => new(
            confirmation =>
            {
                confirmations.Add(confirmation);
                return allowConfirm;
            },
            async (message, action) =>
            {
                busyMessages.Add(message);
                await action();
            },
            () =>
            {
                runtimeRefreshes++;
                return Task.CompletedTask;
            },
            () =>
            {
                overviewRefreshes++;
                return Task.CompletedTask;
            },
            statuses.Add);

        var runtimeFolder = Path.Combine(settings.RuntimeRoot, "app-delete-runtime");
        Directory.CreateDirectory(runtimeFolder);
        File.WriteAllText(Path.Combine(runtimeFolder, "llama-server.exe"), "");
        var runtime = new RuntimeRecord(
            "runtime-app-delete",
            "App Delete Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(runtimeFolder, "llama-server.exe"),
            $$"""{"folder":"{{runtimeFolder.Replace("\\", "\\\\")}}"}""",
            DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);

        var cancelledRuntime = await service.DeleteRuntimeAsync(runtime, settings, Actions());
        var afterCancelledRuntime = await store.ListRuntimesAsync();
        var runtimeFolderAfterCancel = Directory.Exists(runtimeFolder);
        allowConfirm = true;
        var deletedRuntime = await service.DeleteRuntimeAsync(runtime, settings, Actions());
        var afterDeletedRuntime = await store.ListRuntimesAsync();

        var blockedRuntime = new RuntimeRecord(
            "runtime-blocked-delete",
            "Blocked Delete Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(settings.RuntimeRoot, "blocked", "llama-server.exe"),
            "{}",
            DateTimeOffset.UtcNow);
        var replacementRuntime = new RuntimeRecord(
            "runtime-replacement-delete",
            "Replacement Delete Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(settings.RuntimeRoot, "replacement", "llama-server.exe"),
            "{}",
            DateTimeOffset.UtcNow);
        var blockingModel = new ModelRecord("model-blocking-delete", "Blocked Delete Model", Path.Combine(root, "blocked.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(blockedRuntime);
        await store.UpsertRuntimeAsync(replacementRuntime);
        await store.UpsertModelAsync(blockingModel);
        await launchProfiles.SaveAsync(blockingModel, ModelLaunchSettings.FromAppSettings(settings) with { RuntimeId = blockedRuntime.Id });
        var blockedDelete = await service.DeleteRuntimeAsync(blockedRuntime, settings, Actions());
        var reassignedProfile = await launchProfiles.ReadAsync(blockingModel);

        var sourceFolder = Path.Combine(RuntimeBuildCatalogService.SourceRoot(settings.RuntimeRoot), "app-delete-source");
        Directory.CreateDirectory(sourceFolder);
        var source = new RuntimeSourceEntry("source-preset", "Source Preset", "https://example.com/source.git", "main", false, sourceFolder, "abcdef", DateTimeOffset.UtcNow, Mode: RuntimeMode.Native);
        var deletedSource = await service.DeleteSourceAsync(source, settings, Actions());

        var preset = new RuntimeBuildPreset("app-delete-preset", "App Delete Preset", "https://example.com/preset.git", "main", false, Custom: true, Mode: RuntimeMode.Native);
        await RuntimeBuildCatalogService.SaveCustomPresetsAsync(settings.RuntimeRoot, [preset], TestContext.Current.CancellationToken);
        var presetRuntimeFolder = Path.Combine(settings.RuntimeRoot, "app-delete-preset-runtime");
        var presetSourceFolder = RuntimeBuildCatalogService.SourceDir(settings.RuntimeRoot, preset);
        Directory.CreateDirectory(presetRuntimeFolder);
        Directory.CreateDirectory(presetSourceFolder);
        File.WriteAllText(Path.Combine(presetRuntimeFolder, "llama-server.exe"), "");
        var presetRuntime = new RuntimeRecord(
            "runtime-app-delete-preset",
            "App Delete Preset Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(presetRuntimeFolder, "llama-server.exe"),
            $$"""{"folder":"{{presetRuntimeFolder.Replace("\\", "\\\\")}}","managedPresetId":"{{preset.Id}}","managedAction":"build","commit":"abcdef123456"}""",
            DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(presetRuntime);
        var packagedRuntimeFolder = Path.Combine(settings.RuntimeRoot, "app-delete-packaged-runtime");
        Directory.CreateDirectory(packagedRuntimeFolder);
        var packagedRuntimeExe = Path.Combine(packagedRuntimeFolder, "llama-server.exe");
        File.WriteAllText(packagedRuntimeExe, "");
        var packagedRuntime = presetRuntime with
        {
            Id = "runtime-app-delete-packaged",
            ExecutablePath = packagedRuntimeExe,
            MetadataJson = $$"""{"folder":"{{packagedRuntimeFolder.Replace("\\", "\\\\")}}","managedPresetId":"{{preset.Id}}","managedPackageId":"package-keep"}"""
        };
        await store.UpsertRuntimeAsync(packagedRuntime);
        var presetSource = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, presetSourceFolder, "abcdef123456", DateTimeOffset.UtcNow, Mode: RuntimeMode.Native);
        await File.WriteAllTextAsync(
            RuntimeBuildCatalogService.SourceMetadataPath(presetSourceFolder),
            System.Text.Json.JsonSerializer.Serialize(presetSource),
            TestContext.Current.CancellationToken);
        var deletedPreset = await service.DeletePresetBuildsAsync(preset, settings, Actions());
        var afterDeletedPreset = await store.ListRuntimesAsync();

        Assert.Equal(RuntimeBuildDeletionApplicationOutcome.Cancelled, cancelledRuntime);
        Assert.Contains(afterCancelledRuntime, candidate => candidate.Id == runtime.Id);
        Assert.True(runtimeFolderAfterCancel);
        Assert.Equal(RuntimeBuildDeletionApplicationOutcome.Deleted, deletedRuntime);
        Assert.DoesNotContain(afterDeletedRuntime, candidate => candidate.Id == runtime.Id);
        Assert.False(Directory.Exists(runtimeFolder));
        Assert.Equal(RuntimeBuildDeletionApplicationOutcome.Deleted, blockedDelete);
        Assert.NotNull(reassignedProfile);
        Assert.Equal(replacementRuntime.Id, reassignedProfile.RuntimeId);
        Assert.Contains(confirmations, confirmation => confirmation.Message.Contains("Saved model launch settings", StringComparison.Ordinal)
            && confirmation.Message.Contains("Blocked Delete Model", StringComparison.Ordinal)
            && confirmation.Message.Contains("Replacement Delete Runtime", StringComparison.Ordinal));
        Assert.Equal(RuntimeBuildDeletionApplicationOutcome.Deleted, deletedSource);
        Assert.False(Directory.Exists(sourceFolder));
        Assert.Equal(RuntimeBuildDeletionApplicationOutcome.Deleted, deletedPreset);
        Assert.DoesNotContain(afterDeletedPreset, candidate => candidate.Id == presetRuntime.Id);
        Assert.Contains(afterDeletedPreset, candidate => candidate.Id == packagedRuntime.Id);
        Assert.False(Directory.Exists(presetRuntimeFolder));
        Assert.True(Directory.Exists(packagedRuntimeFolder));
        Assert.False(Directory.Exists(presetSourceFolder));
        Assert.DoesNotContain(RuntimeBuildCatalogService.ReadCustomPresets(settings.RuntimeRoot), candidate => candidate.Id == preset.Id);
        Assert.Equal(RuntimeBuildDeletionConfirmationKind.RuntimeFiles, confirmations[0].Kind);
        Assert.Contains(confirmations, confirmation => confirmation.Kind == RuntimeBuildDeletionConfirmationKind.RuntimeSource);
        Assert.Contains(confirmations, confirmation => confirmation.Kind == RuntimeBuildDeletionConfirmationKind.PresetBuilds
            && confirmation.Message.Contains("Built runtimes: 1", StringComparison.Ordinal));
        Assert.Contains("Deleting runtime build...", busyMessages);
        Assert.Contains("Deleting downloaded source...", busyMessages);
        Assert.Contains("Deleting repository builds...", busyMessages);
        Assert.True(runtimeRefreshes >= 3);
        Assert.True(overviewRefreshes >= 3);
    }


    [Fact]
    public void RuntimeDownloadsProjectSourceWorkflowAndVendorPlatformFilters()
    {
        var package = RuntimePackageSourceCatalog.PresetRows()
            .Single(candidate => candidate.Id == "official-prebuilt-windows-cuda");
        var build = RuntimeBuildCatalogService.DefaultPresets
            .Single(candidate => candidate.Id == package.SourcePresetId);
        var view = new RuntimeCatalogViewService(new RuntimePackageStatusService());

        var uncheckedRow = view.BuildPackageRows(
            [package], [build], [], [],
            new Dictionary<string, RuntimeUpdateState>(),
            new Dictionary<string, RuntimePackageUpdateState>()).Single(row => row.Preset?.Id == package.Id);
        var checkedState = new RuntimeUpdateState(true, "", "abcdef1234567890", DateTimeOffset.UtcNow);
        var checkedRows = view.BuildPackageRows(
            [package], [build], [], [],
            new Dictionary<string, RuntimeUpdateState> { [build.Id] = checkedState },
            new Dictionary<string, RuntimePackageUpdateState>());
        var checkedRow = checkedRows.Single(row => row.Preset?.Id == package.Id);
        var source = new RuntimeSourceEntry(
            build.Id,
            build.Label,
            build.RepoUrl,
            build.Branch,
            build.Cuda,
            Path.Combine("D:", "runtimes", "runtime-sources", build.Id),
            checkedState.RemoteCommit,
            DateTimeOffset.UtcNow,
            Mode: RuntimeMode.Native);
        var downloadedRow = view.BuildPackageRows(
            [package], [build], [], [source],
            new Dictionary<string, RuntimeUpdateState> { [build.Id] = checkedState },
            new Dictionary<string, RuntimePackageUpdateState>()).Single(row => row.Preset?.Id == package.Id);
        var packageRoot = CreateTempRoot();
        var packageFolder = Path.Combine(packageRoot, "package-runtime");
        var packageExe = Path.Combine(packageFolder, "llama-server.exe");
        Directory.CreateDirectory(packageFolder);
        File.WriteAllText(packageExe, "");
        var packageRuntime = new RuntimeRecord(
            "package-runtime",
            package.Label,
            RuntimeMode.Native,
            RuntimeBackend.Cuda,
            packageExe,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = packageFolder,
                managedPresetId = build.Id,
                managedPackageId = package.Id,
                releaseTag = "b10000"
            }),
            DateTimeOffset.UtcNow);
        var installedPackageRow = view.BuildPackageRows(
            [package], [build], [packageRuntime], [],
            new Dictionary<string, RuntimeUpdateState>(),
            new Dictionary<string, RuntimePackageUpdateState>()).Single(row => row.Preset?.Id == package.Id);
        var unmanagedRuntime = packageRuntime with
        {
            Id = "unmanaged-cuda-runtime",
            Name = "llama.cpp custom Native Cuda",
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { folder = packageFolder })
        };

        Assert.Equal(RuntimeSourceRowActionKind.Check, uncheckedRow.SourceActionKind);
        Assert.Equal("Check", uncheckedRow.BuildSourceAction);
        Assert.Equal(RuntimeSourceRowActionKind.Download, checkedRow.SourceActionKind);
        Assert.Equal("Download", checkedRow.BuildSourceAction);
        Assert.Equal(RuntimeSourceRowActionKind.Build, downloadedRow.SourceActionKind);
        Assert.Equal("Build", downloadedRow.BuildSourceAction);
        Assert.Same(source, downloadedRow.DownloadedSource);
        Assert.Equal(RuntimeInventoryFilterService.Nvidia, downloadedRow.Vendor);
        Assert.Equal(RuntimeInventoryFilterService.Windows, downloadedRow.Platform);
        Assert.Empty(RuntimeCatalogDataService.InstalledRuntimesForPreset([packageRuntime], build.Id));
        Assert.Empty(RuntimePackageInventoryPresenter.MatchingSourceBuilds([packageRuntime], package));
        Assert.Empty(RuntimeCatalogDataService.InstalledRuntimesForPreset([unmanagedRuntime], build.Id));
        Assert.False(RuntimeMetadataService.IsManagedSourceBuild(unmanagedRuntime));
        Assert.Equal(RuntimeSourceRowActionKind.Check, installedPackageRow.SourceActionKind);
        Assert.True(installedPackageRow.CanBuildSource);
        Assert.Equal(RuntimeDownloadDeleteKind.Package, installedPackageRow.DeleteKind);
        Assert.Equal("Delete All", installedPackageRow.DeleteAction);

        var downloads = new RuntimePackagesPageViewModel();
        downloads.ReplaceRows([
            downloadedRow,
            new RuntimePackagePresetRow { Label = "Vulkan", Vendor = RuntimeInventoryFilterService.Amd, Platform = RuntimeInventoryFilterService.Linux },
            new RuntimePackagePresetRow { Label = "SYCL", Vendor = RuntimeInventoryFilterService.Intel, Platform = RuntimeInventoryFilterService.Windows },
            new RuntimePackagePresetRow { Label = "Add", Vendor = RuntimeInventoryFilterService.All, Platform = RuntimeInventoryFilterService.All }
        ]);
        downloads.ApplyFilters(RuntimeInventoryFilterService.Amd, RuntimeInventoryFilterService.Linux);

        Assert.Equal(["Vulkan", "Add"], downloads.Rows.Select(row => row.Label).ToArray());
        Assert.Equal(RuntimeInventoryFilterService.Nvidia, RuntimeInventoryFilterService.Vendor(RuntimeBackend.Cuda));
        Assert.Equal(RuntimeInventoryFilterService.Amd, RuntimeInventoryFilterService.Vendor(RuntimeBackend.Vulkan));
        Assert.Equal(RuntimeInventoryFilterService.Intel, RuntimeInventoryFilterService.Vendor(RuntimeBackend.Sycl));
        Assert.Equal(RuntimeInventoryFilterService.Linux, RuntimeInventoryFilterService.Platform(RuntimeMode.Wsl));
    }

}
