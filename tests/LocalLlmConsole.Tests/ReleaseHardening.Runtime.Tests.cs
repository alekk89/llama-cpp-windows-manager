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
    public void LlamaProcessSupervisorAttachLoadAndStopTransitionsAreExplicit()
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

        supervisor.Stop();

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
    public void LlamaProcessSupervisorUsesWslRuntimeStopServiceForRecoveredWslSessions()
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

        supervisor.Stop();

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
    public void NativeRuntimeStopServiceStopsStartedProcess()
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
            var result = new NativeRuntimeStopService().Stop(process);

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


    [Fact]
    public void RuntimeAdapterRejectsNetworkHostWithoutLanMode()
    {
        var request = ValidLaunchRequest() with { Host = "0.0.0.0" };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("localhost", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterAllowsNetworkHostWithExplicitLanModeAndApiKey()
    {
        var apiKey = new string('a', 32);
        var request = ValidLaunchRequest() with
        {
            Host = "0.0.0.0",
            AllowNetworkAccess = true,
            ApiKey = apiKey
        };

        var result = RuntimeAdapter.Validate(request);
        var args = RuntimeAdapter.BuildArgs(request);

        Assert.True(result.Ok);
        Assert.Contains("0.0.0.0", args);
        Assert.DoesNotContain("--api-key", args);
        Assert.Equal(apiKey, request.ApiKey);
    }


    [Fact]
    public void RuntimeAdapterRequiresApiKeyForModelServing()
    {
        var request = ValidLaunchRequest() with
        {
            Host = "0.0.0.0",
            AllowNetworkAccess = true,
            RequireApiKeyAuth = false,
            ApiKey = ""
        };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("API key", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterRejectsWeakApiKey()
    {
        var request = ValidLaunchRequest() with { ApiKey = "test-key" };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("32", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterRejectsExtremeLaunchValues()
    {
        var request = ValidLaunchRequest() with
        {
            ContextSize = int.MaxValue,
            BatchSize = int.MaxValue,
            MicroBatchSize = int.MaxValue,
            Threads = int.MaxValue
        };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("Context", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Batch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Threads", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterBuildsLocalOnlyArgs()
    {
        var args = RuntimeAdapter.BuildArgs(ValidLaunchRequest());

        Assert.Contains("--host", args);
        Assert.Contains("127.0.0.1", args);
        Assert.Contains("--port", args);
        Assert.Contains("8081", args);
        Assert.DoesNotContain("--api-key", args);
        Assert.DoesNotContain("--cache-ram", args);
        Assert.DoesNotContain("--ctx-checkpoints", args);
        Assert.DoesNotContain("--checkpoint-min-step", args);
        Assert.DoesNotContain("--reasoning-effort", args);
        Assert.DoesNotContain("--reasoning-budget-message", args);
        Assert.DoesNotContain("--reasoning-preserve", args);
        Assert.DoesNotContain("--no-reasoning-preserve", args);
    }

    [Fact]
    public void RuntimeAdapterBuildsCurrentReasoningControls()
    {
        const string budgetMessage = "Conclude the reasoning and provide the final answer.";
        var args = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            ReasoningEffort = "high",
            ReasoningBudget = 16_384,
            ReasoningBudgetMessage = budgetMessage,
            ReasoningPreserve = "on"
        });

        Assert.Equal("high", args[args.ToList().IndexOf("--reasoning-effort") + 1]);
        Assert.Equal("16384", args[args.ToList().IndexOf("--reasoning-budget") + 1]);
        Assert.Equal(budgetMessage, args[args.ToList().IndexOf("--reasoning-budget-message") + 1]);
        Assert.Contains("--reasoning-preserve", args);

        var noPreserve = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with { ReasoningPreserve = "off" });
        Assert.Contains("--no-reasoning-preserve", noPreserve);
    }

    [Fact]
    public void ReasoningControlsRoundTripThroughSavedLaunchProfiles()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        var edited = defaults with
        {
            ReasoningEffort = "xhigh",
            ReasoningBudget = 16_384,
            ReasoningBudgetMessage = "Finish reasoning now.",
            ReasoningPreserve = "off"
        };

        var profile = ModelLaunchSettings.FromAppSettings(edited, "runtime-current");
        var restored = profile.ApplyTo(defaults);

        Assert.Equal("xhigh", profile.ReasoningEffort);
        Assert.Equal(16_384, restored.ReasoningBudget);
        Assert.Equal("Finish reasoning now.", restored.ReasoningBudgetMessage);
        Assert.Equal("off", restored.ReasoningPreserve);
    }

    [Fact]
    public void RuntimeAdapterRejectsInvalidReasoningControls()
    {
        var result = RuntimeAdapter.Validate(ValidLaunchRequest() with
        {
            ReasoningEffort = "ultra",
            ReasoningBudgetMessage = new string('x', 4097),
            ReasoningPreserve = "sometimes"
        });

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("effort", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("4096", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Preserve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimeAdapterBuildsSingleAndMultiGpuSelectionArgs()
    {
        var autoArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Cuda,
            GpuLayers = 999
        });
        Assert.DoesNotContain("--split-mode", autoArgs);
        Assert.DoesNotContain("--device", autoArgs);
        Assert.DoesNotContain("--tensor-split", autoArgs);

        var singleArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Cuda,
            GpuLayers = 999,
            GpuMode = "single",
            GpuDevices = "CUDA1"
        });
        Assert.Equal("none", singleArgs[singleArgs.ToList().IndexOf("--split-mode") + 1]);
        Assert.Equal("CUDA1", singleArgs[singleArgs.ToList().IndexOf("--device") + 1]);

        var layerArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Cuda,
            GpuLayers = 999,
            GpuMode = "layer",
            GpuDevices = " CUDA0, CUDA1, CUDA2 ",
            GpuSplit = " 2, 1, 1 "
        });
        Assert.Equal("layer", layerArgs[layerArgs.ToList().IndexOf("--split-mode") + 1]);
        Assert.Equal("CUDA0,CUDA1,CUDA2", layerArgs[layerArgs.ToList().IndexOf("--device") + 1]);
        Assert.Equal("2,1,1", layerArgs[layerArgs.ToList().IndexOf("--tensor-split") + 1]);

        var tensorArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Cuda,
            GpuLayers = 999,
            GpuMode = "tensor",
            GpuDevices = "CUDA0,CUDA1",
            GpuSplit = "1,1"
        });
        Assert.Equal("tensor", tensorArgs[tensorArgs.ToList().IndexOf("--split-mode") + 1]);
        Assert.Equal("1,1", tensorArgs[tensorArgs.ToList().IndexOf("--tensor-split") + 1]);
    }

    [Fact]
    public void RuntimeAdapterValidatesGpuSelectionSettings()
    {
        Assert.False(RuntimeAdapter.Validate(ValidLaunchRequest() with
        {
            GpuMode = "single",
            GpuDevices = "CUDA0,CUDA1"
        }).Ok);
        Assert.False(RuntimeAdapter.Validate(ValidLaunchRequest() with
        {
            GpuMode = "layer",
            GpuDevices = "CUDA0,CUDA1",
            GpuSplit = "1"
        }).Ok);
        Assert.False(RuntimeAdapter.Validate(ValidLaunchRequest() with
        {
            GpuMode = "tensor",
            GpuSplit = "0,0"
        }).Ok);
        Assert.False(RuntimeAdapter.Validate(ValidLaunchRequest() with
        {
            GpuMode = "unsupported"
        }).Ok);
    }

    [Fact]
    public void CustomLaunchParameterParserPreservesQuotedWindowsPathsAndEscapes()
    {
        var args = CustomLaunchParameterParser.Parse("""--n-cpu-moe 999 --device-draft CUDA1 --model-draft "D:\Models\draft model.gguf" --flag\ with\ spaces 'single quoted value'""");

        Assert.Equal([
            "--n-cpu-moe",
            "999",
            "--device-draft",
            "CUDA1",
            "--model-draft",
            @"D:\Models\draft model.gguf",
            "--flag with spaces",
            "single quoted value"
        ], args);
        Assert.Empty(CustomLaunchParameterParser.Parse(""));
        Assert.Contains("unterminated quote", Assert.Throws<InvalidOperationException>(() =>
            CustomLaunchParameterParser.Parse("\"oops")).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeAdapterAppendsCustomExtraArgs()
    {
        var args = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            ExtraArgs = CustomLaunchParameterParser.Parse("--n-cpu-moe 999 --device-draft CUDA1 --model-draft \"D:\\Models\\draft model.gguf\"")
        });

        Assert.Equal([
            "--n-cpu-moe",
            "999",
            "--device-draft",
            "CUDA1",
            "--model-draft",
            @"D:\Models\draft model.gguf"
        ], args.TakeLast(6).ToArray());
    }

    [Fact]
    public void RuntimeAdapterBuildsPromptCacheAndCheckpointArgs()
    {
        var onArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            PromptCacheMode = "on",
            PromptCacheRamMb = 16_384,
            ContextCheckpointsMode = "on",
            ContextCheckpointCount = 48,
            ContextCheckpointEveryNTokens = 512
        });
        var offArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            PromptCacheMode = "off",
            ContextCheckpointsMode = "off"
        });

        Assert.Contains("--cache-ram", onArgs);
        Assert.Contains("16384", onArgs);
        Assert.Contains("--ctx-checkpoints", onArgs);
        Assert.Contains("48", onArgs);
        Assert.Contains("--checkpoint-min-step", onArgs);
        Assert.Contains("512", onArgs);

        Assert.Contains("--cache-ram", offArgs);
        Assert.Contains("0", offArgs);
        Assert.Contains("--ctx-checkpoints", offArgs);
        Assert.DoesNotContain("--checkpoint-min-step", offArgs);
    }


    [Fact]
    public void RuntimeAdapterTreatsSyclAsGpuBackend()
    {
        var onArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Sycl,
            GpuLayers = 99,
            MmapMode = "on"
        });
        var offArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Sycl,
            GpuLayers = 88,
            MmapMode = "off"
        });

        Assert.Contains("--n-gpu-layers", onArgs);
        Assert.Contains("99", onArgs);
        Assert.Contains("--mmap", onArgs);
        Assert.Contains("--n-gpu-layers", offArgs);
        Assert.Contains("88", offArgs);
        Assert.Contains("--no-mmap", offArgs);
    }


    [Fact]
    public void RuntimeAdapterTreatsMetalAsGpuBackend()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeAdapter.cs"));

        Assert.Contains("request.Backend is RuntimeBackend.Cuda or RuntimeBackend.Vulkan or RuntimeBackend.Metal or RuntimeBackend.Sycl", source, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeAdapterValidatesVisionProjectorPairing()
    {
        var missing = RuntimeAdapter.Validate(ValidLaunchRequest() with { VisionMode = "on", VisionProjectorPath = "" });

        Assert.False(missing.Ok);
        Assert.Contains(missing.Errors, error => error.Contains("mmproj", StringComparison.OrdinalIgnoreCase));

        var args = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            VisionMode = "on",
            VisionProjectorPath = "mmproj.gguf",
            VisionImageMinTokens = 256,
            VisionImageMaxTokens = 1024
        });
        Assert.Contains("--mmproj", args);
        Assert.Contains("mmproj.gguf", args);
        Assert.Contains("--image-min-tokens", args);
        Assert.Contains("256", args);
        Assert.Contains("--image-max-tokens", args);
        Assert.Contains("1024", args);

        var embeddedArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            VisionMode = "on",
            VisionProjectorEmbedded = true,
            VisionImageMinTokens = 128
        });
        Assert.DoesNotContain("--mmproj", embeddedArgs);
        Assert.Contains("--image-min-tokens", embeddedArgs);

        var offArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with { VisionMode = "off" });
        Assert.Contains("--no-mmproj", offArgs);

        var invalid = RuntimeAdapter.Validate(ValidLaunchRequest() with { VisionImageMinTokens = 2048, VisionImageMaxTokens = 1024 });
        Assert.False(invalid.Ok);
        Assert.Contains(invalid.Errors, error => error.Contains("Image min tokens", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterBuildsSpeculativeSamplingAndRopeArgs()
    {
        var request = ValidLaunchRequest() with
        {
            SpeculativeType = "draft-mtp",
            SpecDraftModelPath = "draft.gguf",
            SpecDraftGpuLayers = 999,
            SpecDraftMinTokens = 1,
            SpecDraftMaxTokens = 4,
            SpecDraftPSplit = 0.2,
            SpecDraftPMin = 0.05,
            SpecDraftCacheTypeK = "q8_0",
            SpecDraftCacheTypeV = "q8_0",
            MaxTokens = 512,
            Seed = 1234,
            RepeatLastN = 128,
            RepeatPenalty = 1.08,
            PresencePenalty = 0.2,
            FrequencyPenalty = 0.1,
            RopeScaling = "yarn",
            RopeScale = 2,
            RopeFreqBase = 1_000_000,
            RopeFreqScale = 0.5
        };

        var args = RuntimeAdapter.BuildArgs(request);

        Assert.Contains("--spec-type", args);
        Assert.Contains("draft-mtp", args);
        Assert.Contains("--model-draft", args);
        Assert.Contains("draft.gguf", args);
        Assert.Contains("--n-gpu-layers-draft", args);
        Assert.Contains("999", args);
        Assert.Contains("--spec-draft-n-min", args);
        Assert.Contains("--spec-draft-n-max", args);
        Assert.Contains("--cache-type-k-draft", args);
        Assert.Contains("--cache-type-v-draft", args);
        Assert.Contains("--predict", args);
        Assert.Contains("512", args);
        Assert.Contains("--seed", args);
        Assert.Contains("1234", args);
        Assert.Contains("--repeat-last-n", args);
        Assert.Contains("--repeat-penalty", args);
        Assert.Contains("1.08", args);
        Assert.Contains("--presence-penalty", args);
        Assert.Contains("--frequency-penalty", args);
        Assert.Contains("--rope-scaling", args);
        Assert.Contains("yarn", args);
        Assert.Contains("--rope-scale", args);
        Assert.Contains("--rope-freq-base", args);
        Assert.Contains("--rope-freq-scale", args);

        var embeddedMtpArgs = RuntimeAdapter.BuildArgs(request with { SpecDraftModelPath = "" });
        Assert.Contains("draft-mtp", embeddedMtpArgs);
        Assert.DoesNotContain("--model-draft", embeddedMtpArgs);

        var dflashArgs = RuntimeAdapter.BuildArgs(request with
        {
            SpeculativeType = "draft-dflash",
            SpecDraftModelPath = "qwen-dflash-head.gguf",
            SpecDraftMaxTokens = 15
        });
        Assert.Contains("draft-dflash", dflashArgs);
        Assert.Contains("qwen-dflash-head.gguf", dflashArgs);
        Assert.Contains("--spec-draft-n-max", dflashArgs);
        Assert.True(RuntimeAdapter.Validate(request with { SpeculativeType = "draft-dflash" }).Ok);
        Assert.Contains("draft-dflash", LaunchSettingMetadataService.SpeculativeTypeOptions);

        var dsparkArgs = RuntimeAdapter.BuildArgs(request with
        {
            SpeculativeType = "draft-dspark",
            SpecDraftModelPath = "qwen-dspark-head.gguf",
            SpecDraftMaxTokens = 7
        });
        Assert.Contains("draft-dspark", dsparkArgs);
        Assert.Contains("qwen-dspark-head.gguf", dsparkArgs);
        Assert.Contains("--spec-draft-n-max", dsparkArgs);
        Assert.Contains("7", dsparkArgs);
        Assert.True(RuntimeAdapter.Validate(request with { SpeculativeType = "draft-dspark" }).Ok);
        Assert.Contains("draft-dspark", LaunchSettingMetadataService.SpeculativeTypeOptions);

        var mtpArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            SpeculativeType = "atomic-mtp",
            MtpHeadPath = "mtp-head.gguf"
        });
        Assert.Contains("--spec-type", mtpArgs);
        Assert.Contains("mtp", mtpArgs);
        Assert.DoesNotContain("atomic-mtp", mtpArgs);
        Assert.Contains("--mtp-head", mtpArgs);
        Assert.Contains("mtp-head.gguf", mtpArgs);
        Assert.DoesNotContain("--model-draft", mtpArgs);

        var legacyMtpArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            SpeculativeType = "mtp",
            MtpHeadPath = "legacy-mtp-head.gguf"
        });
        Assert.Contains("--spec-type", legacyMtpArgs);
        Assert.Contains("mtp", legacyMtpArgs);
        Assert.Contains("legacy-mtp-head.gguf", legacyMtpArgs);

        var missingMtp = RuntimeAdapter.Validate(ValidLaunchRequest() with { SpeculativeType = "atomic-mtp" });
        Assert.False(missingMtp.Ok);
        Assert.Contains(missingMtp.Errors, error => error.Contains("MTP head", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task RuntimeRegistryScanRegistersRuntimeOnceWhenRootContainsExecutable()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        Directory.CreateDirectory(runtimeRoot);
        await File.WriteAllTextAsync(Path.Combine(runtimeRoot, "llama-server.exe"), "fake exe", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var registry = new RuntimeRegistryService(store);

        var count = await registry.ScanAsync(runtimeRoot);
        var runtimes = await store.ListRuntimesAsync();

        Assert.Equal(1, count);
        var runtime = Assert.Single(runtimes);
        Assert.Equal(RuntimeMode.Native, runtime.Mode);
        Assert.Equal(RuntimeBackend.Cpu, runtime.Backend);
        Assert.Equal(Path.Combine(runtimeRoot, "llama-server.exe"), runtime.ExecutablePath);
    }


    [Fact]
    public async Task RuntimeRegistryScanRepairsExecutableMovedInsideItsRecordedFolderWithoutDuplicatingRegistration()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var runtimeFolder = Path.Combine(runtimeRoot, "official-cpu");
        var binFolder = Path.Combine(runtimeFolder, "bin");
        Directory.CreateDirectory(binFolder);
        var movedExecutable = Path.Combine(binFolder, "llama-server.exe");
        await File.WriteAllTextAsync(movedExecutable, "fake exe", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var stale = new RuntimeRecord(
            "custom-runtime-id",
            "My official CPU runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(runtimeFolder, "llama-server.exe"),
            System.Text.Json.JsonSerializer.Serialize(new { folder = runtimeFolder }),
            DateTimeOffset.UtcNow.AddDays(-1));
        await store.UpsertRuntimeAsync(stale);

        var count = await new RuntimeRegistryService(store).ScanAsync(runtimeRoot);
        var repaired = Assert.Single(await store.ListRuntimesAsync());

        Assert.Equal(1, count);
        Assert.Equal(stale.Id, repaired.Id);
        Assert.Equal(stale.Name, repaired.Name);
        Assert.Equal(movedExecutable, repaired.ExecutablePath);
        Assert.True(RuntimeAvailabilityService.IsAvailable(repaired));
        Assert.True(repaired.UpdatedAt > stale.UpdatedAt);
    }


    [Fact]
    public async Task RuntimeRegistryInfersCudaFromNearbyRuntimeFiles()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var buildRoot = Path.Combine(runtimeRoot, "cuda-build");
        var binRoot = Path.Combine(buildRoot, "bin");
        var libRoot = Path.Combine(buildRoot, "lib");
        Directory.CreateDirectory(binRoot);
        Directory.CreateDirectory(libRoot);
        await File.WriteAllTextAsync(Path.Combine(binRoot, "llama-server"), "fake wsl binary", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(libRoot, "libcudart.so"), "fake cuda lib", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var registry = new RuntimeRegistryService(store);

        var count = await registry.ScanAsync(runtimeRoot);
        var runtime = Assert.Single(await store.ListRuntimesAsync());

        Assert.Equal(1, count);
        Assert.Equal(RuntimeMode.Wsl, runtime.Mode);
        Assert.Equal(RuntimeBackend.Cuda, runtime.Backend);
        Assert.Equal(Path.Combine(binRoot, "llama-server"), runtime.ExecutablePath);
    }


    [Fact]
    public async Task RuntimeRegistryDoesNotInferGpuBackendFromLooseFolderText()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var buildRoot = Path.Combine(runtimeRoot, "cuda-backup-notes");
        Directory.CreateDirectory(buildRoot);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "llama-server.exe"), "fake native binary", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var registry = new RuntimeRegistryService(store);

        var count = await registry.ScanAsync(runtimeRoot);
        var runtime = Assert.Single(await store.ListRuntimesAsync());

        Assert.Equal(1, count);
        Assert.Equal(RuntimeBackend.Cpu, runtime.Backend);
    }


    [Fact]
    public async Task RuntimeRegistryHonorsExplicitPackagedBackendMetadata()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var buildRoot = Path.Combine(runtimeRoot, "plain-runtime");
        Directory.CreateDirectory(buildRoot);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "llama-server.exe"), "fake native binary", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "local-llm-runtime.json"), """{"backend":"sycl"}""", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var registry = new RuntimeRegistryService(store);

        var count = await registry.ScanAsync(runtimeRoot);
        var runtime = Assert.Single(await store.ListRuntimesAsync());

        Assert.Equal(1, count);
        Assert.Equal(RuntimeBackend.Sycl, runtime.Backend);
    }


    [Fact]
    public void RuntimeAdapterRejectsInvalidSpeculativeSettings()
    {
        var request = ValidLaunchRequest() with
        {
            SpeculativeType = "maybe-mtp",
            SpecDraftMinTokens = 8,
            SpecDraftMaxTokens = 4,
            SpecDraftPSplit = 2,
            PromptCacheMode = "maybe",
            ContextCheckpointsMode = "maybe",
            RopeScaling = "banana"
        };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("Speculative type", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Draft min tokens", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Draft split", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Prompt cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("checkpoints", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("RoPE", StringComparison.OrdinalIgnoreCase));
    }


    private static WslEnvironmentReport ReadyWslReport(string distroName = "Ubuntu-24.04", string version = "2") => new(
        WslExeFound: true,
        WslWorking: true,
        Status: "ready",
        Details: "",
        DefaultDistro: distroName,
        RecommendedDistro: distroName,
        RecommendedAction: "",
        Distros: [new WslDistroInfo(distroName, "Running", version, IsDefault: true, IsUbuntu: true)]);

    private static WindowsToolSnapshot WindowsBuildTools(
        bool cpuReady = true,
        bool cudaReady = true,
        bool vulkanReady = true,
        bool syclReady = true) => new(
            GitInstalled: cpuReady,
            GitPath: cpuReady ? "git.exe" : "",
            CMakeInstalled: cpuReady,
            CMakePath: cpuReady ? "cmake.exe" : "",
            MsvcInstalled: cpuReady,
            MsvcDetails: cpuReady ? "MSVC ready" : "MSVC missing",
            NvidiaDriverVisible: false,
            NvidiaSmiPath: "",
            CudaToolsInstalled: cudaReady,
            CudaDetails: cudaReady ? "CUDA ready" : "nvcc.exe missing",
            VulkanToolsInstalled: vulkanReady,
            VulkanDetails: vulkanReady ? "Vulkan ready" : "VULKAN_SDK missing",
            SyclToolsInstalled: syclReady,
            SyclDetails: syclReady ? "oneAPI ready" : "oneAPI missing");

    private sealed class FakeModelGatewayRuntimeController : IModelGatewayRuntimeController
    {
        public Task<IReadOnlyList<ModelGatewayModelRoute>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelGatewayModelRoute>>([]);

        public Task<IReadOnlyList<LoadedModelSessionSnapshot>> RunningSessionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LoadedModelSessionSnapshot>>([]);

        public Task<LoadedModelSessionSnapshot> EnsureModelLoadedAsync(
            ModelGatewayModelRoute route,
            ModelGatewaySwapPolicy policy,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeModelGatewayHost : IModelGatewayHost
    {
        private readonly Exception? _startFailure;

        public FakeModelGatewayHost(Exception? startFailure = null)
        {
            _startFailure = startFailure;
        }

        public bool Started { get; private set; }

        public bool Disposed { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_startFailure is not null)
                throw _startFailure;

            Started = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualUiTimerFactory : IUiTimerFactory
    {
        public List<ManualUiTimer> Timers { get; } = [];

        public IUiTimer Create(TimeSpan interval)
        {
            var timer = new ManualUiTimer(interval);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualUiTimer : IUiTimer
    {
        public ManualUiTimer(TimeSpan interval)
        {
            Interval = interval;
        }

        public TimeSpan Interval { get; }

        public bool Started { get; private set; }

        public event EventHandler? Tick;

        public void Start()
            => Started = true;

        public void Stop()
            => Started = false;

        public void Fire()
            => Tick?.Invoke(this, EventArgs.Empty);

        public async Task FireAsync()
        {
            Fire();
            await Task.Yield();
        }
    }

    private static LoadedModelSessionManager CreateLoadedModelSessionManager()
        => new(CreateTestLlamaSupervisor);

    private static LlamaProcessSupervisor CreateTestLlamaSupervisor()
        => new(
            new WslRuntimeStopService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""))),
            new NativeRuntimeStopService());

    private sealed class ScriptedProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessStartInfo, ProcessRunResult> _handler;

        public ScriptedProcessRunner(Func<ProcessStartInfo, ProcessRunResult> handler) => _handler = handler;

        public List<IReadOnlyList<string>> Commands { get; } = [];
        public List<string> StandardInputs { get; } = [];

        public Task<ProcessRunResult> RunAsync(ProcessStartInfo psi, TimeSpan timeout, CancellationToken cancellationToken = default, string? standardInput = null)
        {
            Commands.Add(psi.ArgumentList.ToArray());
            StandardInputs.Add(standardInput ?? "");
            return Task.FromResult(_handler(psi));
        }
    }

}
