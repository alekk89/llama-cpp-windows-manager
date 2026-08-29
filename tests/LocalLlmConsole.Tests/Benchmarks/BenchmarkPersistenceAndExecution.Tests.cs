using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class BenchmarkPersistenceAndExecutionTests : ManagerRegressionTestBase
{
    private const string ResultJson = """{"build_commit":"commit-a","build_number":42,"cpu_info":"CPU","gpu_info":"GPU","backends":"CUDA","devices":"CUDA0","model_filename":"m.gguf","model_type":"model","model_size":100,"model_n_params":200,"n_prompt":512,"n_gen":0,"n_depth":0,"n_batch":2048,"n_ubatch":512,"n_threads":8,"n_gpu_layers":-1,"n_cpu_moe":0,"type_k":"f16","type_v":"f16","split_mode":"layer","main_gpu":0,"no_kv_offload":false,"flash_attn":"on","tensor_split":"","load_mode":"mmap","avg_ns":1000,"stddev_ns":20,"avg_ts":500.25,"stddev_ts":1.5,"test_time":"2026-08-27T00:00:00Z","future_field":"preserved"}""";

    [Fact]
    public async Task BenchmarkRowsRoundTripSignaturesPartialStateAndCascade()
    {
        var root = CreateTempRoot();
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var job = await new JobEngine(store, Path.Combine(root, "logs")).CreateAsync(BenchmarkApplicationService.JobKind, "{}", cancellationToken);
        Assert.True(BenchmarkResultService.TryParse(
            ResultJson, "model-fingerprint", "command-signature", RuntimeMode.Native, RuntimeBackend.Cuda,
            out var parsed, out var error, "v2.5.0", "Windows test host"), error);

        await store.InsertBenchmarkResultAsync(job.Id, "item", 1, 1, parsed!, cancellationToken);
        var partial = Assert.Single(await store.ListBenchmarkResultsAsync(job.Id, cancellationToken: cancellationToken));
        Assert.True(partial.IsPartialAttempt);
        Assert.Equal(parsed!.WorkloadSignature, partial.Result.WorkloadSignature);
        Assert.Equal(parsed.EnvironmentSignature, partial.Result.EnvironmentSignature);
        Assert.Equal("v2.5.0", partial.Result.ManagerVersion);
        Assert.Equal("Windows test host", partial.Result.OperatingEnvironment);
        Assert.Contains("future_field", partial.Result.RawJson, StringComparison.Ordinal);
        Assert.Equal(job.Id, Assert.Single(await store.ListBenchmarkJobsAsync(10, 0, cancellationToken)).Id);

        await store.CompleteBenchmarkAttemptAsync(job.Id, "item", 1, cancellationToken);
        var complete = Assert.Single(await store.ListBenchmarkResultsAsync(job.Id, includePartialAttempts: false, cancellationToken: cancellationToken));
        Assert.False(complete.IsPartialAttempt);

        await store.DeleteJobAsync(job.Id);
        Assert.Equal(0, await store.CountBenchmarkResultsAsync(job.Id, cancellationToken));
    }

    [Fact]
    public void NativeAdapterPreservesLogicalArgumentBoundaries()
    {
        var root = CreateTempRoot();
        var executable = CreateRuntimeExecutable(root, "bin", "llama-bench.exe");
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu,
            Path.Combine(root, "bin", "llama-server.exe"), "{}", DateTimeOffset.UtcNow);

        var start = BenchmarkRuntimeToolAdapter.CreateStartInfo(runtime, "", executable, ["--model", @"D:\Models\a model.gguf", "--output", "jsonl"], "");

        Assert.Equal(executable, start.FileName);
        Assert.Equal(new[] { "--model", @"D:\Models\a model.gguf", "--output", "jsonl" }, start.ArgumentList.Cast<string>());
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
    }

    [Fact]
    public void WslAdapterQuotesPathsArgumentsAndAddsUniqueMarkerEnvironment()
    {
        var root = Path.Combine(@"D:\runtime's", "bin");
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Wsl, RuntimeBackend.Sycl,
            Path.Combine(root, "llama-server"), "{}", DateTimeOffset.UtcNow);
        var executable = Path.Combine(root, "llama-bench");

        var start = BenchmarkRuntimeToolAdapter.CreateStartInfo(runtime, "Ubuntu-24.04", executable,
            ["--model", @"D:\models\a model.gguf", "--output", "jsonl"], "benchmark-marker");
        var arguments = start.ArgumentList.Cast<string>().ToArray();
        var command = arguments[^1];

        Assert.Equal(new[] { "-d", "Ubuntu-24.04", "--", "bash", "-lc" }, arguments[..^1]);
        Assert.Contains("ONEAPI_DEVICE_SELECTOR=level_zero:gpu", command, StringComparison.Ordinal);
        Assert.Contains("LD_LIBRARY_PATH=", command, StringComparison.Ordinal);
        Assert.Contains("exec -a 'benchmark-marker'", command, StringComparison.Ordinal);
        Assert.Contains("a model.gguf", command, StringComparison.Ordinal);
        Assert.Contains("'\"'\"'", command, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WslModelValidationUsesLinuxReadabilityCheck()
    {
        ProcessStartInfo? observed = null;
        var runner = new ScriptedProcessRunner(startInfo =>
        {
            observed = startInfo;
            return new ProcessRunResult(0, "", "");
        });
        var service = new BenchmarkCapabilityService(runner);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Wsl, RuntimeBackend.Cpu,
            @"D:\runtime\bin\llama-server", "{}", DateTimeOffset.UtcNow);

        var error = await service.ValidateModelPathAsync(runtime, "Ubuntu", @"D:\models\model.gguf", TestContext.Current.CancellationToken);

        Assert.Equal("", error);
        Assert.NotNull(observed);
        Assert.Equal("Ubuntu", observed!.ArgumentList[1]);
        Assert.Contains("test -f '/mnt/d/models/model.gguf' && test -r '/mnt/d/models/model.gguf'", observed.ArgumentList[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeProcessRunnerStreamsJsonlAndDiagnostics()
    {
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu,
            HostExecutableResolver.WindowsPowerShellExe(), "{}", DateTimeOffset.UtcNow);
        var rows = new List<string>();
        var diagnostics = new List<string>();
        var processRunner = new BenchmarkProcessRunner(new WslRuntimeStopService(
            new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""))));

        var result = await processRunner.RunAsync(
            runtime,
            "",
            HostExecutableResolver.WindowsPowerShellExe(),
            ["-NoProfile", "-Command", "$ErrorActionPreference='Stop'; [Console]::Out.WriteLine('{\"n_prompt\":512,\"n_gen\":0}'); [Console]::Error.WriteLine('progress 1/1')"],
            line => { rows.Add(line); return Task.CompletedTask; },
            diagnostics.Add,
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(rows);
        Assert.Contains("n_prompt", rows[0], StringComparison.Ordinal);
        Assert.Contains("progress 1/1", result.DiagnosticTail, StringComparison.Ordinal);
        Assert.Contains("progress 1/1", diagnostics);
    }

    [Fact]
    public async Task NativeProcessRunnerStopsProcessWhenResultConsumerFails()
    {
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu,
            HostExecutableResolver.WindowsPowerShellExe(), "{}", DateTimeOffset.UtcNow);
        var processRunner = new BenchmarkProcessRunner(new WslRuntimeStopService(
            new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""))));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => processRunner.RunAsync(
            runtime,
            "",
            HostExecutableResolver.WindowsPowerShellExe(),
            ["-NoProfile", "-Command", "[Console]::Out.WriteLine('row'); Start-Sleep -Seconds 30"],
            _ => throw new InvalidOperationException("Synthetic persistence failure"),
            onDiagnostic: null,
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Contains("Synthetic persistence failure", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnattendedRunPersistsRowsOwnsComputeAndRejectsSecondRun()
    {
        var root = CreateTempRoot();
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtimeFolder = Path.Combine(root, "runtime", "bin");
        Directory.CreateDirectory(runtimeFolder);
        CopyFakeRuntime(runtimeFolder);
        var server = Path.Combine(runtimeFolder, "llama-server.exe");
        var benchmark = Path.Combine(runtimeFolder, "llama-bench.exe");
        File.Copy(Path.Combine(runtimeFolder, "LocalLlmConsole.FakeRuntime.exe"), server, overwrite: true);
        File.Copy(Path.Combine(runtimeFolder, "LocalLlmConsole.FakeRuntime.exe"), benchmark, overwrite: true);
        var modelPath = Path.Combine(root, "model.gguf");
        WriteMinimalGguf(modelPath);

        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var runtime = new RuntimeRecord("runtime", "Fake runtime", RuntimeMode.Native, RuntimeBackend.Cpu, server, "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model", "Fake model", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);
        await store.UpsertModelAsync(model);
        await store.SaveModelLaunchSettingsAsync(model.Id,
            ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root), runtime.Id) with { Threads = 4, BatchSize = 2048, MicroBatchSize = 512 });

        using var sessions = CreateLoadedModelSessionManager();
        var tracked = new TrackedProcessRunner();
        await using var service = new BenchmarkApplicationService(
            store,
            new JobEngine(store, Path.Combine(root, "logs")),
            sessions,
            new BenchmarkCapabilityService(tracked),
            new BenchmarkProcessRunner(new WslRuntimeStopService(tracked)));
        var plan = new BenchmarkPlan
        {
            AllModels = true,
            AllProfiles = true,
            UseProfileRuntime = true,
            PromptSizes = [512],
            GenerationSizes = [128],
            Repetitions = 1,
            PreventSystemSleep = false,
            Options = new BenchmarkOptionSet { AdditionalArguments = ["--fake-delay-ms", "300"] }
        };

        var started = await service.StartAsync(plan, confirmed: true, cancellationToken);
        await WaitUntilAsync(() => sessions.HasBenchmarkLease, cancellationToken);
        var lifecycleError = await Assert.ThrowsAsync<InvalidOperationException>(() => sessions.ExecuteLifecycleAsync(() => Task.CompletedTask, cancellationToken));
        Assert.Contains("benchmark", lifecycleError.Message, StringComparison.OrdinalIgnoreCase);
        var secondError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(plan, confirmed: true, cancellationToken));
        Assert.Contains("already active", secondError.Message, StringComparison.OrdinalIgnoreCase);

        var completed = await WaitForTerminalAsync(service, started.Job.Id, cancellationToken);
        Assert.True(completed.Job.Status == JobStatus.Completed, completed.Payload.Message);
        Assert.Equal(BenchmarkRunOutcome.Success, completed.Payload.Outcome);
        Assert.Equal(2, completed.PersistedResultRows);
        Assert.Equal(2, (await store.ListBenchmarkResultsAsync(started.Job.Id, includePartialAttempts: false, cancellationToken: cancellationToken)).Count);
        Assert.False(sessions.HasBenchmarkLease);
        Assert.Equal(0, service.ActiveQueueTaskCount);
    }

    [Fact]
    public async Task ProfileServingRunLaunchesSavedSpeculativeProfileAndPersistsAcceptance()
    {
        var root = CreateTempRoot();
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtimeFolder = Path.Combine(root, "runtime", "bin");
        Directory.CreateDirectory(runtimeFolder);
        CopyFakeRuntime(runtimeFolder);
        var server = Path.Combine(runtimeFolder, "llama-server.exe");
        File.Copy(Path.Combine(runtimeFolder, "LocalLlmConsole.FakeRuntime.exe"), server, overwrite: true);
        var modelPath = Path.Combine(root, "model.gguf");
        await File.WriteAllBytesAsync(modelPath, [1], cancellationToken);
        using var portLease = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        portLease.Start();
        var port = ((System.Net.IPEndPoint)portLease.LocalEndpoint).Port;
        portLease.Stop();

        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var runtime = new RuntimeRecord("runtime", "Fake runtime", RuntimeMode.Native, RuntimeBackend.Cpu, server, "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model", "Fake model", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);
        await store.UpsertModelAsync(model);
        var profile = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root), runtime.Id) with
        {
            Port = port,
            ContextSize = 2048,
            BatchSize = 2048,
            MicroBatchSize = 512,
            ParallelSlots = 2,
            SpeculativeType = "draft-mtp",
            SpecDraftModelPath = modelPath,
            SpecDraftMaxTokens = 4
        };
        await store.SaveModelLaunchSettingsAsync(model.Id, profile);

        using var sessions = CreateLoadedModelSessionManager();
        var tracked = new TrackedProcessRunner();
        await using var service = new BenchmarkApplicationService(
            store,
            new JobEngine(store, Path.Combine(root, "logs")),
            sessions,
            new BenchmarkCapabilityService(tracked),
            new BenchmarkProcessRunner(new WslRuntimeStopService(tracked)),
            root);
        var plan = new BenchmarkPlan
        {
            ExecutionMode = BenchmarkExecutionMode.ProfileServing,
            AllModels = true,
            AllProfiles = true,
            UseProfileRuntime = true,
            PromptSizes = [32],
            GenerationSizes = [8],
            Repetitions = 2,
            Warmup = true,
            PreventSystemSleep = false,
            Options = new BenchmarkOptionSet { BatchSizes = [1024] },
            Serving = new BenchmarkServingOptions { ContextSizes = [4096], Concurrencies = [1, 2], ReadyTimeoutSeconds = 30, RequestTimeoutSeconds = 30 }
        };

        var started = await service.StartAsync(plan, confirmed: true, cancellationToken);
        var completed = await WaitForTerminalAsync(service, started.Job.Id, cancellationToken);
        var rows = await store.ListBenchmarkResultsAsync(started.Job.Id, includePartialAttempts: false, cancellationToken: cancellationToken);

        Assert.True(completed.Job.Status == JobStatus.Completed, completed.Payload.Message);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(BenchmarkExecutionMode.ProfileServing, row.Result.ExecutionMode);
            Assert.Equal("draft-mtp", row.Result.SpeculativeType);
            Assert.Equal(4096, row.Result.ContextSize);
            Assert.Equal(1024, row.Result.BatchSize);
            Assert.Equal(512, row.Result.MicroBatchSize);
            Assert.True(row.Result.SpeculativeMetricsObserved);
            Assert.Equal(50, row.Result.DraftAcceptancePercent);
            Assert.True(row.Result.DraftTokens > 0);
        });
        Assert.False(sessions.HasRunningSessions);
        Assert.False(sessions.HasBenchmarkLease);
    }

    [Fact]
    public async Task ServingReadinessTimeoutCancelsAStalledHttpRequest()
    {
        using var http = new HttpClient(new StalledHttpHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        using var runner = new BenchmarkServingRunner(http);
        var stopwatch = Stopwatch.StartNew();

        var failure = await Assert.ThrowsAsync<TimeoutException>(() => runner.WaitUntilReadyAsync(
            "http://127.0.0.1:1",
            AppSettings.CreateDefault(CreateTempRoot()),
            TimeSpan.FromMilliseconds(75),
            TestContext.Current.CancellationToken));

        stopwatch.Stop();
        Assert.Contains("0.075 seconds", failure.Message, StringComparison.Ordinal);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(2));
    }

    private static void CopyFakeRuntime(string destination)
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("LocalLlmConsole.sln"))!;
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Debug" : "Release";
        var source = Path.Combine(repositoryRoot, "tests", "LocalLlmConsole.FakeRuntime", "bin", configuration, "net10.0-windows");
        Assert.True(Directory.Exists(source), $"Fake runtime output was not built: {source}");
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }

    private static async Task<BenchmarkRunSnapshot> WaitForTerminalAsync(
        BenchmarkApplicationService service,
        string jobId,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        long revision = -1;
        while (true)
        {
            var snapshot = await service.WaitForRevisionAsync(jobId, revision, TimeSpan.FromSeconds(2), timeout.Token);
            revision = snapshot.Payload.Revision;
            if (snapshot.Job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Interrupted)
                return snapshot;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }

    private sealed class StalledHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The stalled request should be cancelled.");
        }
    }
}
