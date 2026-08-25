using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public async Task RuntimeGpuSummaryApplicationServicePrefersCompleteNvidiaTelemetryWhileIdle()
    {
        var root = CreateTempRoot();
        var now = DateTimeOffset.Parse("2026-08-24T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var files = new List<string>();
        var runner = new ScriptedProcessRunner(psi =>
        {
            files.Add(Path.GetFileName(psi.FileName) ?? "");
            if (string.Equals(Path.GetFileName(psi.FileName), "nvidia-smi.exe", StringComparison.OrdinalIgnoreCase))
            {
                var query = psi.ArgumentList.FirstOrDefault(argument =>
                    argument.StartsWith("--query-gpu=", StringComparison.Ordinal)) ?? "";
                if (!query.Contains("name,utilization.gpu", StringComparison.Ordinal))
                {
                    return new ProcessRunResult(
                        0,
                        "0, N/A, N/A, N/A, N/A, N/A, N/A\n"
                        + "1, N/A, N/A, N/A, N/A, N/A, N/A\n"
                        + "2, N/A, N/A, N/A, N/A, N/A, N/A",
                        "");
                }
                return new ProcessRunResult(
                    0,
                    "0, NVIDIA GeForce RTX 3090, 17, 37, 1416, 24576, 91.80, 750, N/A, N/A, N/A, N/A, N/A, N/A\n"
                    + "1, NVIDIA GeForce RTX 3090, 0, 22, 0, 24576, 11.00, 210, N/A, N/A, N/A, N/A, N/A, N/A\n"
                    + "2, NVIDIA GeForce RTX 3090, 0, 29, 0, 24576, 11.19, 210, N/A, N/A, N/A, N/A, N/A, N/A",
                    "");
            }

            var script = DecodedPowerShellScript(psi);
            if (script.Contains("CreateRunspacePool(1, 3)", StringComparison.Ordinal))
                return new ProcessRunResult(0, System.Text.Json.JsonSerializer.Serialize(new
                {
                    Cpu = "{\"Name\":\"AMD Ryzen 9 7950X\",\"Utilization\":18.5,\"PhysicalCores\":16,\"LogicalProcessors\":32}",
                    Memory = "{\"UsedBytes\":17179869184,\"TotalBytes\":34359738368}",
                    Gpu = "[{\"Index\":0,\"Name\":\"NVIDIA GeForce RTX 3090\",\"Utilization\":17},"
                          + "{\"Index\":1,\"Name\":\"NVIDIA GeForce RTX 3090\",\"Utilization\":0},"
                          + "{\"Index\":2,\"Name\":\"Intel(R) Arc(TM) A770 Graphics\",\"Utilization\":8},"
                          + "{\"Index\":3,\"Name\":\"NVIDIA GeForce RTX 3090\",\"Utilization\":0}]"
                }), "");
            if (script.Contains("Win32_Processor", StringComparison.Ordinal))
                return new ProcessRunResult(0, "{\"Name\":\"AMD Ryzen 9 7950X\",\"Utilization\":18.5,\"PhysicalCores\":16,\"LogicalProcessors\":32}", "");
            if (script.Contains("Win32_OperatingSystem", StringComparison.Ordinal))
                return new ProcessRunResult(0, "{\"UsedBytes\":17179869184,\"TotalBytes\":34359738368}", "");
            return new ProcessRunResult(
                0,
                "[{\"Index\":0,\"Name\":\"NVIDIA GeForce RTX 3090\",\"Utilization\":17},"
                + "{\"Index\":1,\"Name\":\"NVIDIA GeForce RTX 3090\",\"Utilization\":0},"
                + "{\"Index\":2,\"Name\":\"Intel(R) Arc(TM) A770 Graphics\",\"Utilization\":8},"
                + "{\"Index\":3,\"Name\":\"NVIDIA GeForce RTX 3090\",\"Utilization\":0}]",
                "");
        });
        var service = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(runner, () => "", () => "nvidia-smi.exe", () => "powershell.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");

        var summary = await service.SummaryAsync(null, now, TestContext.Current.CancellationToken);

        Assert.Contains("GPU 0: NVIDIA GeForce RTX 3090 | 17% load | 37 °C | 1.4/24.0 GiB VRAM | 91.8 W | 750 MHz core", summary, StringComparison.Ordinal);
        Assert.Contains("GPU 1: NVIDIA GeForce RTX 3090 | 0% load | 22 °C | 0.0/24.0 GiB VRAM | 11 W | 210 MHz core", summary, StringComparison.Ordinal);
        Assert.Contains("GPU 2: NVIDIA GeForce RTX 3090 | 0% load | 29 °C | 0.0/24.0 GiB VRAM | 11.2 W | 210 MHz core", summary, StringComparison.Ordinal);
        Assert.Contains("GPU 3: Intel(R) Arc(TM) A770 Graphics | 8% load", summary, StringComparison.Ordinal);
        Assert.Equal(2, files.Count(file => string.Equals(file, "nvidia-smi.exe", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, files.Count(file => string.Equals(file, "powershell.exe", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task CombinedWindowsHardwareProbeFallsBackWhenRunspacesAreUnavailable()
    {
        var commands = new List<string>();
        var runner = new ScriptedProcessRunner(psi =>
        {
            var script = string.Equals(Path.GetFileName(psi.FileName), "powershell.exe", StringComparison.OrdinalIgnoreCase)
                ? DecodedPowerShellScript(psi)
                : "";
            commands.Add(script);
            if (script.Contains("CreateRunspacePool(1, 3)", StringComparison.Ordinal))
                return new ProcessRunResult(1, "", "runspaces disabled");
            if (script.Contains("Win32_Processor", StringComparison.Ordinal))
                return new ProcessRunResult(0, "{\"Name\":\"Fallback CPU\",\"Utilization\":25}", "");
            if (script.Contains("Win32_OperatingSystem", StringComparison.Ordinal))
                return new ProcessRunResult(0, "{\"UsedBytes\":8589934592,\"TotalBytes\":17179869184}", "");
            if (!string.IsNullOrWhiteSpace(script))
                return new ProcessRunResult(0, "[{\"Index\":0,\"Name\":\"Fallback GPU\",\"Utilization\":40}]", "");
            return new ProcessRunResult(1, "", "vendor tool unavailable");
        });
        var service = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(runner, () => "", () => "nvidia-smi.exe", () => "powershell.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");

        var summary = await service.SummaryAsync(null, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Contains("CPU: Fallback CPU", summary, StringComparison.Ordinal);
        Assert.Contains("RAM: 8.0/16.0 GiB", summary, StringComparison.Ordinal);
        Assert.Contains("GPU 0: Fallback GPU | 40% load", summary, StringComparison.Ordinal);
        Assert.Equal(4, commands.Count(command => !string.IsNullOrWhiteSpace(command)));
    }

    [Fact]
    public async Task NvidiaOptionalSensorsFallBackIndependentlyWhenCombinedQueryIsRejected()
    {
        var queries = new List<string>();
        var runner = new ScriptedProcessRunner(psi =>
        {
            var query = psi.ArgumentList.FirstOrDefault(argument => argument.StartsWith("--query-gpu=", StringComparison.Ordinal)) ?? "";
            queries.Add(query);
            if (query.Contains("index,name,utilization.gpu", StringComparison.Ordinal))
                return query.Contains("clocks.mem", StringComparison.Ordinal)
                    ? new ProcessRunResult(0, "0, NVIDIA RTX, 50, 60, 1024, 24576, 125, 1800, 9751, 350, 0x0000000000000000, 78", "")
                    : new ProcessRunResult(0, "0, NVIDIA RTX, 50, 60, 1024, 24576, 125, 1800", "");
            if (query.Count(character => character == ',') > 1)
                return new ProcessRunResult(1, "", "unsupported field");
            if (query.EndsWith("clocks.mem", StringComparison.Ordinal))
                return new ProcessRunResult(0, "0, 9751", "");
            if (query.EndsWith("power.limit", StringComparison.Ordinal))
                return new ProcessRunResult(0, "0, 350", "");
            if (query.EndsWith("clocks_throttle_reasons.active", StringComparison.Ordinal))
                return new ProcessRunResult(0, "0, 0x0000000000000000", "");
            if (query.EndsWith("temperature.memory", StringComparison.Ordinal))
                return new ProcessRunResult(0, "0, 78", "");
            return new ProcessRunResult(1, "", "unsupported field");
        });
        var probe = new GpuStatusProbeService(runner, () => "", () => "nvidia-smi.exe");

        var summary = await probe.SummaryAsync(TestContext.Current.CancellationToken);

        Assert.Contains("9751 MHz memory", summary, StringComparison.Ordinal);
        Assert.Contains("350 W limit", summary, StringComparison.Ordinal);
        Assert.Contains("throttle none", summary, StringComparison.Ordinal);
        Assert.Contains("78 °C memory", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("% memory", summary, StringComparison.Ordinal);

        var firstQueryCount = queries.Count;
        var repeated = await probe.SummaryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(summary, repeated);
        Assert.Equal(firstQueryCount + 1, queries.Count);
    }

    [Fact]
    public async Task PowerSummarySkipsCpuAndRamProbesAndUsesTenSecondCache()
    {
        var now = DateTimeOffset.Parse("2026-08-24T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var commands = new List<string>();
        var runner = new ScriptedProcessRunner(psi =>
        {
            var command = string.Join(' ', psi.ArgumentList);
            commands.Add(command);
            if (string.Equals(Path.GetFileName(psi.FileName), "nvidia-smi.exe", StringComparison.OrdinalIgnoreCase))
                return new ProcessRunResult(0, "0, NVIDIA RTX 3090, 125.5", "");
            return new ProcessRunResult(0, "[{\"Index\":0,\"Name\":\"NVIDIA RTX 3090\"}]", "");
        });
        var service = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(
                runner,
                () => "",
                () => "nvidia-smi.exe",
                () => "powershell.exe",
                () => "",
                () => ""),
            new GpuSummaryCache(),
            () => "wsl.exe");

        var first = await service.PowerSummaryAsync(now, TestContext.Current.CancellationToken);
        var cached = await service.PowerSummaryAsync(now.AddSeconds(9), TestContext.Current.CancellationToken);

        Assert.Equal(first, cached);
        Assert.Equal("GPU 0: NVIDIA RTX 3090 | 125.5 W", first);
        Assert.Equal(2, commands.Count);
        Assert.DoesNotContain(commands, command => command.Contains("Win32_Processor", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, command => command.Contains("Win32_OperatingSystem", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GpuProbeCachesExecutableDiscoveryUntilExplicitInvalidation()
    {
        var resolutions = 0;
        var now = DateTimeOffset.Parse("2026-08-25T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var executableAvailable = false;
        var probe = new GpuStatusProbeService(
            new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", "")),
            findAmdSmi: () =>
            {
                resolutions++;
                return executableAvailable ? "amd-smi.exe" : "";
            },
            resolverUtcNow: () => now);

        await probe.AmdSmiSummaryAsync(TestContext.Current.CancellationToken);
        await probe.AmdSmiSummaryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, resolutions);

        executableAvailable = true;
        now = now.AddMinutes(4);
        await probe.AmdSmiSummaryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, resolutions);

        now = now.AddMinutes(1);
        await probe.AmdSmiSummaryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, resolutions);

        probe.InvalidateExecutableCache();
        await probe.AmdSmiSummaryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, resolutions);
    }

    [Fact]
    public async Task RuntimeGpuSummaryApplicationServiceChoosesProbeAndCachesByActiveSession()
    {
        var root = CreateTempRoot();
        var now = DateTimeOffset.Parse("2026-05-28T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var files = new List<string>();
        var runner = new ScriptedProcessRunner(psi =>
        {
            files.Add(psi.FileName ?? "");
            if (string.Equals(Path.GetFileName(psi.FileName), "powershell.exe", StringComparison.OrdinalIgnoreCase))
                return DecodedPowerShellScript(psi).Contains("Win32_Processor", StringComparison.Ordinal)
                    ? new ProcessRunResult(0, "{\"Name\":\"AMD Ryzen 9 7950X 16-Core Processor\",\"Utilization\":18.5,\"PhysicalCores\":16,\"LogicalProcessors\":32}", "")
                    : new ProcessRunResult(0, "[{\"Index\":0,\"Name\":\"Intel(R) Arc(TM) A770 Graphics\",\"Utilization\":42,\"MemoryUsedBytes\":4294967296,\"MemoryTotalBytes\":17179869184}]", "");
            return new ProcessRunResult(0, "[level_zero:gpu][level_zero:0] Intel(R) Arc(TM) A770 Graphics", "");
        });
        var service = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(runner, () => "sycl-ls.exe", () => "nvidia-smi.exe", () => "powershell.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");
        var nativeSycl = Session(RuntimeMode.Native, RuntimeBackend.Sycl, AppSettings.CreateDefault(root), now);

        var first = await service.SummaryAsync(nativeSycl, now, TestContext.Current.CancellationToken);
        var cached = await service.SummaryAsync(nativeSycl, now.AddSeconds(1), TestContext.Current.CancellationToken);

        Assert.Contains("CPU: AMD Ryzen 9 7950X", first, StringComparison.Ordinal);
        Assert.Contains("GPU 0: Intel(R) Arc(TM) A770 Graphics | 42% load | 4.0/16.0 GiB VRAM", first, StringComparison.Ordinal);
        Assert.Equal(first, cached);
        Assert.Equal(3, files.Count(file => string.Equals(file, "powershell.exe", StringComparison.OrdinalIgnoreCase)));

        files.Clear();
        var amdService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(new ScriptedProcessRunner(psi =>
            {
                files.Add(psi.FileName ?? "");
                if (string.Equals(Path.GetFileName(psi.FileName), "nvidia-smi.exe", StringComparison.OrdinalIgnoreCase))
                    return new ProcessRunResult(1, "", "Unavailable");
                return DecodedPowerShellScript(psi).Contains("Win32_Processor", StringComparison.Ordinal)
                    ? new ProcessRunResult(0, "{\"Name\":\"AMD Ryzen 9 7950X\",\"Utilization\":18.5,\"PhysicalCores\":16,\"LogicalProcessors\":32}", "")
                    : new ProcessRunResult(0, "[{\"Index\":0,\"Name\":\"AMD Radeon RX 7900 XTX\",\"Utilization\":53.4,\"MemoryUsedBytes\":8589934592,\"MemoryTotalBytes\":25769803776}]", "");
            }), () => "", () => "nvidia-smi.exe", () => "powershell.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");

        var amd = await amdService.SummaryAsync(Session(RuntimeMode.Native, RuntimeBackend.Vulkan, AppSettings.CreateDefault(root), now), now, TestContext.Current.CancellationToken);

        Assert.Contains("CPU: AMD Ryzen 9 7950X", amd, StringComparison.Ordinal);
        Assert.Contains("GPU 0: AMD Radeon RX 7900 XTX | 53.4% load | 8.0/24.0 GiB VRAM", amd, StringComparison.Ordinal);
        Assert.Equal(3, files.Count(file => string.Equals(file, "powershell.exe", StringComparison.OrdinalIgnoreCase)));

        files.Clear();
        var cpuService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(new ScriptedProcessRunner(psi =>
            {
                files.Add(psi.FileName ?? "");
                return new ProcessRunResult(0, "{\"TemperatureCelsius\":58.4}", "");
            }), () => "", () => "nvidia-smi.exe", () => "powershell.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");

        var cpu = await cpuService.SummaryAsync(Session(RuntimeMode.Native, RuntimeBackend.Cpu, AppSettings.CreateDefault(root), now), now, TestContext.Current.CancellationToken);

        Assert.Equal("Telemetry: 58.4 °C thermal", cpu);
        Assert.Equal(5, files.Count);

        files.Clear();
        var cudaCpu = await cpuService.SummaryAsync(
            Session(RuntimeMode.Native, RuntimeBackend.Cuda, AppSettings.CreateDefault(root) with { GpuLayers = 0 }, now),
            now,
            TestContext.Current.CancellationToken);

        Assert.Equal("Telemetry: 58.4 °C thermal", cudaCpu);
        Assert.Equal(4, files.Count);

        files.Clear();
        var wslRunner = new ScriptedProcessRunner(psi =>
        {
            files.Add(psi.FileName ?? "");
            if (string.Equals(Path.GetFileName(psi.FileName), "powershell.exe", StringComparison.OrdinalIgnoreCase))
                return DecodedPowerShellScript(psi).Contains("Win32_Processor", StringComparison.Ordinal)
                    ? new ProcessRunResult(0, "{\"Name\":\"Intel Core Ultra 9\",\"Utilization\":11,\"PhysicalCores\":16,\"LogicalProcessors\":22}", "")
                    : new ProcessRunResult(0, "[]", "");
            return new ProcessRunResult(0, "[level_zero:gpu][level_zero:0] Intel(R) Arc(TM) A770 Graphics", "");
        });
        var wslService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(wslRunner, () => "sycl-ls.exe", () => "nvidia-smi.exe", () => "powershell.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");
        var wslSycl = Session(RuntimeMode.Wsl, RuntimeBackend.Sycl, AppSettings.CreateDefault(root) with { WslDistro = "Ubuntu-24.04" }, now);

        var wsl = await wslService.SummaryAsync(wslSycl, now, TestContext.Current.CancellationToken);

        Assert.Contains("CPU: Intel Core Ultra 9", wsl, StringComparison.Ordinal);
        Assert.Contains("GPU 0: Intel(R) Arc(TM) A770 Graphics", wsl, StringComparison.Ordinal);
        Assert.Equal(3, files.Count);
        Assert.Equal(["-d", "Ubuntu-24.04", "--", "bash", "-lc"], wslRunner.Commands.Last().Take(5).ToArray());

        var nvidiaRunner = new ScriptedProcessRunner(psi => psi.ArgumentList.Any(arg => arg.Contains("clocks.mem", StringComparison.Ordinal))
            ? new ProcessRunResult(0, "0, N/A, N/A, N/A, N/A, N/A", "")
            : new ProcessRunResult(0, "0, NVIDIA RTX, 76, 62, 12288, 24576", ""));
        var nvidiaService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(nvidiaRunner, () => "", () => "nvidia-smi.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");

        var nvidia = await nvidiaService.SummaryAsync(Session(RuntimeMode.Native, RuntimeBackend.Cuda, AppSettings.CreateDefault(root), now), now, TestContext.Current.CancellationToken);

        Assert.Equal("GPU 0: NVIDIA RTX | 76% load | 62 °C | 12.0/24.0 GiB VRAM", nvidia);

        var processRunner = new ScriptedProcessRunner(psi =>
        {
            var command = string.Join(' ', psi.ArgumentList);
            if (command.Contains("--query-compute-apps=", StringComparison.Ordinal))
            {
                return new ProcessRunResult(
                    0,
                    "GPU-a, 1111\nGPU-a, 4242\nGPU-b, 4242\nGPU-c, 3333",
                    "");
            }

            return new ProcessRunResult(
                0,
                "GPU-a, 0, NVIDIA RTX 3090, 76, 62, 12288, 24576, 205, 1695\n"
                + "GPU-b, 1, NVIDIA RTX 3090, 74, 60, 12000, 24576, 198, 1680\n"
                + "GPU-c, 2, NVIDIA RTX 4060, 10, 40, 1000, 8192, 75, 2400",
                "");
        });
        var processService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(processRunner, () => "", () => "nvidia-smi.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");
        var processSession = Session(RuntimeMode.Native, RuntimeBackend.Cuda, AppSettings.CreateDefault(root), now) with { ProcessId = 4242 };

        var processHardware = await processService.SummaryAsync(processSession, now, TestContext.Current.CancellationToken);

        Assert.Equal(
            $"GPU 0: NVIDIA RTX 3090 | 76% load | 62 °C | 12.0/24.0 GiB VRAM | 205 W | 1695 MHz core{Environment.NewLine}"
            + "GPU 1: NVIDIA RTX 3090 | 74% load | 60 °C | 11.7/24.0 GiB VRAM | 198 W | 1680 MHz core",
            processHardware);
        Assert.DoesNotContain("CPU", processHardware, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GPU 2", processHardware, StringComparison.Ordinal);

        var selectedRunner = new ScriptedProcessRunner(psi => psi.ArgumentList.Any(arg => arg.Contains("clocks.mem", StringComparison.Ordinal))
            ? new ProcessRunResult(0, "0, N/A, N/A, N/A, N/A, N/A\n1, N/A, N/A, N/A, N/A, N/A", "")
            : new ProcessRunResult(
                0,
                "0, NVIDIA RTX 3090, 76, 62, 12288, 24576\n1, NVIDIA RTX 3090, 74, 60, 12000, 24576",
                ""));
        var selectedService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(selectedRunner, () => "", () => "nvidia-smi.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");
        var selectedSettings = AppSettings.CreateDefault(root) with { GpuMode = "single", GpuDevices = "CUDA1" };

        var selectedHardware = await selectedService.SummaryAsync(
            Session(RuntimeMode.Native, RuntimeBackend.Cuda, selectedSettings, now),
            now,
            TestContext.Current.CancellationToken);

        Assert.Equal("GPU 1: NVIDIA RTX 3090 | 74% load | 60 °C | 11.7/24.0 GiB VRAM", selectedHardware);

        static LoadedModelSessionSnapshot Session(RuntimeMode mode, RuntimeBackend backend, AppSettings settings, DateTimeOffset startedAt)
            => new(
                "session",
                "model",
                "Model",
                "runtime",
                "Runtime",
                mode,
                backend,
                settings,
                "",
                startedAt,
                "",
                0,
                LoadedModelSessionStatus.Running,
                IsRunning: true,
                IsSelected: true);
    }

    private static string DecodedPowerShellScript(ProcessStartInfo startInfo)
    {
        var encodedIndex = startInfo.ArgumentList.IndexOf("-EncodedCommand");
        return encodedIndex >= 0 && encodedIndex + 1 < startInfo.ArgumentList.Count
            ? System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(startInfo.ArgumentList[encodedIndex + 1]))
            : "";
    }


    [Fact]
    public void RuntimeLifetimeCounterTrackerTracksRuntimeKeysAndUsesSlotFallback()
    {
        var tracker = new RuntimeLifetimeCounterTracker();
        var firstKey = "model-a|runtime-a|8081";
        var secondKey = "model-b|runtime-b|8082";

        Assert.False(tracker.Observe(firstKey, "model-a", "Model A", generatedCounter: 10, promptCounter: 5, slotSnapshot: null).HasTokens);
        var firstDelta = tracker.Observe(firstKey, "model-a", "Model A", generatedCounter: 14, promptCounter: 9, slotSnapshot: null);

        Assert.Equal("model-a", firstDelta.ModelId);
        Assert.Equal(4, firstDelta.GeneratedTokens);
        Assert.Equal(4, firstDelta.PromptTokens);

        Assert.False(tracker.Observe(secondKey, "model-b", "Model B", generatedCounter: null, promptCounter: null, new RuntimeSlotSnapshot(20, 50, false, null, null, null)).HasTokens);
        var secondDelta = tracker.Observe(secondKey, "model-b", "Model B", generatedCounter: null, promptCounter: null, new RuntimeSlotSnapshot(26, 63, false, null, null, null));

        Assert.Equal("model-b", secondDelta.ModelId);
        Assert.Equal(13, secondDelta.GeneratedTokens);
        Assert.Equal(6, secondDelta.PromptTokens);

        tracker.RetainRuntimeKeys([secondKey]);
        Assert.Equal(1, tracker.Count);
        Assert.False(tracker.Observe(firstKey, "model-a", "Model A", generatedCounter: 100, promptCounter: 100, slotSnapshot: null).HasTokens);
    }

    [Fact]
    public void RuntimeLifetimeCounterTrackerAggregatesParallelSlotResetsWithoutDoubleCountingSourceChanges()
    {
        var tracker = new RuntimeLifetimeCounterTracker();
        const string key = "model-a|runtime-a|8081";
        var first = new RuntimeSlotSnapshot(
            120,
            1500,
            true,
            null,
            null,
            4096,
            SlotCounters:
            [
                new RuntimeSlotCounterSnapshot("0", "task-a", 100, 1000, true),
                new RuntimeSlotCounterSnapshot("1", "task-b", 20, 500, true)
            ]);
        var second = new RuntimeSlotSnapshot(
            55,
            570,
            true,
            null,
            null,
            4096,
            SlotCounters:
            [
                new RuntimeSlotCounterSnapshot("0", "task-c", 30, 10, true),
                new RuntimeSlotCounterSnapshot("1", "task-b", 25, 560, true)
            ]);

        Assert.False(tracker.Observe(key, "model-a", "Model A", null, null, first).HasTokens);
        var slotDelta = tracker.Observe(key, "model-a", "Model A", null, null, second);
        Assert.Equal(35, slotDelta.PromptTokens);
        Assert.Equal(70, slotDelta.GeneratedTokens);

        Assert.False(tracker.Observe(key, "model-a", "Model A", 2000, 500, second).HasTokens);
        var prometheusDelta = tracker.Observe(key, "model-a", "Model A", 2020, 508, second);
        Assert.Equal(8, prometheusDelta.PromptTokens);
        Assert.Equal(20, prometheusDelta.GeneratedTokens);
    }


    [Fact]
    public void RuntimeIdleUnloadTrackerTracksEachRuntimeKeyIndependently()
    {
        var tracker = new RuntimeIdleUnloadTracker();
        var now = DateTimeOffset.Parse("2026-05-27T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var firstKey = "model-a|runtime-a|8081";
        var secondKey = "model-b|runtime-b|8082";

        Assert.False(tracker.Observe(firstKey, new RuntimeSlotSnapshot(0, 0, false, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now));
        Assert.False(tracker.Observe(secondKey, new RuntimeSlotSnapshot(0, 0, false, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now));

        Assert.True(tracker.Observe(firstKey, new RuntimeSlotSnapshot(0, 0, false, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now.AddSeconds(61)));
        Assert.False(tracker.Observe(secondKey, new RuntimeSlotSnapshot(0, 0, true, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now.AddSeconds(61)));
        Assert.False(tracker.Observe(secondKey, new RuntimeSlotSnapshot(0, 0, false, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now.AddSeconds(90)));
        Assert.True(tracker.Observe(secondKey, new RuntimeSlotSnapshot(0, 0, false, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now.AddSeconds(122)));

        tracker.RetainRuntimeKeys([secondKey]);
        Assert.Equal(1, tracker.Count);
        tracker.Reset(secondKey);
        Assert.Equal(0, tracker.Count);
    }


    [Fact]
    public async Task RuntimeIdleUnloadPolicyServiceOwnsReentrancyAndUnloadSelection()
    {
        var service = new RuntimeIdleUnloadPolicyService();
        var root = CreateTempRoot();
        var now = DateTimeOffset.Parse("2026-05-27T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var first = PollResult(root, "model-a", "Model A", 8081, new RuntimeSlotSnapshot(0, 0, false, null, null, null));
        var second = PollResult(root, "model-b", "Model B", 8082, new RuntimeSlotSnapshot(0, 0, false, null, null, null));
        var unloaded = new List<string>();

        var firstPass = await service.ApplyAsync(
            [first, second],
            idleMinutes: 1,
            now: now,
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, firstPass);
        Assert.Equal(2, service.TrackedRuntimeCount);

        var secondPass = await service.ApplyAsync(
            [first, second],
            idleMinutes: 1,
            now: now.AddSeconds(61),
            async (idle, token) =>
            {
                unloaded.Add(idle.Session.ModelId);
                var nested = await service.ApplyAsync([idle], 1, now.AddSeconds(62), (_, _) => Task.CompletedTask, token);
                Assert.Equal(0, nested);
                Assert.True(service.IsApplying);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, secondPass);
        Assert.Equal(["model-a", "model-b"], unloaded);
        Assert.False(service.IsApplying);

        service.Reset(first.RuntimeKey);
        Assert.Equal(1, service.TrackedRuntimeCount);

        var resetPass = await service.ApplyAsync(
            [],
            idleMinutes: 1,
            now: now.AddMinutes(2),
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, resetPass);
        Assert.Equal(0, service.TrackedRuntimeCount);

        static RuntimeMetricPollResult PollResult(string root, string modelId, string modelName, int port, RuntimeSlotSnapshot slot)
        {
            var settings = AppSettings.CreateDefault(root) with { Port = port };
            var session = new LoadedModelSessionSnapshot(
                $"session-{modelId}",
                modelId,
                modelName,
                $"runtime-{port}",
                $"Runtime {port}",
                RuntimeMode.Native,
                RuntimeBackend.Cpu,
                settings,
                Path.Combine(root, $"{modelId}.log"),
                DateTimeOffset.UtcNow,
                "",
                0,
                LoadedModelSessionStatus.Running,
                IsRunning: true,
                IsSelected: false);

            return new RuntimeMetricPollResult(
                session,
                RuntimeMetricPollerService.RuntimeKey(session),
                [],
                slot,
                "");
        }
    }


}
