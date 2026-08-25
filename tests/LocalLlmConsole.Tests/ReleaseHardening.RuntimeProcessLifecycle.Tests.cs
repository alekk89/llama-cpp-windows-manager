using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void RuntimeSourceCleanupDefaultsOn()
    {
        var root = CreateTempRoot();

        var settings = AppSettings.CreateDefault(root);

        Assert.True(settings.DeleteRuntimeSourceAfterSuccessfulBuild);
        Assert.True(settings.AutoLoadGatewayEnabled);
        Assert.Equal("singleActive", settings.AutoLoadGatewayPolicy);
    }


    [Fact]
    public void LlamaProcessSupervisorUsesCentralLogRedaction()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "LlamaRuntimeOutputObserver.cs"));

        Assert.Contains("LogFileService.RedactSensitiveText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Regex.Replace", File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "LlamaProcessSupervisor.cs")), StringComparison.Ordinal);
    }


    [Fact]
    public async Task LlamaProcessSupervisorAttachLoadAndStopTransitionsAreExplicit()
    {
        using var supervisor = new LlamaProcessSupervisor(
            new WslRuntimeStopService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""))),
            new NativeRuntimeStopService());
        var root = CreateTempRoot();
        var runtime = new RuntimeRecord(
            "runtime-1",
            "Native CPU",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(root, "llama-server.exe"),
            "{}",
            DateTimeOffset.UtcNow);
        var settings = AppSettings.CreateDefault(root);

        supervisor.AttachExisting(runtime, "model-1", settings, Path.Combine(root, "runtime.log"), LlamaRuntimeState.Failed);

        Assert.True(supervisor.IsRunning);
        Assert.Equal("model-1", supervisor.ActiveModelId);
        Assert.Equal("runtime-1", supervisor.ActiveRuntimeId);
        Assert.Equal(LlamaRuntimeState.Loading, supervisor.State);
        Assert.True(supervisor.MarkLoadedIfRunning());
        Assert.Equal(LlamaRuntimeState.Loaded, supervisor.State);

        var stop = await supervisor.StopVerifiedAsync(TestContext.Current.CancellationToken);

        Assert.True(stop.VerifiedStopped, stop.Error);
        Assert.False(supervisor.IsRunning);
        Assert.Equal("", supervisor.ActiveModelId);
        Assert.Equal("", supervisor.ActiveRuntimeId);
        Assert.Equal(LlamaRuntimeState.Stopped, supervisor.State);
        Assert.Null(supervisor.LastExitCode);
    }


    [Fact]
    public async Task LlamaProcessSupervisorRejectsRegisteredRuntimeWithMissingExecutableBeforeLaunch()
    {
        using var supervisor = CreateTestLlamaSupervisor();
        var root = CreateTempRoot();
        var runtime = new RuntimeRecord(
            "missing-runtime",
            "Missing CPU runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(root, "missing", "llama-server.exe"),
            "{}",
            DateTimeOffset.UtcNow);
        var model = new ModelRecord(
            "model-1",
            "Model",
            Path.Combine(root, "model.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => supervisor.StartAsync(
            runtime,
            model,
            AppSettings.CreateDefault(root),
            Path.Combine(root, "logs")));

        Assert.Contains("registered llama-server executable is missing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Repair or reinstall", error.Message, StringComparison.Ordinal);
        Assert.False(supervisor.IsRunning);
        Assert.False(Directory.Exists(Path.Combine(root, "logs")));
    }


    [Fact]
    public async Task LlamaProcessSupervisorUsesWslRuntimeStopServiceForRecoveredWslSessions()
    {
        var supervisorSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "LlamaProcessSupervisor.cs"));
        var commands = new List<IReadOnlyList<string>>();
        var runner = new ScriptedProcessRunner(psi =>
        {
            commands.Add(psi.ArgumentList.ToArray());
            return new ProcessRunResult(0, "", "");
        });
        using var supervisor = new LlamaProcessSupervisor(
            new WslRuntimeStopService(runner, () => "wsl.exe"),
            new NativeRuntimeStopService());
        var root = CreateTempRoot();
        var runtime = new RuntimeRecord(
            "runtime-wsl",
            "WSL CUDA",
            RuntimeMode.Wsl,
            RuntimeBackend.Cuda,
            "/opt/llama/bin/llama-server",
            "{}",
            DateTimeOffset.UtcNow);
        var settings = AppSettings.CreateDefault(root) with
        {
            WslDistro = "Ubuntu-24.04",
            Port = 8087
        };

        supervisor.AttachExisting(
            runtime,
            "model-1",
            settings,
            Path.Combine(root, "runtime.log"),
            LlamaRuntimeState.Loaded,
            "marker'1");

        var stop = await supervisor.StopVerifiedAsync(TestContext.Current.CancellationToken);

        Assert.True(stop.VerifiedStopped, stop.Error);
        var command = Assert.Single(commands);
        Assert.Equal(["-d", "Ubuntu-24.04", "--", "bash", "-lc"], command.Take(5).ToArray());
        Assert.Contains("marker='marker'\"'\"'1'", command[5], StringComparison.Ordinal);
        Assert.DoesNotContain("new WslRuntimeStopService", supervisorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start(", File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "LlamaProcessSupervisor.Wsl.cs")), StringComparison.Ordinal);
    }


    [Fact]
    public void WslRuntimeStopServiceBuildsMarkerAndFallbackStopCommands()
    {
        var markerCommand = WslRuntimeStopService.BuildStopCommand("/opt/llama/bin/llama-server", 8081, "marker'1");
        var fallbackCommand = WslRuntimeStopService.BuildStopCommand("/opt/llama/bin/llama-server", 8081, "");
        var startInfo = WslRuntimeStopService.BuildStopStartInfo("wsl.exe", "Ubuntu-24.04", "echo stop");

        Assert.Contains("marker='marker'\"'\"'1'", markerCommand, StringComparison.Ordinal);
        Assert.Contains("/proc/[0-9]*/cmdline", markerCommand, StringComparison.Ordinal);
        Assert.Contains("remaining=0", markerCommand, StringComparison.Ordinal);
        Assert.Contains("exit \"$remaining\"", markerCommand, StringComparison.Ordinal);
        Assert.Contains("'/opt/llama/bin/llama-server'", fallbackCommand, StringComparison.Ordinal);
        Assert.Contains("\"--port\"*'8081'", fallbackCommand, StringComparison.Ordinal);
        Assert.Contains("remaining=0", fallbackCommand, StringComparison.Ordinal);
        Assert.Equal("wsl.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        Assert.Equal(["-d", "Ubuntu-24.04", "--", "bash", "-lc", "echo stop"], startInfo.ArgumentList.ToArray());
        Assert.Equal("", WslRuntimeStopService.BuildStopCommand("", 8081, ""));
    }

    [Fact]
    public async Task WslRuntimeStopServiceReportsUnverifiedCleanup()
    {
        var root = CreateTempRoot();
        var logPath = Path.Combine(root, "logs", "runtime.log");
        var runner = new ScriptedProcessRunner(_ => new ProcessRunResult(1, "", "still running"));
        var service = new WslRuntimeStopService(runner, () => "wsl.exe");

        var result = await service.StopAsync(new WslRuntimeStopRequest(
            AppSettings.CreateDefault(root) with { WslDistro = "Ubuntu-24.04", Port = 8081 },
            "/opt/llama/bin/llama-server",
            "marker",
            logPath,
            BoundedLogFile.MegabytesToBytes(1)),
            TestContext.Current.CancellationToken);
        var log = await File.ReadAllTextAsync(logPath, TestContext.Current.CancellationToken);

        Assert.True(result.StopRequested);
        Assert.False(result.VerifiedStopped);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("still running", log, StringComparison.Ordinal);
        Assert.Contains("could not verify shutdown", log, StringComparison.Ordinal);
    }


    [Fact]
    public void NativeRuntimeStopServiceVerifiesAndRetriesByProcessId()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "NativeRuntimeStopService.cs"));

        Assert.Contains("PrimaryExitWaitMilliseconds = 3000", source, StringComparison.Ordinal);
        Assert.Contains("VerificationExitWaitMilliseconds = 1000", source, StringComparison.Ordinal);
        Assert.Contains("Process.GetProcessById(processId)", source, StringComparison.Ordinal);
        Assert.Contains("TryGetStartTime(process)", source, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", source, StringComparison.Ordinal);
    }


    [Fact]
    public async Task NativeRuntimeStopServiceStopsStartedProcess()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Start-Sleep -Seconds 30");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start test process.");
        try
        {
            var result = await new NativeRuntimeStopService().StopAsync(
                process,
                TestContext.Current.CancellationToken);

            Assert.True(result.StopRequested);
            Assert.True(result.Exited);
            Assert.True(process.WaitForExit(1000) || process.HasExited);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        }
    }


    [Fact]
    public void LlamaRuntimeOutputObserverWritesRedactedLogsAndDetectsLoadedLines()
    {
        var root = CreateTempRoot();
        var logPath = Path.Combine(root, "logs", "runtime.log");

        using (var writer = new BoundedLogWriter(logPath, maxBytes: 0))
        {
            Assert.False(LlamaRuntimeOutputObserver.Observe("Authorization: Bearer secret-key", writer, "secret-key"));
            Assert.True(LlamaRuntimeOutputObserver.Observe("server is listening on 127.0.0.1", writer, "secret-key"));
        }

        var log = File.ReadAllText(logPath);
        Assert.Contains("Authorization: Bearer [redacted]", log, StringComparison.Ordinal);
        Assert.Contains("server is listening on 127.0.0.1", log, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoundedLogWriterFlushesOnTimerThresholdExplicitFlushAndDispose()
    {
        var root = CreateTempRoot();
        var logPath = Path.Combine(root, "logs", "buffered-runtime.log");
        var writer = new BoundedLogWriter(logPath, maxBytes: 0);
        try
        {
            writer.WriteLine("timer-line");
            var timerDeadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 3d);
            while (!ReadShared(logPath).Contains("timer-line", StringComparison.Ordinal)
                   && Stopwatch.GetTimestamp() < timerDeadline)
                await Task.Delay(25, TestContext.Current.CancellationToken);
            Assert.Contains("timer-line", ReadShared(logPath), StringComparison.Ordinal);

            writer.WriteLine(new string('x', 64 * 1024));
            Assert.True(new FileInfo(logPath).Length >= 64 * 1024);

            writer.WriteLine("explicit-line");
            writer.Flush();
            Assert.Contains("explicit-line", ReadShared(logPath), StringComparison.Ordinal);
        }
        finally
        {
            writer.Dispose();
        }

        Assert.Contains("explicit-line", File.ReadAllText(logPath), StringComparison.Ordinal);

        static string ReadShared(string path)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }


    [Fact]
    public void LlamaProcessSupervisorStopsRecoveredNativeProcessByProcessId()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var startInfo = new ProcessStartInfo(HostExecutableResolver.WindowsPowerShellExe())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Start-Sleep -Seconds 30");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start test process.");
        using var supervisor = CreateTestLlamaSupervisor();
        try
        {
            var root = CreateTempRoot();
            var runtime = new RuntimeRecord(
                "runtime-native",
                "Native CPU",
                RuntimeMode.Native,
                RuntimeBackend.Cpu,
                Path.Combine(root, "llama-server.exe"),
                "{}",
                DateTimeOffset.UtcNow);

            supervisor.AttachExisting(
                runtime,
                "model-1",
                AppSettings.CreateDefault(root),
                Path.Combine(root, "logs", "runtime.log"),
                LlamaRuntimeState.Loaded,
                processId: process.Id);

            Assert.True(supervisor.IsRunning);
            Assert.Equal(process.Id, supervisor.ProcessId);

            supervisor.Stop();

            Assert.True(process.WaitForExit(1000) || process.HasExited);
            Assert.Equal(LlamaRuntimeState.Stopped, supervisor.State);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        }
    }


}
