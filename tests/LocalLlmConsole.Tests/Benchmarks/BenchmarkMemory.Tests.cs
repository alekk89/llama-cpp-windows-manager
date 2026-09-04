using System.Text.Json;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class BenchmarkMemoryTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task SamplerCapturesTransientPeaksAndDisposesWithoutInferringMissingValues()
    {
        var sampled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var probe = new Probe(() =>
        {
            var count = Interlocked.Increment(ref reads);
            if (count >= 3) sampled.TrySetResult();
            return [new("amd", "AMD", 24576, count == 2 ? 23000 : 12000, count == 2 ? 700 : 0),
                new("intel", "Intel", 0, null, 2048)];
        });
        await using (var sampler = await BenchmarkGpuMemorySampler.StartAsync(() => probe,
                         TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(10)))
        {
            await sampled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var peaks = await sampler.FinishAsync();
            var amd = Assert.Single(peaks, peak => peak.DeviceId == "amd");
            Assert.Equal(23000, amd.PeakDedicatedUsedMiB);
            Assert.Equal(700, amd.PeakSharedUsedMiB);
            Assert.True(amd.SampleCount >= 3);
            var intel = Assert.Single(peaks, peak => peak.DeviceId == "intel");
            Assert.Null(intel.PeakDedicatedUsedMiB);
            Assert.Equal(2048, intel.PeakSharedUsedMiB);
        }
        Assert.True(probe.Disposed);
        var finishedReads = reads;
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal(finishedReads, reads);
    }

    [Fact]
    public async Task TelemetryFailureDoesNotFailTheBenchmarkAndCancellationStopsPolling()
    {
        using var cancellation = new CancellationTokenSource();
        var probe = new Probe(() => throw new InvalidOperationException("driver unavailable"));
        await using (var sampler = await BenchmarkGpuMemorySampler.StartAsync(() => probe, cancellation.Token, TimeSpan.FromMilliseconds(10)))
        {
            await cancellation.CancelAsync();
            Assert.Empty(await sampler.FinishAsync());
        }
        Assert.True(probe.Disposed);
        await using var unavailable = await BenchmarkGpuMemorySampler.StartAsync(
            () => throw new InvalidOperationException("counters unavailable"), TestContext.Current.CancellationToken);
        Assert.Empty(await unavailable.FinishAsync());
    }

    [Fact]
    public void WindowsCountersMatchAdapterIdentityAndKeepMissingSharedReadingsUnknown()
    {
        const string a = "luid_0x00000000_0x00000001";
        const string b = "luid_0x00000000_0x00000002";
        var readings = WindowsGpuMemoryProbe.Combine(
            [new(b, "Intel", 0, null, null), new(a, "AMD", 24576, null, null)],
            new Dictionary<string, long> { [a + "_phys_0"] = 8L * 1024 * 1024 * 1024, [a + "_phys_1"] = 1024 * 1024 },
            new Dictionary<string, long> { [b + "_phys_0"] = 0 });
        Assert.Null(readings[0].DedicatedUsedMiB);
        Assert.Equal(0, readings[0].SharedUsedMiB);
        Assert.Equal(8193, readings[1].DedicatedUsedMiB);
        Assert.Null(readings[1].SharedUsedMiB);
    }

    [Fact]
    public void NativeWindowsProbeReturnsUniqueDevicesAndNonnegativeReadings()
    {
        using var probe = new WindowsGpuMemoryProbe();
        var readings = probe.Read();
        Assert.Equal(readings.Count, readings.Select(reading => reading.DeviceId).Distinct().Count());
        Assert.All(readings, reading =>
        {
            Assert.False(string.IsNullOrWhiteSpace(reading.DeviceName));
            Assert.True(reading.DedicatedCapacityMiB is null or >= 0);
            Assert.True(reading.DedicatedUsedMiB is null or >= 0);
            Assert.True(reading.SharedUsedMiB is null or >= 0);
        });
        TestContext.Current.TestOutputHelper?.WriteLine(JsonSerializer.Serialize(readings));
    }

    [Fact]
    public async Task MemoryRoundTripsAndCsvKeepsNumericColumnsPerDeviceIncludingZero()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "memory.db"));
        await store.InitializeAsync();
        var token = TestContext.Current.CancellationToken;
        var job = await new JobEngine(store, Path.Combine(root, "logs")).CreateAsync(BenchmarkApplicationService.JobKind, "{}", token);
        var result = Parse("""{"n_prompt":512,"n_gen":128,"avg_ts":42} """);
        await store.InsertBenchmarkResultAsync(job.Id, "item", 1, 1, result, token);
        await store.SetBenchmarkMemoryAsync(job.Id, "item", 1,
            [new("amd", "AMD", 24576, 23000, 0, 15), new("intel", "Intel", 0, null, 2000, 15)], 1000);
        var row = Assert.Single(await store.ListBenchmarkResultsAsync(job.Id, cancellationToken: token));
        Assert.Equal("process", row.Result.GpuMemoryMeasurementWindow);
        Assert.Equal(23000, row.Result.GpuMemoryPeaks![0].PeakDedicatedUsedMiB);
        var csv = BenchmarkExportService.Csv([row]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = csv[0].Split(',');
        var values = csv[1].Split(',');
        Assert.Equal(header.Length, values.Length);
        Assert.Equal("23000", values[Array.IndexOf(header, "gpu_0_peak_dedicated_mib")]);
        Assert.Equal("0", values[Array.IndexOf(header, "gpu_0_peak_shared_mib")]);
        Assert.Equal("", values[Array.IndexOf(header, "gpu_1_peak_dedicated_mib")]);
        Assert.Equal("", values[Array.IndexOf(header, "gpu_memory_used_mib")]);
        Assert.Contains("process peak", BenchmarkMemoryReportService.Label(row.Result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportsUseMaximumAcrossRepetitionsAndLabelLegacyAndUnavailableValues()
    {
        var legacy = Parse("""{"n_prompt":512,"n_gen":128,"avg_ts":42,"gpu_memory_used_mib":15000} """);
        Assert.Contains("snapshot", BenchmarkMemoryReportService.Label(legacy), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("peak unavailable", BenchmarkMemoryReportService.Label(legacy), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("GPU memory: Unavailable", BenchmarkMemoryReportService.Label(legacy with { ObservedGpuMemoryUsedMiB = 0 }));
        var first = legacy with { GpuMemoryPeaks = [new("a", "AMD", 24576, 20000, null, 3)], GpuMemoryMeasurementWindow = "workload" };
        var second = first with { GpuMemoryPeaks = [new("a", "AMD", 24576, 23000, 0, 3)] };
        var report = BenchmarkSpeedReportService.Build([Row(first), Row(second)]);
        Assert.Contains("23,000 MiB", report[0].Bars[0].Label, StringComparison.Ordinal);
        Assert.Contains("includes other applications", report[0].Bars[0].Label, StringComparison.Ordinal);
    }

    private static BenchmarkParsedResult Parse(string json)
    {
        Assert.True(BenchmarkResultService.TryParse(json, "model", "command", RuntimeMode.Native, RuntimeBackend.Vulkan, out var result, out var error), error);
        return result!;
    }

    private static StoredBenchmarkResult Row(BenchmarkParsedResult result)
        => new(1, "job", "item", 1, 1, false, result, DateTimeOffset.UtcNow);

    private sealed class Probe(Func<IReadOnlyList<GpuMemorySample>> read) : IGpuMemoryProbe
    {
        public bool Disposed { get; private set; }
        public IReadOnlyList<GpuMemorySample> Read() => read();
        public void Dispose() => Disposed = true;
    }
}
