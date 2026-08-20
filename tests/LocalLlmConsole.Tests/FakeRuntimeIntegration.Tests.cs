using System.Net;
using System.Net.Sockets;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public async Task NativeSupervisorLaunchesAuthenticatedRuntimeProbesItAndStopsItCleanly()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "fixture-model.gguf");
        await File.WriteAllTextAsync(modelPath, "deterministic fake GGUF", TestContext.Current.CancellationToken);
        var runtimePath = FakeRuntimeExecutable();
        var port = FreeFakeRuntimePort();
        const string apiKey = "fake-runtime-test-key-0123456789abcdef";
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = port,
            Host = "127.0.0.1",
            ModelApiKey = apiKey,
            ModelApiKeyBackup = apiKey,
            RequireApiKeyAuth = true,
            GpuLayers = 0,
            EnableMetrics = true
        };
        var runtime = new RuntimeRecord(
            "fake-native-runtime",
            "Fake native runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            runtimePath,
            "{\"fixture\":true}",
            DateTimeOffset.UtcNow);
        var model = new ModelRecord(
            "fixture-model",
            "Fixture Model",
            modelPath,
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);

        using var supervisor = CreateTestLlamaSupervisor();
        await supervisor.StartAsync(runtime, model, settings, Path.Combine(root, "logs"));

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var probe = new RuntimeEndpointProbeService(http);
            await WaitForFakeRuntimeAsync(() => probe.IsAliveAsync(settings, TestContext.Current.CancellationToken));

            Assert.True(supervisor.IsRunning);
            Assert.Equal(LlamaRuntimeState.Loaded, supervisor.State);
            Assert.True(supervisor.ProcessId > 0);
            Assert.Equal(model.Id, supervisor.ActiveModelId);
            Assert.Equal(runtime.Id, supervisor.ActiveRuntimeId);
            Assert.Equal(["fixture-model"], await probe.ServedModelsAsync(settings, TestContext.Current.CancellationToken));

            using var unauthenticated = await http.GetAsync(
                $"http://127.0.0.1:{port}/health",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

            await WaitForFakeRuntimeAsync(() => Task.FromResult(File.Exists(supervisor.LogPath)));
        }
        finally
        {
            var stop = await supervisor.StopVerifiedAsync(TestContext.Current.CancellationToken);
            Assert.True(stop.VerifiedStopped, stop.Error);
        }

        Assert.False(supervisor.IsRunning);
        Assert.Equal(LlamaRuntimeState.Stopped, supervisor.State);
        Assert.False(await EndpointRespondsAsync(port));
        var log = await File.ReadAllTextAsync(
            Directory.GetFiles(Path.Combine(root, "logs"), "llama-server-*.log").Single(),
            TestContext.Current.CancellationToken);
        Assert.Contains("HTTP server listening", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(apiKey, log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeSupervisorSurfacesFakeRuntimeCrashWithoutLeavingAProcess()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "crash-model.gguf");
        await File.WriteAllTextAsync(modelPath, "fake", TestContext.Current.CancellationToken);
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = FreeFakeRuntimePort(),
            GpuLayers = 0,
            ModelApiKey = "fake-crash-test-key-0123456789abcdef",
            ModelApiKeyBackup = "fake-crash-test-key-0123456789abcdef",
            CustomParameters = "--fake-exit-code 37"
        };
        var runtime = new RuntimeRecord(
            "crashing-runtime",
            "Crashing fake runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            FakeRuntimeExecutable(),
            "{\"fixture\":true}",
            DateTimeOffset.UtcNow);
        var model = new ModelRecord(
            "crash-model",
            "Crash Model",
            modelPath,
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);

        using var supervisor = CreateTestLlamaSupervisor();
        await supervisor.StartAsync(runtime, model, settings, Path.Combine(root, "logs"));
        await WaitForFakeRuntimeAsync(() => Task.FromResult(!supervisor.IsRunning));

        Assert.Equal(LlamaRuntimeState.Failed, supervisor.State);
        Assert.Equal(37, supervisor.LastExitCode);
        Assert.True((await supervisor.StopVerifiedAsync(TestContext.Current.CancellationToken)).VerifiedStopped);
        Assert.False(supervisor.IsRunning);
    }

    private static string FakeRuntimeExecutable()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("LocalLlmConsole.sln"))
            ?? throw new InvalidOperationException("Repository root was not found.");
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Debug"
            : "Release";
        var executable = Path.Combine(
            repositoryRoot,
            "tests",
            "LocalLlmConsole.FakeRuntime",
            "bin",
            configuration,
            "net10.0-windows",
            "LocalLlmConsole.FakeRuntime.exe");
        Assert.True(File.Exists(executable), $"Fake runtime was not built: {executable}");
        return executable;
    }

    private static int FreeFakeRuntimePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForFakeRuntimeAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            if (await condition())
                return;
            await Task.Delay(50, timeout.Token);
        }
        throw new TimeoutException("The fake runtime did not become ready.");
    }

    private static async Task<bool> EndpointRespondsAsync(int port)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(300) };
            using var _ = await http.GetAsync($"http://127.0.0.1:{port}/health");
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
