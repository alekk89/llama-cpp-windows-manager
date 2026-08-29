namespace LocalLlmConsole.Services;

public sealed class LifetimeMetricsApplicationService
{
    private static readonly TimeSpan DefaultGpuEnergySampleInterval = TimeSpan.FromSeconds(10);
    private readonly StateStore _stateStore;
    private readonly UsageMetricsService _usageMetrics;
    private readonly GpuEnergyAccumulator _gpuEnergy = new();
    private readonly ObservedGpuEnergyTracker _observedGpuEnergy = new();
    private readonly object _gpuEnergyGate = new();
    private DateTimeOffset _lastGpuEnergySampleAt = DateTimeOffset.MinValue;
    private bool _gpuEnergyPersistenceActive = true;
    private long _dataVersion;

    public LifetimeMetricsApplicationService(StateStore stateStore, UsageMetricsService? usageMetrics = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _usageMetrics = usageMetrics ?? new UsageMetricsService();
    }

    public Task<IReadOnlyList<TokenUsageRecord>> ListAsync()
        => _stateStore.ListTokenUsageAsync();

    public long DataVersion => Interlocked.Read(ref _dataVersion);

    public async Task AddUsageAsync(TokenUsageDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        if (!delta.HasActivity) return;
        await _stateStore.RecordTokenUsageAsync(delta);
        Interlocked.Increment(ref _dataVersion);
    }

    public bool ReserveGpuEnergySample(DateTimeOffset capturedAt)
        => ReserveGpuEnergySample(capturedAt, DefaultGpuEnergySampleInterval);

    public bool ReserveGpuEnergySample(DateTimeOffset capturedAt, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero) interval = DefaultGpuEnergySampleInterval;
        lock (_gpuEnergyGate)
        {
            if (_lastGpuEnergySampleAt != DateTimeOffset.MinValue
                && capturedAt - _lastGpuEnergySampleAt < interval)
                return false;
            _lastGpuEnergySampleAt = capturedAt;
            return true;
        }
    }

    public void SetGpuEnergyPersistenceActive(bool active)
    {
        lock (_gpuEnergyGate)
        {
            if (_gpuEnergyPersistenceActive == active) return;
            _gpuEnergyPersistenceActive = active;
            _gpuEnergy.Reset();
        }
    }

    public async Task<GpuPowerObservation> ObserveGpuPowerAsync(
        string hardwareSummary,
        DateTimeOffset capturedAt,
        bool persistHistory = true)
        => await ObserveGpuPowerAsync(
            HostHardwareSnapshotParser.Parse(hardwareSummary, capturedAt),
            capturedAt,
            persistHistory);

    public async Task<GpuPowerObservation> ObserveGpuPowerAsync(
        HostHardwareSnapshot hardwareSnapshot,
        DateTimeOffset capturedAt,
        bool persistHistory = true)
    {
        var observation = GpuPowerObservationParser.Parse(hardwareSnapshot, capturedAt);
        GpuEnergySampleResult deltas;
        lock (_gpuEnergyGate)
        {
            if (_gpuEnergyPersistenceActive != persistHistory)
            {
                _gpuEnergyPersistenceActive = persistHistory;
                _gpuEnergy.Reset();
            }
            deltas = _gpuEnergy.ObserveDetailed(observation);
            _observedGpuEnergy.Observe(observation, deltas);
        }
        if (persistHistory)
            await _stateStore.RecordGpuEnergyAsync(deltas.TotalDeltas, deltas.DeviceDeltas);
        if (persistHistory && (deltas.TotalDeltas.Count > 0 || deltas.DeviceDeltas.Count > 0))
            Interlocked.Increment(ref _dataVersion);
        return observation;
    }

    public ObservedGpuEnergySnapshot? ObservedGpuEnergySnapshot(
        ElectricityTariff? tariff = null,
        TimeZoneInfo? timeZone = null)
    {
        lock (_gpuEnergyGate)
            return _observedGpuEnergy.Snapshot(tariff, timeZone);
    }

    public async Task<UsageMetricsReport> GetReportAsync(
        UsageMetricsQuery query,
        TimeZoneInfo? timeZone = null,
        DateTimeOffset? now = null,
        ElectricityTariff? electricityTariff = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        timeZone ??= TimeZoneInfo.Local;
        var capturedAt = now ?? DateTimeOffset.UtcNow;
        var reportWindow = _usageMetrics.ResolveWindow(query, capturedAt, timeZone);
        var calendarWindow = _usageMetrics.ResolveCalendarWindow(capturedAt, timeZone);
        var fromUtc = reportWindow.FromUtc is null
            ? null
            : reportWindow.FromUtc < calendarWindow.FromUtc
                ? reportWindow.FromUtc
                : calendarWindow.FromUtc;
        var toUtc = reportWindow.ToUtc > calendarWindow.ToUtc
            ? reportWindow.ToUtc
            : calendarWindow.ToUtc;
        var buckets = await _stateStore.ListTokenUsageBucketsAsync(fromUtc, toUtc);
        var energyBuckets = await _stateStore.ListGpuEnergyBucketsAsync(fromUtc, toUtc);
        var energyDeviceBuckets = await _stateStore.ListGpuEnergyDeviceBucketsAsync(fromUtc, toUtc);
        var lifetime = await _stateStore.ListTokenUsageAsync();
        var dimensions = await _stateStore.ListTokenUsageDimensionsAsync();
        var trackingStartedAt = await _stateStore.GetTokenUsageTrackingStartedAtAsync();
        var energyTrackingStartedAt = await _stateStore.GetGpuEnergyTrackingStartedAtAsync();
        return _usageMetrics.BuildReport(
            query,
            buckets,
            lifetime,
            dimensions,
            capturedAt,
            timeZone,
            trackingStartedAt,
            energyBuckets,
            energyTrackingStartedAt,
            energyDeviceBuckets,
            electricityTariff);
    }

    public async Task DeleteModelUsageAsync(string modelId)
    {
        await _stateStore.DeleteTokenUsageAsync(modelId);
        Interlocked.Increment(ref _dataVersion);
    }

    public async Task DeleteAllUsageAsync()
    {
        await _stateStore.DeleteAllTokenUsageAsync();
        await _stateStore.DeleteAllGpuEnergyAsync();
        lock (_gpuEnergyGate)
        {
            _gpuEnergy.Reset();
            _observedGpuEnergy.Reset();
            _lastGpuEnergySampleAt = DateTimeOffset.MinValue;
        }
        Interlocked.Increment(ref _dataVersion);
    }
}
