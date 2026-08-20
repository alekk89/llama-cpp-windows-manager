using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
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

        Assert.Equal("GPU 0: Intel(R) Arc(TM) A770 Graphics | 42% | 4.0/16.0 GiB", first);
        Assert.Equal(first, cached);
        Assert.Equal(["powershell.exe"], files);

        files.Clear();
        var amdService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(new ScriptedProcessRunner(psi =>
            {
                files.Add(psi.FileName ?? "");
                return DecodedPowerShellScript(psi).Contains("Win32_Processor", StringComparison.Ordinal)
                    ? new ProcessRunResult(0, "{\"Name\":\"AMD Ryzen 9 7950X\",\"Utilization\":18.5,\"PhysicalCores\":16,\"LogicalProcessors\":32}", "")
                    : new ProcessRunResult(0, "[{\"Index\":0,\"Name\":\"AMD Radeon RX 7900 XTX\",\"Utilization\":53.4,\"MemoryUsedBytes\":8589934592,\"MemoryTotalBytes\":25769803776}]", "");
            }), () => "", () => "nvidia-smi.exe", () => "powershell.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");

        var amd = await amdService.SummaryAsync(Session(RuntimeMode.Native, RuntimeBackend.Vulkan, AppSettings.CreateDefault(root), now), now, TestContext.Current.CancellationToken);

        Assert.Equal("GPU 0: AMD Radeon RX 7900 XTX | 53.4% | 8.0/24.0 GiB", amd);
        Assert.Equal(["powershell.exe"], files);

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
        Assert.Equal(["powershell.exe"], files);

        files.Clear();
        var cudaCpu = await cpuService.SummaryAsync(
            Session(RuntimeMode.Native, RuntimeBackend.Cuda, AppSettings.CreateDefault(root) with { GpuLayers = 0 }, now),
            now,
            TestContext.Current.CancellationToken);

        Assert.Equal("Telemetry: 58.4 °C thermal", cudaCpu);
        Assert.Equal(["powershell.exe"], files);

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

        Assert.Equal("Intel(R) Arc(TM) A770 Graphics", wsl);
        Assert.Equal(["powershell.exe", "wsl.exe"], files);
        Assert.Equal(["-d", "Ubuntu-24.04", "--", "bash", "-lc"], wslRunner.Commands.Last().Take(5).ToArray());

        var nvidiaRunner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "0, NVIDIA RTX, 76, 62, 12288, 24576", ""));
        var nvidiaService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(nvidiaRunner, () => "", () => "nvidia-smi.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");

        var nvidia = await nvidiaService.SummaryAsync(Session(RuntimeMode.Native, RuntimeBackend.Cuda, AppSettings.CreateDefault(root), now), now, TestContext.Current.CancellationToken);

        Assert.Equal("GPU 0: NVIDIA RTX | 76% | 62C | 12.0/24.0 GiB", nvidia);

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
                "GPU-a, 0, NVIDIA RTX 3090, 76, 62, 12288, 24576\n"
                + "GPU-b, 1, NVIDIA RTX 3090, 74, 60, 12000, 24576\n"
                + "GPU-c, 2, NVIDIA RTX 4060, 10, 40, 1000, 8192",
                "");
        });
        var processService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(processRunner, () => "", () => "nvidia-smi.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");
        var processSession = Session(RuntimeMode.Native, RuntimeBackend.Cuda, AppSettings.CreateDefault(root), now) with { ProcessId = 4242 };

        var processHardware = await processService.SummaryAsync(processSession, now, TestContext.Current.CancellationToken);

        Assert.Equal(
            $"GPU 0: NVIDIA RTX 3090 | 76% | 62C | 12.0/24.0 GiB{Environment.NewLine}"
            + "GPU 1: NVIDIA RTX 3090 | 74% | 60C | 11.7/24.0 GiB",
            processHardware);
        Assert.DoesNotContain("CPU", processHardware, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GPU 2", processHardware, StringComparison.Ordinal);

        var selectedRunner = new ScriptedProcessRunner(_ => new ProcessRunResult(
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

        Assert.Equal("GPU 1: NVIDIA RTX 3090 | 74% | 60C | 11.7/24.0 GiB", selectedHardware);

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
