using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeBuildExecutionTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task RuntimeBuildWorkflowServiceCompletesBuildAndPreservesSourceContext()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var preset = new RuntimeBuildPreset("official-cpu", "Official CPU", "https://example.com/llama.cpp.git", "master", false, Mode: RuntimeMode.Native);
        var source = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, Path.Combine(root, "source"), "abcdef123456", DateTimeOffset.UtcNow, Mode: RuntimeMode.Native);
        var plan = RuntimeBuildJobService.CreatePlan(preset, update: false, source, settings, new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero), "marker");
        var job = await jobs.CreateAsync("runtime-build", RuntimeBuildJobService.Payload(preset, plan.Action, plan.InstallDir, plan.QueuedMessage, plan.ProcessMarker, settings.WslDistro, source.SourceDir), TestContext.Current.CancellationToken);
        var executed = false;
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(plan.InstallDir, "llama-server.exe"), $$"""{"folder":"{{plan.InstallDir.Replace("\\", "\\\\")}}"}""", DateTimeOffset.UtcNow);
        var workflow = new RuntimeBuildWorkflowService(
            jobs,
            request =>
            {
                executed = true;
                Assert.Equal(source, request.Source);
                Assert.Equal(plan, request.Plan);
                return Task.FromResult(new RuntimeBuildExecutionResult(runtime, "", "Official CPU installed as official-cpu-20260528-100000."));
            },
            (_, _) => throw new InvalidOperationException("Update check should not run for a source build."));

        var result = await workflow.RunAsync(new RuntimeBuildWorkflowRequest(
            preset,
            settings,
            plan,
            source,
            job,
            Update: false,
            settings.WslDistro,
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken));
        var stored = Assert.Single(await store.ListJobsAsync());
        var payload = RuntimeBuildJobService.ParsePayload(stored.PayloadJson);
        var log = await File.ReadAllTextAsync(stored.LogPath, TestContext.Current.CancellationToken);

        Assert.True(executed);
        Assert.Equal(RuntimeBuildWorkflowResultKind.Completed, result.Kind);
        Assert.Equal(JobStatus.Completed, stored.Status);
        Assert.NotNull(payload);
        Assert.Equal("build", payload.Action);
        Assert.Equal(source.SourceDir, payload.SourceDir);
        Assert.Equal(settings.WslDistro, payload.WslDistro);
        Assert.Contains("Building downloaded source", log, StringComparison.Ordinal);
        Assert.Contains("installed as official-cpu", log, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeBuildWorkflowServiceCompletesNoUpdateWithoutExecutingBuild()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var preset = new RuntimeBuildPreset("official-cpu", "Official CPU", "https://example.com/llama.cpp.git", "master", false, Mode: RuntimeMode.Native);
        var plan = RuntimeBuildJobService.CreatePlan(preset, update: true, source: null, settings, new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero), "marker");
        var job = await jobs.CreateAsync("runtime-build", RuntimeBuildJobService.Payload(preset, plan.Action, plan.InstallDir, plan.QueuedMessage, plan.ProcessMarker, settings.WslDistro), TestContext.Current.CancellationToken);
        var workflow = new RuntimeBuildWorkflowService(
            jobs,
            _ => throw new InvalidOperationException("Build should not execute when remote commit matches."),
            (_, _) => Task.FromResult(new RuntimeSourceUpdateCheck(IsInstalled: true, HasUpdate: false, LocalCommit: "abcdef123456", RemoteCommit: "abcdef123456")));

        var result = await workflow.RunAsync(new RuntimeBuildWorkflowRequest(
            preset,
            settings,
            plan,
            Source: null,
            job,
            Update: true,
            settings.WslDistro,
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken));
        var stored = Assert.Single(await store.ListJobsAsync());
        var payload = RuntimeBuildJobService.ParsePayload(stored.PayloadJson);
        var log = await File.ReadAllTextAsync(stored.LogPath, TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeBuildWorkflowResultKind.NoUpdate, result.Kind);
        Assert.Equal(JobStatus.Completed, stored.Status);
        Assert.NotNull(payload);
        Assert.Equal("update", payload.Action);
        Assert.Contains("No new build was created", payload.Message, StringComparison.Ordinal);
        Assert.Contains("Checking remote repository", log, StringComparison.Ordinal);
        Assert.Contains("No new build was created", log, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeBuildApplicationServiceCoordinatesBuildAndNoUpdateResults()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var runner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""));
        var cancellations = new RuntimeBuildCancellationRegistry();
        var controls = new RuntimeBuildJobControlService(
            store,
            jobs,
            new RuntimeBuildMarkerService(runner),
            cancellations,
            root);
        var prerequisites = new RuntimeBuildPrerequisiteService(new RuntimeToolPrerequisiteService(
            _ => throw new InvalidOperationException("WSL readiness is not expected for native build application tests."),
            () => WindowsBuildTools(),
            runner,
            () => "wsl.exe"));
        var preset = new RuntimeBuildPreset("app-build-cpu", "App Build CPU", "https://example.com/llama.cpp.git", "master", false, Mode: RuntimeMode.Native);
        var source = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, Path.Combine(root, "source"), "abcdef123456", DateTimeOffset.UtcNow, Mode: RuntimeMode.Native);
        var completedWorkflow = new RuntimeBuildWorkflowService(
            jobs,
            request => Task.FromResult(new RuntimeBuildExecutionResult(
                new RuntimeRecord("runtime-app-build", "App Build Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(request.Plan.InstallDir, "llama-server.exe"), "{}", DateTimeOffset.UtcNow),
                "",
                "App Build CPU installed.")),
            (_, _) => throw new InvalidOperationException("Update check should not run for source builds."));
        var catalogData = new RuntimeCatalogDataService();
        var completedService = new RuntimeBuildApplicationService(jobs, prerequisites, completedWorkflow, controls, catalogData);
        var noUpdateWorkflow = new RuntimeBuildWorkflowService(
            jobs,
            _ => throw new InvalidOperationException("Build should not run when runtime is already current."),
            (_, _) => Task.FromResult(new RuntimeSourceUpdateCheck(IsInstalled: true, HasUpdate: false, LocalCommit: "abcdef123456", RemoteCommit: "abcdef123456")));
        var noUpdateService = new RuntimeBuildApplicationService(jobs, prerequisites, noUpdateWorkflow, controls, catalogData);
        var busyMessages = new List<string>();
        var statuses = new List<string>();
        var infoMessages = new List<string>();
        var runtimeRefreshes = 0;
        var overviewRefreshes = 0;
        RuntimeBuildApplicationActions Actions() => new(
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
            statuses.Add,
            (title, message) => infoMessages.Add($"{title}: {message}"));

        var completed = await completedService.BuildSourceAsync(
            source,
            settings,
            BoundedLogFile.MegabytesToBytes(1),
            Actions());
        var noUpdate = await noUpdateService.BuildAsync(
            new RuntimeBuildApplicationRequest(
                preset,
                settings,
                true,
                null,
                BoundedLogFile.MegabytesToBytes(1),
                new DateTimeOffset(2026, 5, 28, 12, 0, 0, TimeSpan.Zero)),
            Actions());
        var storedJobs = (await store.ListJobsAsync()).OrderBy(job => job.CreatedAt).ToList();
        var completedPayload = RuntimeBuildJobService.ParsePayload(storedJobs[0].PayloadJson);
        var noUpdatePayload = RuntimeBuildJobService.ParsePayload(storedJobs[1].PayloadJson);

        Assert.Equal(RuntimeBuildApplicationOutcome.Completed, completed);
        Assert.Equal(RuntimeBuildApplicationOutcome.NoUpdate, noUpdate);
        Assert.Equal(["Building App Build CPU...", "Updating App Build CPU..."], busyMessages);
        Assert.Equal(["App Build CPU installed."], statuses);
        Assert.Contains(infoMessages, message => message.Contains("Runtime update", StringComparison.Ordinal)
            && message.Contains("No new build was created", StringComparison.Ordinal));
        Assert.Equal(2, storedJobs.Count);
        Assert.All(storedJobs, job => Assert.Equal(JobStatus.Completed, job.Status));
        Assert.NotNull(completedPayload);
        Assert.NotNull(noUpdatePayload);
        Assert.Equal("build", completedPayload.Action);
        Assert.Equal(source.SourceDir, completedPayload.SourceDir);
        Assert.Equal(settings.WslDistro, completedPayload.WslDistro);
        Assert.Equal("update", noUpdatePayload.Action);
        Assert.Equal("", noUpdatePayload.SourceDir);
        Assert.Equal(1, runtimeRefreshes);
        Assert.Equal(2, overviewRefreshes);
        Assert.Equal(0, cancellations.ActiveCount);
    }


    [Fact]
    public void RuntimeBuildToolServiceBuildsHiddenPowerShellCommand()
    {
        var preset = new RuntimeBuildPreset("custom-cuda", "Custom CUDA", "https://example.com/repo.git", "feature/runtime", true, Custom: true);

        var psi = RuntimeBuildToolService.CreateBuildProcessStartInfo(
            "powershell.exe",
            @"D:\tools\Build-LlamaCppRuntime.ps1",
            @"D:\cache\source",
            @"D:\cache\build",
            @"D:\runtimes\install",
            preset,
            RuntimeMode.Wsl,
            "Ubuntu-24.04",
            "marker-1",
            @"C:\Windows\System32\wsl.exe",
            "",
            "",
            noUpdate: true);
        var args = psi.ArgumentList.ToArray();

        Assert.Equal("powershell.exe", psi.FileName);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.RedirectStandardOutput);
        Assert.Contains("-RepoUrl", args);
        Assert.Contains("https://example.com/repo.git", args);
        Assert.Contains("-Branch", args);
        Assert.Contains("feature/runtime", args);
        Assert.Contains("-WslDistro", args);
        Assert.Contains("Ubuntu-24.04", args);
        Assert.Contains("-ProcessMarker", args);
        Assert.Contains("marker-1", args);
        Assert.Contains("-Cuda", args);
        Assert.Contains("-NoUpdate", args);
        Assert.Contains("-Clean", args);

        var vulkanPreset = new RuntimeBuildPreset("official-vulkan", "Official Vulkan", "https://example.com/repo.git", "master", false, Backend: "vulkan");
        var vulkanPsi = RuntimeBuildToolService.CreateBuildProcessStartInfo(
            "powershell.exe",
            @"D:\tools\Build-LlamaCppRuntime.ps1",
            @"D:\cache\source",
            @"D:\cache\build",
            @"D:\runtimes\install",
            vulkanPreset,
            RuntimeMode.Wsl,
            "Ubuntu-24.04",
            "marker-2",
            @"C:\Windows\System32\wsl.exe",
            "",
            "",
            noUpdate: false);
        var vulkanArgs = vulkanPsi.ArgumentList.ToArray();

        Assert.Contains("-Vulkan", vulkanArgs);
        Assert.DoesNotContain("-Cuda", vulkanArgs);

        var syclPreset = new RuntimeBuildPreset("official-sycl", "Official SYCL", "https://example.com/repo.git", "master", false, Backend: "sycl");
        var syclPsi = RuntimeBuildToolService.CreateBuildProcessStartInfo(
            "powershell.exe",
            @"D:\tools\Build-LlamaCppRuntime.ps1",
            @"D:\cache\source",
            @"D:\cache\build",
            @"D:\runtimes\install",
            syclPreset,
            RuntimeMode.Wsl,
            "Ubuntu-24.04",
            "marker-3",
            @"C:\Windows\System32\wsl.exe",
            "",
            "",
            noUpdate: false);
        var syclArgs = syclPsi.ArgumentList.ToArray();

        Assert.Contains("-Sycl", syclArgs);
        Assert.DoesNotContain("-Cuda", syclArgs);
        Assert.DoesNotContain("-Vulkan", syclArgs);

        var nativePreset = new RuntimeBuildPreset("official-windows-cpu", "Official CPU Windows", "https://example.com/repo.git", "master", false, Mode: RuntimeMode.Native);
        var nativePsi = RuntimeBuildToolService.CreateBuildProcessStartInfo(
            "powershell.exe",
            @"D:\tools\Build-LlamaCppRuntime.ps1",
            @"D:\cache\source",
            @"D:\cache\build",
            @"D:\runtimes\install",
            nativePreset,
            RuntimeMode.Native,
            "",
            "",
            "",
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files\CMake\bin\cmake.exe",
            noUpdate: false);
        var nativeArgs = nativePsi.ArgumentList.ToArray();

        Assert.Contains("-Runtime", nativeArgs);
        Assert.Contains("native", nativeArgs);
        Assert.Contains("-GitExe", nativeArgs);
        Assert.Contains(@"C:\Program Files\Git\cmd\git.exe", nativeArgs);
        Assert.Contains("-CMakeExe", nativeArgs);
        Assert.DoesNotContain("-WslDistro", nativeArgs);
        Assert.DoesNotContain("-WslExe", nativeArgs);
    }


    [Fact]
    public async Task RuntimeBuildExecutionServiceRunsNativeBuildRegistersRuntimeAndDeletesSource()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var runtimes = new RuntimeRegistryService(store);
        var settings = AppSettings.CreateDefault(root) with { DeleteRuntimeSourceAfterSuccessfulBuild = true };
        var preset = new RuntimeBuildPreset("official-windows-cpu", "Official CPU Windows", "https://example.com/repo.git", "master", false, Mode: RuntimeMode.Native);
        var sourceDir = Path.Combine(settings.RuntimeRoot, "runtime-sources", preset.Id);
        Directory.CreateDirectory(sourceDir);
        var source = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, sourceDir, "abcdef123456", DateTimeOffset.UtcNow, Mode: RuntimeMode.Native);
        var plan = RuntimeBuildJobService.CreatePlan(preset, update: false, source, settings, new DateTimeOffset(2026, 5, 28, 10, 11, 12, TimeSpan.Zero), "native-marker");
        var runner = new ScriptedProcessRunner(psi =>
        {
            var args = psi.ArgumentList.ToArray();
            var installDir = args[Array.IndexOf(args, "-InstallDir") + 1];
            Directory.CreateDirectory(installDir);
            File.WriteAllText(Path.Combine(installDir, "llama-server.exe"), "");
            return new ProcessRunResult(0, "native build output", "native build warning");
        });
        var markers = new RuntimeBuildMarkerService(runner);
        var service = new RuntimeBuildExecutionService(root, runner, runtimes, markers);
        var logPath = Path.Combine(root, "logs", "runtime-build.log");

        var result = await service.ExecuteAsync(new RuntimeBuildExecutionRequest(preset, settings, plan, source, logPath, false, TestContext.Current.CancellationToken));
        var registered = Assert.Single(await store.ListRuntimesAsync());
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(plan.InstallDir, "local-llm-runtime.json"), TestContext.Current.CancellationToken))!.AsObject();
        var log = await File.ReadAllTextAsync(logPath, TestContext.Current.CancellationToken);
        var buildArgs = runner.Commands.Single(command => command.Contains("-InstallDir", StringComparer.Ordinal));

        Assert.Equal(registered.Id, result.Runtime.Id);
        Assert.Equal(RuntimeMode.Native, registered.Mode);
        Assert.False(Directory.Exists(sourceDir));
        Assert.Equal("official-windows-cpu", metadata["managedPresetId"]?.ToString());
        Assert.Equal("native build output", log.Split(Environment.NewLine, StringSplitOptions.None)[0]);
        Assert.Contains("Deleted downloaded source", log, StringComparison.Ordinal);
        Assert.Contains("-NoUpdate", buildArgs);
        Assert.Equal(0, markers.ActiveMarkerCount);
        Assert.Contains("Downloaded source deleted", result.StatusMessage, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeBuildExecutionServiceCleansWslMarkerWhenBuildFails()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root) with { WslDistro = "Ubuntu-24.04" };
        var preset = new RuntimeBuildPreset("official-cpu", "Official CPU WSL", "https://example.com/repo.git", "master", false);
        var plan = RuntimeBuildJobService.CreatePlan(preset, update: true, source: null, settings, new DateTimeOffset(2026, 5, 28, 10, 11, 12, TimeSpan.Zero), "wsl-marker");
        var runner = new ScriptedProcessRunner(psi =>
        {
            var args = psi.ArgumentList.ToArray();
            if (args.Contains("-d", StringComparer.Ordinal) && args.Contains("Ubuntu-24.04", StringComparer.Ordinal))
                return new ProcessRunResult(0, "cleanup", "");
            return new ProcessRunResult(1, "", "build failed");
        });
        var markers = new RuntimeBuildMarkerService(runner);
        var service = new RuntimeBuildExecutionService(root, runner, new RuntimeRegistryService(store), markers);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(new RuntimeBuildExecutionRequest(preset, settings, plan, null, Path.Combine(root, "logs", "runtime-build.log"), true, TestContext.Current.CancellationToken)));
        var cleanupCommand = runner.Commands.Single(command => command.Contains("-d", StringComparer.Ordinal) && command.Contains("Ubuntu-24.04", StringComparer.Ordinal));

        Assert.Contains("build failed", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, markers.ActiveMarkerCount);
        Assert.Contains("wsl-marker", string.Join(" ", cleanupCommand), StringComparison.Ordinal);
        Assert.Empty(await store.ListRuntimesAsync());
    }


}
