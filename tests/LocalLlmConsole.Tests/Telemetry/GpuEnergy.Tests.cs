using System.Globalization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class GpuEnergyTests : ManagerRegressionTestBase
{
    [Fact]
    public void HostHardwareSnapshotParsesFormattedTelemetryOnceForDashboardAndEnergyConsumers()
    {
        var capturedAt = DateTimeOffset.Parse("2026-08-24T12:00:00Z", CultureInfo.InvariantCulture);
        var snapshot = HostHardwareSnapshotParser.Parse(
            "CPU: AMD Ryzen\nTelemetry: 12% load | 5100 MHz core | 16 cores | 32 threads\n"
            + "RAM: 50% load | 16.0/32.0 GiB | 6000 MHz\n"
            + "GPU 0: NVIDIA RTX | 75% load | 20.0/24.0 GiB VRAM | 225 W | 300 W limit | 1800 MHz core | 70 °C | 82 °C memory\n"
            + "Process: 4.5% CPU | 3.25 GiB private RAM",
            capturedAt);

        Assert.Equal(12, snapshot.Cpu?.UtilizationPercent);
        Assert.Equal(16, snapshot.Cpu?.PhysicalCores);
        Assert.Equal(16, snapshot.Memory?.UsedGibibytes);
        Assert.Equal(225, snapshot.Gpus.Single().PowerWatts);
        Assert.Equal(300, snapshot.Gpus.Single().PowerLimitWatts);
        Assert.Equal(82, snapshot.Gpus.Single().MemoryTemperatureCelsius);
        Assert.Equal(3.25, snapshot.Process?.PrivateMemoryGibibytes);

        var power = GpuPowerObservationParser.Parse(snapshot, capturedAt);
        Assert.Equal(225, power.TotalWatts);
        Assert.Equal(1, power.DetectedGpuCount);
    }

    [Theory]
    [InlineData("NVIDIA RTX 3090", 250)]
    [InlineData("AMD Radeon RX 7900 XTX", 550)]
    [InlineData("Intel Arc B580", 190)]
    public void PowerLimitWithoutDrawIsNotRecordedAsGpuPower(string gpuName, double powerLimit)
    {
        var capturedAt = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        var snapshot = HostHardwareSnapshotParser.Parse(
            $"GPU 0: {gpuName} | 50% load | {powerLimit:0.#} W limit",
            capturedAt);

        var gpu = Assert.Single(snapshot.Gpus);
        Assert.Null(gpu.PowerWatts);
        Assert.Equal(powerLimit, gpu.PowerLimitWatts);

        var observation = GpuPowerObservationParser.Parse(snapshot, capturedAt);
        Assert.Empty(observation.Sensors);
        Assert.Equal(0, observation.TotalWatts);
        Assert.Equal(0, observation.ObservedGpuCount);
        Assert.Equal(1, observation.DetectedGpuCount);
    }

    [Fact]
    public void GpuPowerObservationParserTotalsOnlyObservedSensorsAndReportsCoverage()
    {
        var capturedAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        var observation = GpuPowerObservationParser.Parse(
            "GPU 0: NVIDIA RTX 4090 | 80% load | 205.4 W\n" +
            "GPU 1: AMD Radeon RX 7900 XTX | 70% load\n" +
            "GPU 2: Intel Arc B580 | 66 W / 190 W",
            capturedAt);

        Assert.Equal(271.4, observation.TotalWatts, precision: 6);
        Assert.Equal(2, observation.ObservedGpuCount);
        Assert.Equal(3, observation.DetectedGpuCount);
        Assert.False(observation.HasCompleteCoverage);
        Assert.Equal(capturedAt, observation.CapturedAt);
        Assert.Collection(
            observation.Sensors,
            sensor =>
            {
                Assert.Equal(0, sensor.GpuIndex);
                Assert.Equal("NVIDIA RTX 4090", sensor.GpuName);
                Assert.Equal(205.4, sensor.Watts, precision: 6);
            },
            sensor =>
            {
                Assert.Equal(2, sensor.GpuIndex);
                Assert.Equal("Intel Arc B580", sensor.GpuName);
                Assert.Equal(66, sensor.Watts, precision: 6);
            });
    }

    [Fact]
    public void VendorPowerFormattersNormalizeIntelAndAmdTelemetry()
    {
        var intel = GpuStatusVendorPowerFormatter.FormatIntelXpuSmi("""
            |   0  Intel(R) Arc(TM) B580   Off             | 0000:03:00.0      Off      | Disabled |
            | N/A    25C  66W / 190W                       | 239MiB / 12216MiB          | 42% Default |
            """);
        Assert.Equal(
            "GPU 0: Intel(R) Arc(TM) B580 | 42% load | 25 °C | 0.2/11.9 GiB VRAM | 66 W | 190 W limit",
            Assert.Single(intel));

        var amd = GpuStatusVendorPowerFormatter.FormatAmdSmi("""
            0  1  29856, 63046  47.0°C  110.0W  NPS1 SPX 0  210Mhz 1300Mhz 0% auto 550W 0% 61%
            """);
        Assert.Equal(
            "GPU 0: AMD GPU | 61% load | 47.0 °C | 110.0 W | 210 MHz core | 1300 MHz memory | 0% memory | 0% fan | 550 W limit",
            Assert.Single(amd));
    }

    [Fact]
    public void GpuEnergyAccumulatorIntegratesPowerAndSplitsUtcHours()
    {
        var accumulator = new GpuEnergyAccumulator();
        var first = new DateTimeOffset(2026, 8, 24, 10, 59, 55, TimeSpan.Zero);
        Assert.Empty(accumulator.Observe(Observation(first, 100)));

        var deltas = accumulator.Observe(Observation(first.AddSeconds(10), 200));

        Assert.Equal(2, deltas.Count);
        Assert.Equal(first.AddMinutes(-59).AddSeconds(-55), deltas[0].BucketStartUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero), deltas[1].BucketStartUtc);
        Assert.Equal(10, deltas.Sum(delta => delta.SampledSeconds), precision: 6);
        Assert.Equal(150 * 10 / 3600d, deltas.Sum(delta => delta.WattHours), precision: 6);
        Assert.All(deltas, delta => Assert.True(delta.CompleteCoverage));
    }

    [Fact]
    public void GpuEnergyAccumulatorDoesNotEstimateAcrossGapsOrSensorChanges()
    {
        var accumulator = new GpuEnergyAccumulator();
        var start = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        accumulator.Observe(Observation(start, 100));

        Assert.Empty(accumulator.Observe(Observation(start.AddMinutes(1), 100)));
        Assert.Empty(accumulator.Observe(Observation(start.AddMinutes(1).AddSeconds(10), 100, "GPU 1")));
        Assert.Single(accumulator.Observe(Observation(start.AddMinutes(1).AddSeconds(20), 100, "GPU 1")));
    }

    [Fact]
    public void GpuEnergyAccumulatorIntegratesEachObservedGpuSeparately()
    {
        var accumulator = new GpuEnergyAccumulator();
        var start = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        Assert.Empty(accumulator.ObserveDetailed(DeviceObservation(start, 100, 200)).DeviceDeltas);

        var result = accumulator.ObserveDetailed(DeviceObservation(start.AddSeconds(10), 200, 400));

        Assert.Equal(2, result.DeviceDeltas.Count);
        Assert.Equal(150 * 10 / 3600d, result.DeviceDeltas.Single(delta => delta.GpuIndex == 0).WattHours, 6);
        Assert.Equal(300 * 10 / 3600d, result.DeviceDeltas.Single(delta => delta.GpuIndex == 1).WattHours, 6);
        Assert.Equal(
            result.TotalDeltas.Sum(delta => delta.WattHours),
            result.DeviceDeltas.Sum(delta => delta.WattHours),
            6);
    }

    [Fact]
    public async Task StateStorePersistsHourlyGpuEnergyAndCoverage()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var hour = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        await store.RecordGpuEnergyAsync(
            [
                new GpuEnergyDelta(hour, 1.5, 10, true, 2, 2, hour.AddSeconds(10)),
                new GpuEnergyDelta(hour, 2.5, 10, false, 1, 2, hour.AddSeconds(20))
            ],
            [
                new GpuEnergyDeviceDelta(hour, "GPU 0: NVIDIA", 0, "NVIDIA", 1, 10, hour.AddSeconds(10)),
                new GpuEnergyDeviceDelta(hour, "GPU 0: NVIDIA", 0, "NVIDIA", 2, 10, hour.AddSeconds(20))
            ]);

        var bucket = Assert.Single(await store.ListGpuEnergyBucketsAsync());
        Assert.Equal(4, bucket.WattHours, precision: 6);
        Assert.Equal(20, bucket.SampledSeconds, precision: 6);
        Assert.False(bucket.CompleteCoverage);
        Assert.Equal(1, bucket.ObservedGpuCount);
        Assert.Equal(2, bucket.DetectedGpuCount);
        Assert.Equal(hour, await store.GetGpuEnergyTrackingStartedAtAsync());
        var device = Assert.Single(await store.ListGpuEnergyDeviceBucketsAsync());
        Assert.Equal(3, device.WattHours, precision: 6);
        Assert.Equal(20, device.SampledSeconds, precision: 6);
        Assert.Equal("NVIDIA", device.GpuName);

        await store.DeleteAllGpuEnergyAsync();
        Assert.Empty(await store.ListGpuEnergyBucketsAsync());
        Assert.Empty(await store.ListGpuEnergyDeviceBucketsAsync());
    }

    [Fact]
    public void UsageReportIncludesHostEnergyWithoutAttributingItToAModel()
    {
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var energy = new GpuEnergyBucket(now.AddHours(-1), 1250, 3600, true, 2, 2, now);
        var devices = new[]
        {
            new GpuEnergyDeviceBucket(now.AddHours(-1), "GPU 0: NVIDIA", 0, "NVIDIA", 750, 3600, now),
            new GpuEnergyDeviceBucket(now.AddHours(-1), "GPU 1: AMD", 1, "AMD", 500, 3600, now)
        };

        var report = new UsageMetricsService().BuildReport(
            new UsageMetricsQuery(UsageMetricsRange.OneDay, ModelId: "model-a"),
            [],
            [],
            UsageMetricDimensions.Empty,
            now,
            TimeZoneInfo.Utc,
            energyBuckets: [energy],
            energyTrackingStartedAt: energy.BucketStartUtc,
            energyDeviceBuckets: devices);

        Assert.NotNull(report.GpuEnergy);
        Assert.Equal(1.25, report.GpuEnergy!.KilowattHours, precision: 6);
        Assert.True(report.GpuEnergy.CompleteCoverage);
        Assert.Equal(1250, Assert.Single(report.Days).GpuEnergy!.WattHours, precision: 6);
        Assert.Collection(
            report.GpuEnergyDevices!,
            device => Assert.Equal(750, device.WattHours, precision: 6),
            device => Assert.Equal(500, device.WattHours, precision: 6));
        Assert.Empty(report.Models);
    }

    [Fact]
    public async Task LifetimeMetricsApplicationThrottlesHostEnergySampling()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var application = new LifetimeMetricsApplicationService(store);
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(application.ReserveGpuEnergySample(now));
        Assert.False(application.ReserveGpuEnergySample(now.AddSeconds(9)));
        Assert.True(application.ReserveGpuEnergySample(now.AddSeconds(10)));
    }

    [Fact]
    public void GpuEnergySamplingPolicyUsesSessionOnlyHistoryByDefaultAndSupportsContinuousIdleTracking()
    {
        var idle = GpuEnergySamplingPolicy.Decide(hasRunningSessions: false, trackWhileIdle: false);
        var active = GpuEnergySamplingPolicy.Decide(hasRunningSessions: true, trackWhileIdle: false);
        var continuous = GpuEnergySamplingPolicy.Decide(hasRunningSessions: false, trackWhileIdle: true);

        Assert.False(idle.PersistHistory);
        Assert.Equal(TimeSpan.FromMinutes(5), idle.Interval);
        Assert.True(active.PersistHistory);
        Assert.Equal(TimeSpan.FromSeconds(10), active.Interval);
        Assert.True(continuous.PersistHistory);
        Assert.Equal(TimeSpan.FromSeconds(10), continuous.Interval);
    }

    [Fact]
    public async Task LifetimeMetricsApplicationCanObserveIdlePowerWithoutPersistingHistory()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var application = new LifetimeMetricsApplicationService(store);
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        application.SetGpuEnergyPersistenceActive(false);
        await application.ObserveGpuPowerAsync("GPU 0: NVIDIA RTX | 100 W", now, persistHistory: false);
        await application.ObserveGpuPowerAsync("GPU 0: NVIDIA RTX | 200 W", now.AddSeconds(10), persistHistory: false);

        var live = Assert.IsType<ObservedGpuEnergySnapshot>(application.ObservedGpuEnergySnapshot());
        Assert.Equal(150 * 10 / 3600d, live.WattHours, precision: 6);
        Assert.Empty(await store.ListGpuEnergyBucketsAsync());
        Assert.Empty(await store.ListGpuEnergyDeviceBucketsAsync());
        Assert.Equal(0, application.DataVersion);
    }

    [Fact]
    public async Task GpuEnergyPersistenceModeChangesBreakTheHistoricalIntegrationWindow()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var application = new LifetimeMetricsApplicationService(store);
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        await application.ObserveGpuPowerAsync("GPU 0: NVIDIA RTX | 100 W", now);
        await application.ObserveGpuPowerAsync("GPU 0: NVIDIA RTX | 100 W", now.AddSeconds(10));
        application.SetGpuEnergyPersistenceActive(false);
        application.SetGpuEnergyPersistenceActive(true);
        await application.ObserveGpuPowerAsync("GPU 0: NVIDIA RTX | 100 W", now.AddSeconds(20));
        await application.ObserveGpuPowerAsync("GPU 0: NVIDIA RTX | 100 W", now.AddSeconds(30));

        var historical = Assert.Single(await store.ListGpuEnergyBucketsAsync());
        Assert.Equal(100 * 20 / 3600d, historical.WattHours, precision: 6);
        Assert.Equal(20, historical.SampledSeconds, precision: 6);
    }

    [Fact]
    public void MinimizedUiRefreshPolicyRendersEveryFiveSecondsAndImmediatelyAfterReset()
    {
        var policy = new MinimizedUiRefreshPolicy();
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(policy.ShouldRender(minimized: true, now));
        Assert.False(policy.ShouldRender(minimized: true, now.AddSeconds(4)));
        Assert.True(policy.ShouldRender(minimized: true, now.AddSeconds(5)));
        Assert.True(policy.ShouldRender(minimized: false, now.AddSeconds(6)));
        policy.Reset();
        Assert.True(policy.ShouldRender(minimized: true, now.AddSeconds(6)));
    }

    [Fact]
    public void LifetimeMetricsRefreshPolicyRequiresNewDataAndTheRefreshDeadline()
    {
        var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        var next = now.AddSeconds(5);

        Assert.False(LifetimeMetricsRefreshPolicy.ShouldRefresh(true, true, 2, 1, now, next));
        Assert.True(LifetimeMetricsRefreshPolicy.ShouldRefresh(true, true, 2, 1, next, next));
        Assert.False(LifetimeMetricsRefreshPolicy.ShouldRefresh(true, true, 1, 1, next, next));
        Assert.False(LifetimeMetricsRefreshPolicy.ShouldRefresh(false, true, 2, 1, next, next));
        Assert.False(LifetimeMetricsRefreshPolicy.ShouldRefresh(true, false, 2, 1, next, next));
    }

    [Fact]
    public async Task LifetimeMetricsApplicationFeedsObservedLiveAndHistoricalEnergyFromTheSameSamples()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var application = new LifetimeMetricsApplicationService(store);
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        await application.ObserveGpuPowerAsync("GPU 0: NVIDIA RTX | 100 W", now);
        Assert.Equal(0, application.DataVersion);
        await application.ObserveGpuPowerAsync("GPU 0: NVIDIA RTX | 200 W", now.AddSeconds(10));
        Assert.Equal(1, application.DataVersion);

        var live = Assert.IsType<ObservedGpuEnergySnapshot>(application.ObservedGpuEnergySnapshot());
        var historical = Assert.Single(await store.ListGpuEnergyDeviceBucketsAsync());
        Assert.Equal(150 * 10 / 3600d, live.WattHours, precision: 6);
        Assert.Equal(live.WattHours, historical.WattHours, precision: 6);
    }

    [Fact]
    public void ObservedGpuEnergyTrackerAccumulatesPerGpuAndCombinedEnergyUntilReset()
    {
        var accumulator = new GpuEnergyAccumulator();
        var tracker = new ObservedGpuEnergyTracker();
        var startedAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var first = DeviceObservation(startedAt, 100, 200);

        tracker.Observe(first, accumulator.ObserveDetailed(first));
        var initial = Assert.IsType<ObservedGpuEnergySnapshot>(tracker.Snapshot());
        Assert.Equal(0, initial.WattHours, precision: 6);
        Assert.Equal(2, initial.Devices.Count);

        var second = DeviceObservation(startedAt.AddSeconds(10), 200, 400);
        tracker.Observe(second, accumulator.ObserveDetailed(second));
        var accumulated = Assert.IsType<ObservedGpuEnergySnapshot>(tracker.Snapshot());
        Assert.Equal(150 * 10 / 3600d,
            accumulated.Devices.Single(device => device.GpuIndex == 0).WattHours, 6);
        Assert.Equal(300 * 10 / 3600d,
            accumulated.Devices.Single(device => device.GpuIndex == 1).WattHours, 6);
        Assert.Equal(accumulated.Devices.Sum(device => device.WattHours), accumulated.WattHours, 6);
    }

    [Fact]
    public void ObservedGpuEnergyTrackerIsIndependentOfSessionsAndResetsExplicitly()
    {
        var accumulator = new GpuEnergyAccumulator();
        var tracker = new ObservedGpuEnergyTracker();
        var startedAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var first = DeviceObservation(startedAt, 100, 200);
        var second = DeviceObservation(startedAt.AddSeconds(10), 100, 200);
        tracker.Observe(first, accumulator.ObserveDetailed(first));
        tracker.Observe(second, accumulator.ObserveDetailed(second));
        Assert.True(tracker.Snapshot()!.WattHours > 0);

        tracker.Reset();
        Assert.Null(tracker.Snapshot());
    }

    [Fact]
    public void ElectricityTariffSplitsAnHourlyEnergyBucketAtMinutePrecision()
    {
        Assert.True(ElectricityTariffPolicy.TryCreate(
            " gbp ", .30, .10, "00:00", "06:30", out var tariff, out var error), error);
        var hour = new DateTimeOffset(2026, 8, 24, 6, 0, 0, TimeSpan.Zero);

        var cost = ElectricityTariffPolicy.CostForUtcHour(hour, 1000, TimeZoneInfo.Utc, tariff);

        Assert.Equal("GBP", tariff.CurrencyCode);
        Assert.Equal(.20, cost, precision: 6);
        Assert.Equal(.10, ElectricityTariffPolicy.RateAt(hour, TimeZoneInfo.Utc, tariff), precision: 6);
        Assert.Equal(.30, ElectricityTariffPolicy.RateAt(hour.AddMinutes(30), TimeZoneInfo.Utc, tariff), precision: 6);
    }

    [Theory]
    [InlineData("GB", .3, .1, "00:00", "07:00")]
    [InlineData("GBP", -1, .1, "00:00", "07:00")]
    [InlineData("GBP", .3, .1, "bad", "07:00")]
    [InlineData("GBP", .3, .1, "07:00", "07:00")]
    public void ElectricityTariffRejectsInvalidSettings(
        string currency,
        double dayRate,
        double nightRate,
        string nightStart,
        string nightEnd)
        => Assert.False(ElectricityTariffPolicy.TryCreate(
            currency, dayRate, nightRate, nightStart, nightEnd, out _, out _));

    [Fact]
    public void UsageReportCalculatesHistoricalElectricityCostFromSelectedEnergy()
    {
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var tariff = new ElectricityTariff("EUR", .30, .10, new TimeOnly(0, 0), new TimeOnly(7, 0));
        var report = new UsageMetricsService().BuildReport(
            new UsageMetricsQuery(UsageMetricsRange.OneDay),
            [],
            [],
            UsageMetricDimensions.Empty,
            now,
            TimeZoneInfo.Utc,
            energyBuckets: [new GpuEnergyBucket(now.AddHours(-1), 1250, 3600, true, 1, 1, now)],
            electricityTariff: tariff);

        Assert.NotNull(report.GpuElectricityCost);
        Assert.Equal(.375, report.GpuElectricityCost!.Amount, precision: 6);
        Assert.Equal("EUR", report.GpuElectricityCost.CurrencyCode);
    }

    [Fact]
    public void ObservedGpuEnergyRecalculatesPerGpuAndTotalElectricityCost()
    {
        var accumulator = new GpuEnergyAccumulator();
        var tracker = new ObservedGpuEnergyTracker();
        var startedAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var first = DeviceObservation(startedAt, 100, 200);
        var second = DeviceObservation(startedAt.AddSeconds(10), 200, 400);
        tracker.Observe(first, accumulator.ObserveDetailed(first));
        tracker.Observe(second, accumulator.ObserveDetailed(second));
        var tariff = new ElectricityTariff("USD", .24, .24, new TimeOnly(0, 0), new TimeOnly(7, 0));

        var snapshot = Assert.IsType<ObservedGpuEnergySnapshot>(
            tracker.Snapshot(tariff, TimeZoneInfo.Utc));
        var cached = tracker.Snapshot(tariff, TimeZoneInfo.Utc);

        Assert.Equal("USD", snapshot.ElectricityCurrencyCode);
        Assert.Same(snapshot, cached);
        Assert.Equal(snapshot.KilowattHours * .24, snapshot.ElectricityCost, precision: 8);
        Assert.All(snapshot.Devices, device =>
            Assert.Equal(device.KilowattHours * .24, device.ElectricityCost, precision: 8));
    }

    [Fact]
    public async Task ElectricityTariffSettingsRoundTripThroughTheStateStore()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root) with
        {
            ElectricityCurrencyCode = "EUR",
            ElectricityDayRatePerKwh = .31,
            ElectricityNightRatePerKwh = .12,
            ElectricityNightStartLocal = "23:30",
            ElectricityNightEndLocal = "06:45"
        };

        await store.SaveAppSettingsAsync(settings);
        var reloaded = await store.GetAppSettingsAsync(root);

        Assert.Equal("EUR", reloaded.ElectricityCurrencyCode);
        Assert.Equal(.31, reloaded.ElectricityDayRatePerKwh, precision: 6);
        Assert.Equal(.12, reloaded.ElectricityNightRatePerKwh, precision: 6);
        Assert.Equal("23:30", reloaded.ElectricityNightStartLocal);
        Assert.Equal("06:45", reloaded.ElectricityNightEndLocal);
    }

    [Fact]
    public void SettingsPageAndEditorExposeValidatedElectricityTariffFields()
    {
        LocalLlmConsole.Localization.Loc.LoadLanguage("en");
        var current = AppSettings.CreateDefault(CreateTempRoot());
        var rows = new SettingsPageDefinitionService().BuildRows(current);
        Assert.Contains(rows, row => row.Key == "electricityCurrencyCode");
        Assert.Contains(rows, row => row.Key == "electricityDayRatePerKwh");
        Assert.Contains(rows, row => row.Key == "electricityNightRatePerKwh");
        Assert.Contains(rows, row => row.Key == "electricityNightStartLocal");
        Assert.Contains(rows, row => row.Key == "electricityNightEndLocal");

        var result = new AppSettingsUpdateService().Build(new AppSettingsUpdateRequest(
            current,
            current.WorkspaceRoot,
            current.ThemeMode,
            new Dictionary<string, string>
            {
                ["electricityCurrencyCode"] = "usd",
                ["electricityDayRatePerKwh"] = "0.29",
                ["electricityNightRatePerKwh"] = "0.08",
                ["electricityNightStartLocal"] = "23:00",
                ["electricityNightEndLocal"] = "06:00"
            },
            new HashSet<int>()));

        Assert.True(result.Success, result.StatusMessage);
        Assert.Equal("USD", result.Settings.ElectricityCurrencyCode);
        Assert.Equal(.29, result.Settings.ElectricityDayRatePerKwh, precision: 6);
        Assert.Equal(.08, result.Settings.ElectricityNightRatePerKwh, precision: 6);
    }

    private static GpuPowerObservation Observation(
        DateTimeOffset capturedAt,
        double watts,
        string sensorKey = "GPU 0")
        => new(capturedAt, watts, [sensorKey], 1);

    private static GpuPowerObservation DeviceObservation(DateTimeOffset capturedAt, double gpu0, double gpu1)
        => new(capturedAt, gpu0 + gpu1, ["GPU 0: NVIDIA", "GPU 1: AMD"], 2)
        {
            Sensors =
            [
                new GpuPowerSensorReading("GPU 0: NVIDIA", 0, "NVIDIA", gpu0),
                new GpuPowerSensorReading("GPU 1: AMD", 1, "AMD", gpu1)
            ]
        };

}
