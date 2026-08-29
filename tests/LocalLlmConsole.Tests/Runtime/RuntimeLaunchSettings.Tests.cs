using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeLaunchSettingsTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task TrackedProcessRunnerCapturesOutputErrorAndStandardInput()
    {
        var runner = new TrackedProcessRunner();
        var psi = new System.Diagnostics.ProcessStartInfo(HostExecutableResolver.WindowsPowerShellExe());
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("$text = [Console]::In.ReadToEnd(); Write-Output $text.Trim(); [Console]::Error.WriteLine('runner-error')");

        var result = await runner.RunAsync(
            psi,
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken,
            "runner-output");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("runner-output", result.Output, StringComparison.Ordinal);
        Assert.Contains("runner-error", result.Error, StringComparison.Ordinal);
    }


    [Fact]
    public async Task TrackedProcessRunnerCancelsAndDrainsRedirectedIoPromptly()
    {
        var runner = new TrackedProcessRunner();
        var psi = new ProcessStartInfo(HostExecutableResolver.WindowsPowerShellExe());
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("Write-Output 'started'; Start-Sleep -Seconds 30");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            psi,
            TimeSpan.FromMinutes(1),
            cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Cancellation took {stopwatch.Elapsed}.");
    }


    [Fact]
    public async Task RuntimePortAllocatorSkipsReservedAndOccupiedPorts()
    {
        var allocator = new RuntimePortAllocator();
        var occupied = new HashSet<int> { 8082 };

        var port = await allocator.AllocateAsync(
            8081,
            [8081],
            candidate => Task.FromResult(occupied.Contains(candidate)));

        Assert.Equal(8083, port);
    }


    [Fact]
    public void ModelPortAllocatorUsesLowestFreePortAndReusesGaps()
    {
        Assert.Equal(8081, ModelPortAllocator.NextAvailable(8081, []));
        Assert.Equal(8082, ModelPortAllocator.NextAvailable(8081, [8081]));
        Assert.Equal(8082, ModelPortAllocator.NextAvailable(8081, [8081, 8083]));
        Assert.Equal(8081, ModelPortAllocator.NextAvailable(8081, [8082, 8083]));
    }



    [Fact]
    public void LaunchRuntimeSelectionServiceOwnsResolutionAndMissingStatus()
    {
        var root = CreateTempRoot();
        var service = new LaunchRuntimeSelectionService();
        var cpu = new RuntimeRecord("runtime-cpu", "CPU", RuntimeMode.Native, RuntimeBackend.Cpu, CreateRuntimeExecutable(root, "cpu.exe"), "{}", DateTimeOffset.UtcNow);
        var cuda = new RuntimeRecord("runtime-cuda", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, CreateRuntimeExecutable(root, "cuda.exe"), "{}", DateTimeOffset.UtcNow);

        Assert.Same(cuda, service.Resolve([cpu, cuda], "RUNTIME-CUDA"));
        Assert.Null(service.Resolve([cpu, cuda], "missing"));
        Assert.Same(cuda, service.Resolve([cpu], "", cuda));
        Assert.Same(cpu, service.Resolve([cpu, cuda], ""));
        Assert.Null(service.Resolve([], ""));
        var selectedState = service.BuildSelectorState([cpu, cuda], "RUNTIME-CUDA");
        Assert.Equal("runtime-cuda", selectedState.SelectedRuntimeId);
        Assert.Null(selectedState.MissingRuntimeId);
        Assert.Equal([cpu, cuda], selectedState.Runtimes);
        var defaultState = service.BuildSelectorState([cpu, cuda], "");
        Assert.Equal("runtime-cpu", defaultState.SelectedRuntimeId);
        Assert.Null(defaultState.MissingRuntimeId);
        var missingState = service.BuildSelectorState([cpu], "missing");
        Assert.Equal("missing", missingState.SelectedRuntimeId);
        Assert.Equal("missing", missingState.MissingRuntimeId);
        Assert.Equal("Register a llama.cpp runtime first.", service.MissingRuntimeStatus([], ""));
        Assert.Equal(
            "Saved runtime 'missing' is missing. Choose another runtime and save the model profile.",
            service.MissingRuntimeStatus([cpu], "missing"));
        Assert.Equal("Choose a llama.cpp runtime before loading the model.", service.MissingRuntimeStatus([cpu], ""));

        var unavailable = cpu with { ExecutablePath = Path.Combine(root, "deleted", "llama-server.exe") };
        Assert.Null(service.Resolve([unavailable], unavailable.Id));
        var unavailableState = service.BuildSelectorState([unavailable, cuda], unavailable.Id);
        Assert.Equal([cuda], unavailableState.Runtimes);
        Assert.Equal(unavailable.Id, unavailableState.MissingRuntimeId);
        Assert.Contains("unavailable", service.MissingRuntimeStatus([unavailable, cuda], unavailable.Id), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Repair or reinstall", service.MissingRuntimeStatus([unavailable, cuda], unavailable.Id), StringComparison.Ordinal);
    }


    [Fact]
    public void ModelLaunchHeadSelectionApplicationServiceOwnsPickerRequestsAndNormalization()
    {
        var root = CreateTempRoot();
        var modelsRoot = Path.Combine(root, "models");
        var modelFolder = Path.Combine(modelsRoot, "qwen");
        Directory.CreateDirectory(modelFolder);
        var modelPath = Path.Combine(modelFolder, "qwen.gguf");
        var projectorPath = Path.Combine(modelFolder, "mmproj.gguf");
        var mtpHeadPath = Path.Combine(modelFolder, "mtp-head.gguf");
        var dflashHeadPath = Path.Combine(modelFolder, "dflash-head.gguf");
        var model = new ModelRecord("model-1", "Qwen", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var service = new ModelLaunchHeadSelectionApplicationService();
        var requests = new List<OpenFilePickerRequest>();
        var applied = new List<string>();

        LaunchHeadSelectionActions Actions(string? selected)
            => new(
                request =>
                {
                    requests.Add(request);
                    return selected;
                },
                applied.Add);

        var embedded = service.ChooseVisionProjector(
            new LaunchHeadSelectionRequest(model, modelsRoot),
            Actions(modelPath));

        Assert.Equal(LaunchHeadSelectionOutcome.Applied, embedded);
        Assert.Equal(VisionProjectorSelection.EmbeddedToken, applied.Single());
        var visionRequest = requests.Single();
        Assert.Equal("Choose vision head/projector GGUF", visionRequest.Title);
        Assert.Equal("GGUF files|*.gguf|All files|*.*", visionRequest.Filter);
        Assert.True(visionRequest.CheckFileExists);
        Assert.False(visionRequest.AddExtension);
        Assert.Equal(".gguf", visionRequest.DefaultExt);
        Assert.Equal("", visionRequest.FileName);
        Assert.Equal(modelFolder, visionRequest.InitialDirectory);

        requests.Clear();
        applied.Clear();
        var external = service.ChooseVisionProjector(
            new LaunchHeadSelectionRequest(model, modelsRoot),
            Actions(projectorPath));

        Assert.Equal(LaunchHeadSelectionOutcome.Applied, external);
        Assert.Equal(projectorPath, applied.Single());

        requests.Clear();
        applied.Clear();
        var mtp = service.ChooseMtpHead(
            new LaunchHeadSelectionRequest(model, modelsRoot),
            Actions(mtpHeadPath));

        Assert.Equal(LaunchHeadSelectionOutcome.Applied, mtp);
        Assert.Equal(mtpHeadPath, applied.Single());
        Assert.Equal("Choose MTP head GGUF", requests.Single().Title);

        requests.Clear();
        applied.Clear();
        var dflash = service.ChooseDraftModel(
            new LaunchHeadSelectionRequest(model, modelsRoot),
            Actions(dflashHeadPath));

        Assert.Equal(LaunchHeadSelectionOutcome.Applied, dflash);
        Assert.Equal(dflashHeadPath, applied.Single());
        Assert.Equal("Choose speculative draft head GGUF", requests.Single().Title);

        requests.Clear();
        applied.Clear();
        var cancelled = service.ChooseMtpHead(
            new LaunchHeadSelectionRequest(null, modelsRoot),
            Actions(""));

        Assert.Equal(LaunchHeadSelectionOutcome.Cancelled, cancelled);
        Assert.Empty(applied);
        Assert.Equal(modelsRoot, requests.Single().InitialDirectory);
    }



    [Fact]
    public async Task LaunchSettingsRenderApplicationServiceAppliesRenderFlowAndSkipsStaleSelection()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081 };
        var model = new ModelRecord("model-1", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var other = model with { Id = "model-2", Name = "Llama" };
        var saved = ModelLaunchSettings.FromAppSettings(settings with { Port = 8095 }, "runtime-1");
        var viewState = new ModelLaunchSettingsViewState(
            model.Id,
            SavedProfile: saved,
            HasSavedProfile: true,
            RuntimeId: "runtime-1",
            LaunchSettings: saved.ApplyTo(settings),
            ProfileId: "profile-1",
            ProfileName: "Fast");
        var service = new LaunchSettingsRenderApplicationService();
        var selected = model;
        var selectedProfileId = "profile-1";
        var calls = new List<string>();

        LaunchSettingsRenderActions Actions(Func<ModelRecord, AppSettings, string, CancellationToken, Task<ModelLaunchSettingsViewState>> build)
            => new(
                () => selected,
                () => selectedProfileId,
                () => calls.Add("clear"),
                source => calls.Add($"name:{source?.Name ?? ""}"),
                build,
                state => calls.Add($"load:{state.ModelId}"),
                runtimeId => { calls.Add($"runtime:{runtimeId}"); return Task.CompletedTask; },
                launchSettings => calls.Add($"apply:{launchSettings.Port}"),
                (capabilityModel, _) =>
                {
                    calls.Add($"cap:{capabilityModel?.Id ?? ""}");
                    return Task.CompletedTask;
                },
                () => calls.Add("save"));

        await service.RenderSelectedAsync(
            null,
            settings,
            Actions((_, _, _, _) => throw new InvalidOperationException("No model render should not build a profile.")),
            TestContext.Current.CancellationToken);

        Assert.Equal(["clear", "name:", "runtime:", "apply:8081", "cap:", "save"], calls);

        calls.Clear();
        selected = model;
        await service.RenderSelectedAsync(
            model,
            settings,
            Actions((selectedModel, _, profileId, _) =>
            {
                calls.Add($"build:{selectedModel.Id}:{profileId}");
                return Task.FromResult(viewState);
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal(["name:Qwen", "build:model-1:profile-1", "load:model-1", "runtime:runtime-1", "apply:8095", "cap:model-1", "save"], calls);

        calls.Clear();
        selected = model;
        await service.RenderSelectedAsync(
            model,
            settings,
            Actions((selectedModel, _, profileId, _) =>
            {
                calls.Add($"build:{selectedModel.Id}:{profileId}");
                selected = other;
                return Task.FromResult(viewState);
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal(["name:Qwen", "build:model-1:profile-1"], calls);

        calls.Clear();
        selected = model;
        selectedProfileId = "profile-1";
        await service.RenderSelectedAsync(
            model,
            settings,
            Actions((selectedModel, _, profileId, _) =>
            {
                calls.Add($"build:{selectedModel.Id}:{profileId}");
                selectedProfileId = "profile-2";
                return Task.FromResult(viewState);
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal(["name:Qwen", "build:model-1:profile-1"], calls);
    }



    [Fact]
    public async Task ModelLaunchSettingsWorkflowBuildsDraftsSavesProfilesAndPreservesDefaultPort()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var model = new ModelRecord("model-1", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(model);
        using var sessions = CreateLoadedModelSessionManager();
        var profiles = new ModelLaunchProfileService(store, sessions);
        var workflow = new ModelLaunchSettingsWorkflowService(profiles);
        var defaults = AppSettings.CreateDefault(root) with
        {
            Port = 8081,
            ContextSize = 4096,
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082
        };

        var draft = await workflow.BuildAsync(model, defaults, TestContext.Current.CancellationToken);
        var savedProfile = await workflow.SaveForModelAsync(model, draft.LaunchSettings with { Port = 8099, ContextSize = 32768 }, "runtime-1", TestContext.Current.CancellationToken);
        var savedResult = await workflow.SaveProfileAsync(model, draft.LaunchSettings with { Port = 8100, ContextSize = 65536 }, "runtime-2", TestContext.Current.CancellationToken);
        var saved = await workflow.BuildAsync(model, defaults, TestContext.Current.CancellationToken);
        var appliedDefaults = ModelLaunchSettingsWorkflowService.ApplyLaunchDefaults(defaults, defaults with { Port = 9000, ContextSize = 65536 });
        var defaultsResult = ModelLaunchSettingsWorkflowService.SaveLaunchDefaults(defaults, defaults with { Port = 9000, ContextSize = 131072 });

        Assert.False(draft.HasSavedProfile);
        Assert.Null(draft.SavedProfile);
        Assert.Equal(8081, draft.LaunchSettings.Port);
        Assert.Equal("runtime-1", savedProfile.RuntimeId);
        Assert.Equal("runtime-2", savedResult.SavedSettings.RuntimeId);
        Assert.Equal("Saved default profile for Qwen.", savedResult.StatusMessage);
        Assert.True(saved.HasSavedProfile);
        Assert.Equal(8100, saved.LaunchSettings.Port);
        Assert.Equal(65536, saved.LaunchSettings.ContextSize);
        Assert.Equal("runtime-2", saved.RuntimeId);
        Assert.Equal(8081, appliedDefaults.Port);
        Assert.Equal(65536, appliedDefaults.ContextSize);
        Assert.Equal(8081, defaultsResult.Settings.Port);
        Assert.Equal(131072, defaultsResult.Settings.ContextSize);
        Assert.Equal("Launch defaults saved. Model ports stay per-model.", defaultsResult.StatusMessage);
    }


    [Fact]
    public async Task ModelLaunchProfileServiceAllocatesPortsAroundGatewayProfilesAndSessions()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = 8081,
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082
        };
        var runtime = new RuntimeRecord("runtime", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var target = new ModelRecord("target", "Target Model", Path.Combine(root, "target.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profiled = new ModelRecord("profiled", "Profiled Model", Path.Combine(root, "profiled.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var loaded = new ModelRecord("loaded", "Loaded Model", Path.Combine(root, "loaded.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(target);
        await store.UpsertModelAsync(profiled);
        await store.UpsertModelAsync(loaded);
        await store.SaveModelLaunchSettingsAsync(profiled.Id, ModelLaunchSettings.FromAppSettings(settings with { Port = 8081 }));
        await store.SaveModelLaunchSettingsAsync(target.Id, ModelLaunchSettings.FromAppSettings(settings with { Port = 8081 }));
        sessions.AttachExisting(runtime, loaded, settings with { Port = 8083 }, "loaded.log", LlamaRuntimeState.Loaded, "", "loaded-session", DateTimeOffset.UtcNow);

        var service = new ModelLaunchProfileService(store, sessions);
        var ensured = await service.EnsureAsync(target, settings);
        var saved = await store.GetModelLaunchSettingsAsync(target.Id);

        Assert.False(await service.IsPortAvailableAsync(target.Id, 8081, settings));
        Assert.False(await service.IsPortAvailableAsync(target.Id, 8082, settings));
        Assert.False(await service.IsPortAvailableAsync(target.Id, 8083, settings));
        Assert.True(await service.IsPortAvailableAsync(target.Id, 8084, settings));
        Assert.Equal(8084, ensured?.Port);
        Assert.Equal(8084, saved?.Port);
    }


    [Fact]
    public async Task ModelLaunchVariantWorkflowCreatesNamedProfileWithoutDuplicatingModelRecord()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = 8081,
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082
        };
        var source = new ModelRecord("qwen", "Qwen Test", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profiled = new ModelRecord("profiled", "Profiled Model", Path.Combine(root, "profiled.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(source);
        await store.UpsertModelAsync(profiled);
        await store.SaveModelLaunchSettingsAsync(profiled.Id, ModelLaunchSettings.FromAppSettings(settings with { Port = 8081 }));
        var profiles = new ModelLaunchProfileService(store, sessions);
        var workflow = new ModelLaunchVariantWorkflowService(profiles);

        var unchanged = await workflow.SaveAsNewAsync(new ModelLaunchVariantWorkflowRequest(
            source,
            "",
            settings,
            "runtime-cuda",
            settings),
            TestContext.Current.CancellationToken);
        var created = await workflow.SaveAsNewAsync(new ModelLaunchVariantWorkflowRequest(
            source,
            "Qwen Test 32K",
            settings with { ContextSize = 32768, Port = 9000 },
            "runtime-cuda",
            settings),
            TestContext.Current.CancellationToken);
        var duplicate = await workflow.SaveAsNewAsync(new ModelLaunchVariantWorkflowRequest(
            source,
            "Qwen Test 32K",
            settings,
            "runtime-cuda",
            settings),
            TestContext.Current.CancellationToken);
        var models = await store.ListModelsAsync();
        var named = Assert.Single(await store.ListNamedModelLaunchProfilesAsync(source.Id));
        var saved = named.Settings;

        Assert.False(unchanged.Success);
        Assert.Contains("Enter a name", unchanged.StatusMessage, StringComparison.Ordinal);
        Assert.True(created.Success);
        Assert.NotNull(created.Profile);
        Assert.Equal(2, models.Count);
        Assert.DoesNotContain(models, ModelAliasService.IsLaunchAlias);
        Assert.Equal("Qwen Test 32K", named.Name);
        Assert.Equal(source.Id, named.ModelId);
        Assert.Equal(8083, created.Port);
        Assert.Equal(8083, saved?.Port);
        Assert.Equal(32768, saved?.ContextSize);
        Assert.Equal("runtime-cuda", saved?.RuntimeId);
        Assert.False(duplicate.Success);
        Assert.Contains("already exists", duplicate.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task ModelLaunchProfileServiceCreatesOneProtectedDefaultProfilePerModel()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var defaults = AppSettings.CreateDefault(root) with
        {
            Port = 8081,
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082
        };
        var first = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var second = new ModelRecord("model-b", "Model B", Path.Combine(root, "b.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(first);
        await store.UpsertModelAsync(second);
        var service = new ModelLaunchProfileService(store, sessions);

        var created = await service.EnsureDefaultsAsync([first, second], defaults);
        var repeated = await service.EnsureDefaultsAsync([first, second], defaults);
        var stored = await store.ListNamedModelLaunchProfilesAsync();

        Assert.Equal(2, created.Count);
        Assert.All(created, profile =>
        {
            Assert.True(profile.IsDefault);
            Assert.Equal(ModelLaunchProfileService.DefaultProfileName, profile.Name);
        });
        Assert.Equal([8081, 8083], created.Select(profile => profile.Settings.Port).Order().ToArray());
        Assert.Equal(created.Select(profile => profile.Id), repeated.Select(profile => profile.Id));
        Assert.Equal(2, stored.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteNamedAsync(created[0].Id));
    }


    [Fact]
    public async Task ModelLaunchProfileServiceEditsAndRemovesDefaultWhileAlwaysRetainingOneProfile()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var defaults = AppSettings.CreateDefault(root) with { Port = 8081, ContextSize = 4096 };
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(model);
        var service = new ModelLaunchProfileService(store, sessions);
        var workflow = new ModelLaunchSettingsWorkflowService(service);
        var defaultProfile = await service.EnsureDefaultAsync(model, defaults);
        var tuned = new NamedModelLaunchProfile(
            "profile-tuned",
            model.Id,
            "Tuned",
            ModelLaunchSettings.FromAppSettings(defaults with { Port = 8082 }),
            DateTimeOffset.UtcNow.AddMinutes(1));
        await service.SaveNamedAsync(tuned);

        await workflow.SaveProfileAsync(
            model,
            defaults with { ContextSize = 32768 },
            "runtime-cpu",
            TestContext.Current.CancellationToken,
            defaultProfile.Id);
        var editedDefault = await store.GetNamedModelLaunchProfileAsync(defaultProfile.Id);
        Assert.NotNull(editedDefault);
        Assert.True(editedDefault.IsDefault);
        Assert.Equal(32768, editedDefault.Settings.ContextSize);

        var promoted = await service.DeleteNamedAsync(defaultProfile.Id);
        var onlyRemaining = Assert.Single(await service.ListNamedAsync(model));
        Assert.NotNull(promoted);
        Assert.Equal(tuned.Id, promoted.Id);
        Assert.Equal("Tuned", promoted.Name);
        Assert.True(promoted.IsDefault);
        Assert.True(onlyRemaining.IsDefault);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteNamedAsync(onlyRemaining.Id));

        var replacement = tuned with
        {
            Id = "profile-replacement",
            Name = "Replacement",
            IsDefault = false,
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(2)
        };
        await service.SaveNamedAsync(replacement);
        var nextDefault = await service.DeleteNamedAsync(onlyRemaining.Id);
        Assert.Equal(replacement.Id, nextDefault?.Id);
        Assert.True(Assert.Single(await service.ListNamedAsync(model)).IsDefault);
    }


    [Fact]
    public async Task ModelRuntimeLaunchPreparationOwnsApiKeyPortsPrerequisitesAndGatewayAdmission()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "model.gguf");
        File.WriteAllBytes(modelPath, new byte[1024 * 1024]);
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = 8084,
            ModelApiKey = "  existing-api-key  ",
            ContextSize = 131072,
            GpuLayers = AppSettings.DefaultGpuLayers
        };
        var model = new ModelRecord("model", "Big Model", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var cpuRuntime = new RuntimeRecord("runtime-cpu", "CPU", RuntimeMode.Native, RuntimeBackend.Cpu, CreateRuntimeExecutable(root), "{}", DateTimeOffset.UtcNow);
        var cudaRuntime = cpuRuntime with { Id = "runtime-cuda", Name = "CUDA", Backend = RuntimeBackend.Cuda };
        using var sessions = CreateLoadedModelSessionManager();
        var coordinator = new RuntimeSessionCoordinator(sessions, Path.Combine(root, "logs"));
        var portProbes = new List<int>();
        var prerequisites = new RuntimeLaunchPrerequisiteService(
            _ => Task.FromResult(ReadyWslReport()),
            () => WindowsBuildTools(),
            new ScriptedProcessRunner(_ => new ProcessRunResult(0, "ok", "")),
            (port, _) =>
            {
                portProbes.Add(port);
                return Task.FromResult(false);
            },
            () => "wsl.exe");
        var service = new ModelRuntimeLaunchPreparationService(
            coordinator,
            prerequisites,
            new RuntimeLaunchAdmissionService(new VramAdmissionService()),
            new GpuStatusProbeService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""))));

        var prepared = await service.PrepareAsync(new ModelRuntimeLaunchPreparationRequest(
            cpuRuntime,
            model,
            settings,
            InteractivePrompts: true,
            AutoLoadGatewayEnabled: true,
            AutoLoadGatewayPort: 8082,
            (launchSettings, _) => Task.FromResult(launchSettings with { ModelApiKey = launchSettings.ModelApiKey.Trim() }),
            (_, _) => Task.FromResult(false),
            (_, _) => throw new InvalidOperationException("CPU launch should not request admission confirmation.")),
            TestContext.Current.CancellationToken);

        sessions.AttachExisting(cudaRuntime, model with { Id = "loaded", Name = "Loaded Model" }, settings with { Port = 8081 }, "loaded.log", LlamaRuntimeState.Loaded, "", "loaded-session", DateTimeOffset.UtcNow);
        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PrepareAsync(new ModelRuntimeLaunchPreparationRequest(
                cudaRuntime,
                model,
                settings,
                InteractivePrompts: false,
                AutoLoadGatewayEnabled: true,
                AutoLoadGatewayPort: 8082,
                (launchSettings, _) => Task.FromResult(launchSettings),
                (_, _) => Task.FromResult(false),
                ReadMemoryAsync: _ => Task.FromResult<VramMemorySnapshot?>(new VramMemorySnapshot(0.1, 24))),
                TestContext.Current.CancellationToken));

        Assert.True(prepared.CanLaunch);
        Assert.Equal("existing-api-key", prepared.LaunchSettings.ModelApiKey);
        Assert.Equal([8084, 8084], portProbes);
        Assert.Contains("Auto-load gateway refused", blocked.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelRuntimeLaunchPreparationDoesNotApplyVramAdmissionForExistingCpuSession()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "model.gguf");
        File.WriteAllBytes(modelPath, new byte[1024 * 1024]);
        var settings = AppSettings.CreateDefault(root) with { Port = 8084 };
        var model = new ModelRecord("model", "GPU Model", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var cpuRuntime = new RuntimeRecord("runtime-cpu", "CPU", RuntimeMode.Native, RuntimeBackend.Cpu, CreateRuntimeExecutable(root), "{}", DateTimeOffset.UtcNow);
        var cudaRuntime = cpuRuntime with { Id = "runtime-cuda", Name = "CUDA", Backend = RuntimeBackend.Cuda };
        using var sessions = CreateLoadedModelSessionManager();
        sessions.AttachExisting(cpuRuntime, model with { Id = "cpu-model" }, settings with { Port = 8081 },
            "cpu.log", LlamaRuntimeState.Loaded, "", "cpu-session", DateTimeOffset.UtcNow);
        var service = new ModelRuntimeLaunchPreparationService(
            new RuntimeSessionCoordinator(sessions, Path.Combine(root, "logs")),
            new RuntimeLaunchPrerequisiteService(
                _ => Task.FromResult(ReadyWslReport()),
                () => WindowsBuildTools(),
                new ScriptedProcessRunner(_ => new ProcessRunResult(0, "ok", "")),
                (_, _) => Task.FromResult(false),
                () => "wsl.exe"),
            new RuntimeLaunchAdmissionService(new VramAdmissionService()),
            new GpuStatusProbeService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""))));

        var prepared = await service.PrepareAsync(new ModelRuntimeLaunchPreparationRequest(
            cudaRuntime,
            model,
            settings,
            InteractivePrompts: false,
            AutoLoadGatewayEnabled: false,
            AutoLoadGatewayPort: 8082,
            (launchSettings, _) => Task.FromResult(launchSettings),
            (_, _) => Task.FromResult(false),
            ReadMemoryAsync: _ => throw new InvalidOperationException("CPU sessions must not trigger a VRAM probe.")),
            TestContext.Current.CancellationToken);

        Assert.True(prepared.CanLaunch);
        Assert.True(sessions.HasRunningSessions);
        Assert.False(sessions.HasRunningGpuSessions);
    }


}
