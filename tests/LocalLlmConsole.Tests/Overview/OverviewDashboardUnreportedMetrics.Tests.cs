using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class OverviewDashboardUnreportedMetricsTests
{
    [Fact]
    public void HidesUnreportedMetric_GeneratedGpuCardSensor_IsHidden()
        => Assert.True(OverviewDashboardLayoutPolicy.HidesUnreportedMetric(
            "default-gpu-1", OverviewDashboardMetricIds.GpuTemperature(1)));

    [Fact]
    public void HidesUnreportedMetric_SensorAddedToGeneratedCard_StaysVisible()
        => Assert.False(OverviewDashboardLayoutPolicy.HidesUnreportedMetric(
            "default-host", OverviewDashboardMetricIds.CpuTemperature));

    [Fact]
    public void HidesUnreportedMetric_OtherGpuMetricOnGeneratedGpuCard_StaysVisible()
        => Assert.False(OverviewDashboardLayoutPolicy.HidesUnreportedMetric(
            "default-gpu-1", OverviewDashboardMetricIds.GpuTemperature(0)));

    [Theory]
    [InlineData("card")]
    [InlineData("card-3")]
    [InlineData("default-gpu-x")]
    [InlineData("")]
    public void HidesUnreportedMetric_UserCard_StaysVisible(string cardId)
        => Assert.False(OverviewDashboardLayoutPolicy.HidesUnreportedMetric(
            cardId, OverviewDashboardMetricIds.GpuTemperature(0)));

    [Fact]
    public void HidesUnreportedMetric_EveryGeneratedDefaultMetric_IsHidden()
    {
        var layout = OverviewDashboardLayoutPolicy.WithDetectedGpuCards(OverviewDashboardLayoutPolicy.Default, [0]);

        Assert.All(layout.Cards, card => Assert.All(card.MetricIds, metricId =>
            Assert.True(OverviewDashboardLayoutPolicy.HidesUnreportedMetric(card.Id, metricId))));
    }

    [Theory]
    [InlineData(OverviewDashboardMetricIds.CpuTemperature, OverviewDashboardMetricSource.Hardware)]
    [InlineData("overview.hardware.gpu-temperature:0", OverviewDashboardMetricSource.Hardware)]
    [InlineData(OverviewDashboardMetricIds.GatewayFailureRate, OverviewDashboardMetricSource.Gateway)]
    [InlineData(OverviewDashboardMetricIds.ContextShifts, OverviewDashboardMetricSource.Runtime)]
    [InlineData("prometheus:llama_tokens|", OverviewDashboardMetricSource.Runtime)]
    public void MetricSource_ClassifiesByMetricFamily(string metricId, OverviewDashboardMetricSource expected)
        => Assert.Equal(expected, OverviewDashboardLayoutPolicy.MetricSource(metricId));
}
