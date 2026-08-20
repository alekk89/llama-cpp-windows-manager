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

        await executor.DeleteRuntimeAsync(referencedPlan, settings.RuntimeRoot, TestContext.Current.CancellationToken);
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
        await executor.DeleteRuntimeAsync(safePlan, settings.RuntimeRoot, TestContext.Current.CancellationToken);
        await executor.DeleteRuntimeAsync(externalPlan, settings.RuntimeRoot, TestContext.Current.CancellationToken);

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
        await executor.DeletePackageAsync(deletePlan, settings.RuntimeRoot, TestContext.Current.CancellationToken);

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
        await executor.DeleteBuildPresetAsync(plan, settings.RuntimeRoot, TestContext.Current.CancellationToken);

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
        await executor.DeleteRuntimeSourceAsync(plan, settings.RuntimeRoot, TestContext.Current.CancellationToken);

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


}
