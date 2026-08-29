using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class RuntimeMetricBaselinesTests : ManagerRegressionTestBase
{
    [Fact]
    public void RuntimeMetricSummaryTrackerKeepsPerRuntimeRateBaselines()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot());
        var tracker = new RuntimeMetricSummaryTracker();
        var capturedAt = DateTimeOffset.Parse("2026-05-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        tracker.Apply(
            "model-a|runtime|8081",
            [
                new PrometheusSample("llama_tokens_predicted_total", "", 10, "10", "counter", ""),
                new PrometheusSample("llama_tokens_predicted_seconds_total", "", 5, "5", "counter", "")
            ],
            settings,
            slotSnapshot: null,
            mtpTokenSnapshot: null,
            capturedAt);
        tracker.Apply(
            "model-b|runtime|8082",
            [
                new PrometheusSample("llama_tokens_predicted_total", "", 100, "100", "counter", ""),
                new PrometheusSample("llama_tokens_predicted_seconds_total", "", 50, "50", "counter", "")
            ],
            settings,
            slotSnapshot: null,
            mtpTokenSnapshot: null,
            capturedAt.AddSeconds(1));

        var secondA = tracker.Apply(
            "model-a|runtime|8081",
            [
                new PrometheusSample("llama_tokens_predicted_total", "", 16, "16", "counter", ""),
                new PrometheusSample("llama_tokens_predicted_seconds_total", "", 8, "8", "counter", "")
            ],
            settings,
            slotSnapshot: null,
            mtpTokenSnapshot: null,
            capturedAt.AddSeconds(2));

        Assert.Equal("Gen 2.0 t/s (2.0 avg)\nPrompt Unknown", secondA.GenerationRate);
        Assert.Equal(2, secondA.Atomic.GenerationRate);
        Assert.Equal(2, secondA.Atomic.AverageGenerationRate);
        Assert.Equal(16, secondA.Atomic.GeneratedTokens);
    }

    [Fact]
    public void RuntimeMetricSummaryTrackerUsesLogMtpDurationsForAverages()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with { SpeculativeType = "draft-mtp" };
        var tracker = new RuntimeMetricSummaryTracker();
        var capturedAt = DateTimeOffset.Parse("2026-05-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var firstStats = RuntimeDashboardService.ParseMtpTokenStats(
            "statistics draft-mtp: #calls(b,g,a) = 1 10 10, #gen drafts = 10, #acc drafts = 8, #gen tokens = 100, #acc tokens = 80, dur(b,g,a) = 0.001, 10000.000, 0.250 ms");
        var secondStats = RuntimeDashboardService.ParseMtpTokenStats(
            "statistics draft-mtp: #calls(b,g,a) = 2 20 20, #gen drafts = 20, #acc drafts = 13, #gen tokens = 160, #acc tokens = 130, dur(b,g,a) = 0.001, 20000.000, 0.500 ms");

        var first = tracker.Apply("model|runtime|8081", [], settings, null, firstStats, capturedAt);
        var second = tracker.Apply("model|runtime|8081", [], settings, null, secondStats, capturedAt.AddSeconds(2));
        var idle = tracker.Apply("model|runtime|8081", [], settings, null, secondStats, capturedAt.AddSeconds(4));
        var stale = tracker.Apply("model|runtime|8081", [], settings, null, null, capturedAt.AddSeconds(6));

        Assert.Equal("Unknown (Gen) | 10.0 t/s (Avg) | 100 t (Total)\nUnknown (Accepted) | 8.0 t/s (Avg) | 80 t (Total)", first.MtpTokens);
        Assert.Equal("30.0 t/s (Gen) | 8.0 t/s (Avg) | 160 t (Total)\n25.0 t/s (Accepted) | 6.5 t/s (Avg) | 130 t (Total)", second.MtpTokens);
        Assert.Equal("0.0 t/s (Gen) | 8.0 t/s (Avg) | 160 t (Total)\n0.0 t/s (Accepted) | 6.5 t/s (Avg) | 130 t (Total)", idle.MtpTokens);
        Assert.Equal(0, idle.Atomic.MtpGeneratedRate);
        Assert.Equal(8, idle.Atomic.AverageMtpGeneratedRate);
        Assert.Equal(160, idle.Atomic.MtpGeneratedTokens);
        Assert.True(stale.UsedLastKnown);
        Assert.Equal(idle.MtpTokens, stale.MtpTokens);
        Assert.Equal(idle.Atomic, stale.Atomic);
    }

    [Fact]
    public void GpuSummaryCacheOwnsFreshnessAndFallback()
    {
        var cache = new GpuSummaryCache();
        var now = DateTimeOffset.Parse("2026-05-28T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.False(cache.TryGet(now, out var initial));
        Assert.Equal("Unavailable", initial);
        Assert.Equal("Intel Arc 24 GB free", cache.Store("Intel Arc 24 GB free", now));
        Assert.True(cache.TryGet(now.AddSeconds(1), out var fresh));
        Assert.Equal("Intel Arc 24 GB free", fresh);
        Assert.False(cache.TryGet(now.AddSeconds(10), out var expired));
        Assert.Equal("Unavailable", expired);
        Assert.Equal("Unavailable", cache.Store("", now));
        Assert.Equal("GPU 0: 76% | 62C | 12.0/24.0 GiB", cache.Store("GPU 0: 76%|62C|12.0/24.0 GiB", now));
        Assert.Equal("NVIDIA 16 GB free", cache.Store("cuda", "NVIDIA 16 GB free", now));
        Assert.False(cache.TryGet("vulkan", now.AddSeconds(1), out var wrongKey));
        Assert.Equal("Unavailable", wrongKey);
        Assert.True(cache.TryGet("cuda", now.AddSeconds(1), out var retainedKey));
        Assert.Equal("NVIDIA 16 GB free", retainedKey);

        cache.Store("NVIDIA 16 GB free", now);
        cache.Clear();
        Assert.False(cache.TryGet(now.AddSeconds(1), out var cleared));
        Assert.Equal("Unavailable", cleared);
    }

    [Fact]
    public async Task GpuSummaryCacheCoalescesConcurrentProbeRequests()
    {
        var cache = new GpuSummaryCache();
        var now = DateTimeOffset.Parse("2026-08-24T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        Task<string> Factory()
        {
            Interlocked.Increment(ref calls);
            return release.Task;
        }

        var first = cache.GetOrCreateAsync("host", now, Factory, TestContext.Current.CancellationToken);
        var second = cache.GetOrCreateAsync("host", now, Factory, TestContext.Current.CancellationToken);
        release.SetResult("GPU 0: 25% load");

        Assert.Equal(["GPU 0: 25% load", "GPU 0: 25% load"], await Task.WhenAll(first, second));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GpuSummaryCacheFreshnessStartsWhenSlowProbeCompletes()
    {
        var cache = new GpuSummaryCache();
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        await cache.GetOrCreateSnapshotAsync(
            "slow",
            now,
            async () =>
            {
                await Task.Delay(150, TestContext.Current.CancellationToken);
                return HostHardwareSnapshot.Unavailable(now);
            },
            TestContext.Current.CancellationToken);

        Assert.True(cache.TryGetSnapshot("slow", now.AddSeconds(10.05), out _));
    }
}
