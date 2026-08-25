namespace LocalLlmConsole.Services;

/// <summary>
/// Accumulates the host GPU energy observed during the current Manager process.
/// Historical persistence remains the responsibility of the caller.
/// </summary>
public sealed class ObservedGpuEnergyTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ObservedGpuEnergyDevice> _devices =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<DateTimeOffset, double>> _deviceWattHoursByUtcHour =
        new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _startedAt;
    private DateTimeOffset _capturedAt;
    private long _version;
    private long _cachedVersion = -1;
    private ElectricityTariff? _cachedTariff;
    private string _cachedTimeZoneId = "";
    private ObservedGpuEnergySnapshot? _cachedSnapshot;

    public void Observe(GpuPowerObservation observation, GpuEnergySampleResult sample)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(sample);
        lock (_gate)
        {
            if (observation.Sensors.Count == 0) return;

            _startedAt ??= observation.CapturedAt;
            _capturedAt = observation.CapturedAt;
            foreach (var sensor in observation.Sensors)
            {
                var key = NormalizeKey(sensor.Key);
                if (!_devices.ContainsKey(key))
                    _devices[key] = new ObservedGpuEnergyDevice(
                        sensor.Key, sensor.GpuIndex, sensor.GpuName, 0);
                if (!_deviceWattHoursByUtcHour.ContainsKey(key))
                    _deviceWattHoursByUtcHour[key] = [];
            }

            foreach (var group in sample.DeviceDeltas.GroupBy(delta => NormalizeKey(delta.SensorKey)))
            {
                var latest = group.Last();
                var prior = _devices.GetValueOrDefault(group.Key)
                            ?? new ObservedGpuEnergyDevice(
                                latest.SensorKey, latest.GpuIndex, latest.GpuName, 0);
                _devices[group.Key] = prior with
                {
                    GpuIndex = latest.GpuIndex,
                    GpuName = latest.GpuName,
                    WattHours = prior.WattHours + group.Sum(delta => Math.Max(0, delta.WattHours))
                };
                var hourly = _deviceWattHoursByUtcHour.GetValueOrDefault(group.Key);
                if (hourly is null)
                {
                    hourly = [];
                    _deviceWattHoursByUtcHour[group.Key] = hourly;
                }
                foreach (var delta in group)
                {
                    var hour = delta.BucketStartUtc.ToUniversalTime();
                    hourly[hour] = hourly.GetValueOrDefault(hour) + Math.Max(0, delta.WattHours);
                }
            }
            _version++;
        }
    }

    public ObservedGpuEnergySnapshot? Snapshot(
        ElectricityTariff? tariff = null,
        TimeZoneInfo? timeZone = null)
    {
        lock (_gate)
        {
            if (_startedAt is null || _devices.Count == 0) return null;
            timeZone ??= TimeZoneInfo.Local;
            if (_cachedVersion == _version
                && Equals(_cachedTariff, tariff)
                && string.Equals(_cachedTimeZoneId, timeZone.Id, StringComparison.Ordinal))
                return _cachedSnapshot;

            _cachedSnapshot = new ObservedGpuEnergySnapshot(
                _startedAt.Value,
                _capturedAt,
                _devices.Values
                    .OrderBy(device => device.GpuIndex)
                    .ThenBy(device => device.SensorKey, StringComparer.OrdinalIgnoreCase)
                    .Select(device => device with
                    {
                        ElectricityCost = tariff is null
                            ? 0
                            : _deviceWattHoursByUtcHour
                                .GetValueOrDefault(NormalizeKey(device.SensorKey), [])
                                .Sum(bucket => ElectricityTariffPolicy.CostForUtcHour(
                                    bucket.Key, bucket.Value, timeZone, tariff))
                    })
                    .ToArray(),
                tariff?.CurrencyCode ?? "");
            _cachedVersion = _version;
            _cachedTariff = tariff;
            _cachedTimeZoneId = timeZone.Id;
            return _cachedSnapshot;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _devices.Clear();
            _deviceWattHoursByUtcHour.Clear();
            _startedAt = null;
            _capturedAt = default;
            _version++;
            _cachedVersion = -1;
            _cachedTariff = null;
            _cachedTimeZoneId = "";
            _cachedSnapshot = null;
        }
    }

    private static string NormalizeKey(string value)
        => (value ?? "").Trim().ToUpperInvariant();
}
