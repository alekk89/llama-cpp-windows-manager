using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using LocalLlmConsole.Localization;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


[Collection(LocalizationStateTestCollection.Name)]
public sealed class RuntimeCatalogApplicationTests : ManagerRegressionTestBase
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
        var ikRuntime = runtime with { Name = "ik_llama.cpp", MetadataJson = runtime.MetadataJson.Replace("ggml-org/llama.cpp", "ikawrakow/ik_llama.cpp", StringComparison.Ordinal) };
        Assert.Equal("ik-llama-cuda", RuntimeMetadataService.ManagedPresetId(ikRuntime));
        Assert.Equal("ik-windows-cuda", RuntimeMetadataService.ManagedPresetId(ikRuntime with { Mode = RuntimeMode.Native, ExecutablePath = Path.Combine(runtimeRoot, "llama-server.exe") }));
        Assert.Equal("ik-llama-cpu", RuntimeMetadataService.ManagedPresetId(ikRuntime with { Backend = RuntimeBackend.Cpu }));
        var theTomRuntime = runtime with { Name = "TheTom TurboQuant", MetadataJson = runtime.MetadataJson.Replace("ggml-org/llama.cpp", "TheTom/llama-cpp-turboquant", StringComparison.Ordinal) };
        Assert.Equal("thetom-turboquant-cuda", RuntimeMetadataService.ManagedPresetId(theTomRuntime));
        Assert.Equal("thetom-windows-turboquant-cuda", RuntimeMetadataService.ManagedPresetId(theTomRuntime with { Mode = RuntimeMode.Native, ExecutablePath = Path.Combine(runtimeRoot, "llama-server.exe") }));
        Assert.Equal("thetom-turboquant-vulkan", RuntimeMetadataService.ManagedPresetId(theTomRuntime with { Backend = RuntimeBackend.Vulkan }));
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
        Assert.Contains(rows, preset => preset.Id == "ik-windows-cuda" && RuntimeBuildCatalogService.BuildBackend(preset) == RuntimeBackend.Cuda && RuntimeBuildCatalogService.BuildMode(preset) == RuntimeMode.Native);
        Assert.Contains(rows, preset => preset.Id == "ik-llama-cpu" && RuntimeBuildCatalogService.BuildBackend(preset) == RuntimeBackend.Cpu && RuntimeBuildCatalogService.BuildMode(preset) == RuntimeMode.Wsl);
        Assert.Contains(rows, preset => preset.Id == "thetom-windows-turboquant-cuda" && RuntimeBuildCatalogService.BuildBackend(preset) == RuntimeBackend.Cuda && RuntimeBuildCatalogService.BuildMode(preset) == RuntimeMode.Native);
        Assert.Contains(rows, preset => preset.Id == "thetom-turboquant-vulkan" && RuntimeBuildCatalogService.BuildBackend(preset) == RuntimeBackend.Vulkan && RuntimeBuildCatalogService.BuildMode(preset) == RuntimeMode.Wsl);
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
    public async Task RuntimeBuildCatalogServiceRecoversCustomPresetsFromLastKnownGoodBackup()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var first = new RuntimeBuildPreset("", "First", "https://example.com/first.git", "main", false, Custom: true);
        var second = new RuntimeBuildPreset("", "Second", "https://example.com/second.git", "main", false, Custom: true);

        await RuntimeBuildCatalogService.SaveCustomPresetsAsync(runtimeRoot, [first], TestContext.Current.CancellationToken);
        await RuntimeBuildCatalogService.SaveCustomPresetsAsync(runtimeRoot, [second], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            RuntimeBuildCatalogService.CustomRepositoriesPath(runtimeRoot),
            "{truncated",
            TestContext.Current.CancellationToken);

        var recovered = Assert.Single(RuntimeBuildCatalogService.ReadCustomPresets(runtimeRoot));

        Assert.Equal("First", recovered.Label);
        Assert.True(File.Exists(RuntimeBuildCatalogService.CustomRepositoriesBackupPath(runtimeRoot)));
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
            new Dictionary<string, RuntimePackageUpdateState>()), TestContext.Current.CancellationToken);

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
                    scanRefreshes.Add("overview");
                    return Task.CompletedTask;
                }));

        Assert.Contains(refresh.Runtimes, candidate => candidate.Id == runtime.Id);
        Assert.Contains(refresh.Rows.Runtimes, row => row.Runtime?.Id == runtime.Id);
        Assert.NotEmpty(refresh.Rows.BuildPresets);
        Assert.NotEmpty(refresh.Rows.PackagePresets);
        Assert.Equal(["Detecting installed runtimes..."], busyMessages);
        Assert.Equal(["runtimes", "overview"], scanRefreshes);
        Assert.False(scanState.TryMarkRuntimeRootScanned(settings.RuntimeRoot, out _));
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
        var stateSource = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Catalog", "RuntimeCatalogSessionState.cs"));
        var runtimeCatalogApplication = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Catalog", "RuntimeCatalogApplicationService.cs"));
        var runtimePackageApplication = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Packages", "RuntimePackageApplicationService.cs"));
        var runtimeSourceApplication = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Build", "RuntimeSourceApplicationService.cs"));
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
        var runtimeCatalog = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Runtimes", "MainWindow.RuntimeCatalog.cs"));
        var dialogFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Runtimes", "RuntimeCustomRepositoryDialogFactory.cs"));

        Assert.Contains("RuntimeCustomRepositoryDialogFactory.Show", runtimeCatalog, StringComparison.Ordinal);
        Assert.Contains("ValidateDraft", dialogFactory, StringComparison.Ordinal);
        Assert.Contains("ShowValidationWarning", dialogFactory, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.Dialogs.Notify(owner, message, Loc.T(\"Runtimes.CustomRepo.NotificationTitle\"), MessageBoxImage.Warning)", runtimeCatalog, StringComparison.Ordinal);
        Assert.Contains("RuntimeCustomRepositoryService.BackendOptions", dialogFactory, StringComparison.Ordinal);
        Assert.Contains("request.ShowValidationWarning(dialog, validation.StatusMessage)", dialogFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("ThemedMessageBox.Show", dialogFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("new Window", runtimeCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogTextBox", runtimeCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("AddDialogRow", runtimeCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("Custom runtime repository service is not initialized.", source, StringComparison.Ordinal);
    }


}
