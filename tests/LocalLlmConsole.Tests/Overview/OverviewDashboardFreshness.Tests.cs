using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class OverviewDashboardFreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OldestLastKnown_LiveReadingsOnly_ReturnsNull()
        => Assert.Null(OverviewDashboardFreshnessPolicy.OldestLastKnown([null, null]));

    [Fact]
    public void OldestLastKnown_MixedReadings_ReturnsOldestCapture()
    {
        var older = Now.AddMinutes(-5);
        var newer = Now.AddMinutes(-1);

        Assert.Equal(older, OverviewDashboardFreshnessPolicy.OldestLastKnown([null, newer, older]));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(-30, "")]
    [InlineData(1, "1s")]
    [InlineData(59, "59s")]
    [InlineData(60, "1m")]
    [InlineData(150, "2m")]
    [InlineData(3599, "59m")]
    [InlineData(3600, "1h 00m")]
    [InlineData(3 * 3600 + 7 * 60 + 40, "3h 07m")]
    public void RelativeAge_FormatsCoarseCompactAge(int ageSeconds, string expected)
        => Assert.Equal(expected, OverviewDashboardFreshnessPolicy.RelativeAge(Now.AddSeconds(-ageSeconds), Now));
}
