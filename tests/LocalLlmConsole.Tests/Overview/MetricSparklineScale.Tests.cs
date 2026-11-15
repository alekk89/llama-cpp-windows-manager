using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class MetricSparklineScaleTests
{
    [Theory]
    [InlineData(0.7, 0.8)]
    [InlineData(1, 1)]
    [InlineData(64.8, 80)]
    [InlineData(101, 120)]
    [InlineData(2300, 2500)]
    public void NiceCeilingRoundsUpToAShortStep(double value, double expected)
        => Assert.Equal(expected, MetricSparklineScale.NiceCeiling(value), precision: 9);

    [Fact]
    public void StableScaleKeepsSmallDropsSmallAndGrowsImmediately()
    {
        var maximum = MetricSparklineScale.StableMaximum(60, null);
        Assert.Equal(80, maximum);

        // 60 -> 55 t/s neither moves the axis nor reads as a cliff.
        var afterSmallDrop = MetricSparklineScale.StableMaximum(55, maximum);
        Assert.Equal(maximum, afterSmallDrop);
        var range = new MetricSparklineRange(0, afterSmallDrop);
        Assert.InRange(range.Fraction(60) - range.Fraction(55), 0.06, 0.07);

        // A spike leaving the window does not collapse the axis while data still uses it.
        Assert.Equal(maximum, MetricSparklineScale.StableMaximum(40, maximum));

        // Exceeding the axis grows it at once.
        Assert.Equal(120, MetricSparklineScale.StableMaximum(95, maximum));
    }

    [Fact]
    public void StableScaleShrinksOnlyAfterALargeSustainedDrop()
    {
        Assert.Equal(25, MetricSparklineScale.StableMaximum(20, 80));
        Assert.Equal(80, MetricSparklineScale.StableMaximum(0, 80));
        Assert.Equal(1, MetricSparklineScale.StableMaximum(0, null));
    }

    [Fact]
    public void FitRangePadsTheVisibleRangeWithoutGoingNegative()
    {
        var fit = MetricSparklineScale.FitRange(55, 60);
        Assert.Equal(54.5, fit.Minimum, precision: 9);
        Assert.Equal(60.5, fit.Maximum, precision: 9);
        Assert.True(fit.Fraction(60) - fit.Fraction(55) > .8);

        Assert.Equal(0, MetricSparklineScale.FitRange(0.2, 10).Minimum);
        var flat = MetricSparklineScale.FitRange(42, 42);
        Assert.True(flat.Minimum < 42 && flat.Maximum > 42);
        Assert.Equal(.5, flat.Fraction(42), precision: 9);
        Assert.Equal(new MetricSparklineRange(0, 1), MetricSparklineScale.FitRange(double.NaN, 1));
    }

    [Fact]
    public void SummaryIgnoresGapsAndReportsLatestValue()
    {
        var summary = MetricSparklineScale.Summarize([null, 58, 61.5, double.NaN, 55, null]);
        Assert.Equal(new MetricSparklineSummary(55, 61.5, 55), summary);
        Assert.Null(MetricSparklineScale.Summarize([null, double.PositiveInfinity]));
    }

    [Theory]
    [InlineData(12345.6, "12,346")]
    [InlineData(123.4, "123")]
    [InlineData(55.34, "55.3")]
    [InlineData(1.234, "1.23")]
    [InlineData(0.0042, "0.004")]
    public void ReadoutValuesFollowTheirMagnitude(double value, string expected)
        => Assert.Equal(expected, MetricSparklineScale.FormatValue(value, CultureInfo.InvariantCulture));

    [Fact]
    public void SpanAndReadoutUseLocalizedLabels()
    {
        var previousLanguage = Loc.CurrentLanguage;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            Loc.LoadLanguage("en");
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal("Last 45 s", MetricSparkline.FormatSpan(TimeSpan.FromSeconds(45)));
            Assert.Equal("Last 1 s", MetricSparkline.FormatSpan(TimeSpan.FromMilliseconds(20)));
            Assert.Equal("Last 2 min", MetricSparkline.FormatSpan(TimeSpan.FromSeconds(118)));
            Assert.Equal("Last 1.5 h", MetricSparkline.FormatSpan(TimeSpan.FromMinutes(90)));
            Assert.Equal("Min 55 · Max 60 · Latest 58.2 t/s",
                MetricSparkline.FormatReadout(new MetricSparklineSummary(55, 60, 58.2), "t/s"));
            Assert.Equal("Min 1 · Max 2 · Latest 2",
                MetricSparkline.FormatReadout(new MetricSparklineSummary(1, 2, 2), ""));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            Loc.LoadLanguage(previousLanguage);
        }
    }

    [Fact]
    public void ChartScalePersistsAndOlderLayoutsDefaultToStable()
    {
        var layout = OverviewDashboardLayoutPolicy.Default;
        var cardId = layout.Cards[0].Id;
        var fitted = OverviewDashboardLayoutPolicy.SetChartScale(layout, cardId, OverviewDashboardChartScale.FitToData);
        Assert.Equal(OverviewDashboardChartScale.FitToData, fitted.Cards[0].ChartScale);
        Assert.All(fitted.Cards.Skip(1), card => Assert.Equal(OverviewDashboardChartScale.Stable, card.ChartScale));

        var roundTripped = OverviewDashboardLayoutPolicy.Normalize(
            JsonSerializer.Deserialize<OverviewDashboardLayout>(JsonSerializer.Serialize(fitted)));
        Assert.Equal(OverviewDashboardChartScale.FitToData, roundTripped.Cards[0].ChartScale);

        var legacyJson = JsonNode.Parse(JsonSerializer.Serialize(layout))!;
        foreach (var card in legacyJson["Cards"]!.AsArray())
            card!.AsObject().Remove(nameof(OverviewDashboardCardLayout.ChartScale));
        var legacy = OverviewDashboardLayoutPolicy.Normalize(
            JsonSerializer.Deserialize<OverviewDashboardLayout>(legacyJson.ToJsonString()));
        Assert.All(legacy.Cards, card => Assert.Equal(OverviewDashboardChartScale.Stable, card.ChartScale));

        var invalid = OverviewDashboardLayoutPolicy.Normalize(layout with
        {
            Cards = [layout.Cards[0] with { ChartScale = (OverviewDashboardChartScale)42 }]
        });
        Assert.Equal(OverviewDashboardChartScale.Stable, invalid.Cards[0].ChartScale);
    }
}
