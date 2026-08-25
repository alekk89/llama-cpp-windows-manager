using System.Globalization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void RuntimeMetricSummaryDerivesCuratedEfficiencyAndContextReadings()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot());
        var samples = new[]
        {
            Sample("llama_prompt_tokens_total", 200),
            Sample("llama_prompt_tokens_cached_total", 800),
            Sample("llama_mtp_tokens_generated_total", 100),
            Sample("llama_mtp_tokens_accepted_total", 75),
            Sample("llama_n_tokens_max", 4096, "gauge"),
            Sample("llama_context_shifts_total", 3)
        };

        var summary = new RuntimeMetricSummaryTracker().Apply(
            "runtime", samples, settings, null, null, DateTimeOffset.UtcNow);

        Assert.Equal(80, summary.Atomic.PromptCacheReusePercent);
        Assert.Equal(800, summary.Atomic.PromptCachedTokens);
        Assert.Equal(75, summary.Atomic.DraftAcceptancePercent);
        Assert.Equal(4096, summary.Atomic.PeakContextTokens);
        Assert.Equal(3, summary.Atomic.ContextShiftCount);
    }

    [Fact]
    public void DashboardRegistryDiscoversProcessAndExtendedGpuSensors()
    {
        var registry = new OverviewDashboardMetricRegistry();
        var readings = registry.ObserveHardware(
            "Process: 12.5% CPU | 3.25 GiB private RAM\n" +
            "GPU 0: NVIDIA Test | 60% load | 1200 MHz memory | 33% memory | 40% fan | 300 W limit | throttle none");

        Assert.Contains(readings, reading => reading.MetricId == OverviewDashboardMetricIds.ServerProcessCpu && reading.Primary == 12.5);
        Assert.Contains(readings, reading => reading.MetricId == OverviewDashboardMetricIds.ServerProcessMemory && reading.Primary == 3.25);
        Assert.Contains(readings, reading => reading.MetricId == OverviewDashboardMetricIds.GpuMemoryClock(0) && reading.Primary == 1200);
        Assert.Contains(readings, reading => reading.MetricId == OverviewDashboardMetricIds.GpuMemoryActivity(0) && reading.Primary == 33);
        Assert.Contains(readings, reading => reading.MetricId == OverviewDashboardMetricIds.GpuFanSpeed(0) && reading.Primary == 40);
        Assert.Contains(readings, reading => reading.MetricId == OverviewDashboardMetricIds.GpuPowerLimit(0) && reading.Primary == 300);
        Assert.Contains(readings, reading => reading.MetricId == OverviewDashboardMetricIds.GpuThrottling(0) && reading.Primary == 0);
    }

    [Fact]
    public void VersionNineDraftAverageMigratesToAcceptancePercentage()
    {
        var old = new OverviewDashboardLayout(9,
        [
            new OverviewDashboardCardLayout("draft", [OverviewDashboardMetricIds.AverageMtpAcceptedRate],
                ChartMetricIds: [OverviewDashboardMetricIds.AverageMtpAcceptedRate])
        ]);

        var migrated = OverviewDashboardLayoutPolicy.Normalize(old);

        Assert.Equal([OverviewDashboardMetricIds.DraftAcceptance], migrated.Cards.Single().MetricIds);
        Assert.Equal([OverviewDashboardMetricIds.DraftAcceptance], migrated.Cards.Single().ChartMetricIds);
    }

    [Fact]
    public void GatewayPerformanceTrackerReportsHealthAndLatestLatency()
    {
        var tracker = new GatewayPerformanceTracker();
        tracker.Observe(true, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(100), 20);
        tracker.Observe(false, TimeSpan.FromMilliseconds(250), null, null);

        var snapshot = tracker.Snapshot();

        Assert.Equal(2, snapshot.RequestCount);
        Assert.Equal(1, snapshot.FailureCount);
        Assert.Equal(50, snapshot.FailureRatePercent);
        Assert.Equal(250, snapshot.LastRequestDurationMilliseconds);
        Assert.Null(snapshot.LastTimeToFirstDataMilliseconds);
    }

    [Fact]
    public void NvidiaExtendedSensorsMergeWithoutReplacingCoreTelemetry()
    {
        var merged = GpuStatusService.MergeNvidiaExtendedSummary(
            ["GPU 0: Test | 50% load | 100 W"],
            "0, 1400, 22, 35, 250, 0x0000000000000000, 76");

        Assert.Contains("50% load", merged, StringComparison.Ordinal);
        Assert.Contains("1400 MHz memory", merged, StringComparison.Ordinal);
        Assert.Contains("22% memory", merged, StringComparison.Ordinal);
        Assert.Contains("35% fan", merged, StringComparison.Ordinal);
        Assert.Contains("250 W limit", merged, StringComparison.Ordinal);
        Assert.Contains("throttle none", merged, StringComparison.Ordinal);
        Assert.Contains("76 °C memory", merged, StringComparison.Ordinal);
    }

    private static PrometheusSample Sample(string name, double value, string type = "counter")
        => new(name, "", value, value.ToString(CultureInfo.InvariantCulture), type, "");
}
