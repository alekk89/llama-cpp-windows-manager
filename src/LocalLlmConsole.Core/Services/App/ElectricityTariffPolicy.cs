namespace LocalLlmConsole.Services;

public static class ElectricityTariffPolicy
{
    public const double MaximumRatePerKwh = 1000;
    private const int MaximumCachedHourlyRates = 4096;
    private const string DefaultCurrencyCode = "GBP";
    private static readonly TimeOnly DefaultNightStart = new(0, 0);
    private static readonly TimeOnly DefaultNightEnd = new(7, 0);
    private static readonly object HourlyRateCacheGate = new();
    private static readonly Dictionary<HourlyRateCacheKey, double> HourlyRateCache = [];
    private static readonly Queue<HourlyRateCacheKey> HourlyRateOrder = [];

    public static ElectricityTariff FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return TryCreate(
            settings.ElectricityCurrencyCode,
            settings.ElectricityDayRatePerKwh,
            settings.ElectricityNightRatePerKwh,
            settings.ElectricityNightStartLocal,
            settings.ElectricityNightEndLocal,
            out var tariff,
            out _)
            ? tariff
            : new ElectricityTariff(DefaultCurrencyCode, 0, 0, DefaultNightStart, DefaultNightEnd);
    }

    public static bool TryCreate(
        string? currencyCode,
        double dayRatePerKwh,
        double nightRatePerKwh,
        string? nightStartLocal,
        string? nightEndLocal,
        out ElectricityTariff tariff,
        out string error)
    {
        var currency = (currencyCode ?? "").Trim().ToUpperInvariant();
        if (currency.Length != 3 || currency.Any(character => character is < 'A' or > 'Z'))
            return Fail("Electricity currency must be a three-letter code such as GBP, EUR, or USD.", out tariff, out error);
        if (!ValidRate(dayRatePerKwh) || !ValidRate(nightRatePerKwh))
            return Fail($"Electricity rates must be between 0 and {MaximumRatePerKwh:N0} currency units per kWh.", out tariff, out error);
        if (!TryTime(nightStartLocal, out var start) || !TryTime(nightEndLocal, out var end))
            return Fail("Electricity night start and end must use 24-hour HH:mm format.", out tariff, out error);
        if (start == end)
            return Fail("Electricity night start and end must be different.", out tariff, out error);

        tariff = new ElectricityTariff(currency, dayRatePerKwh, nightRatePerKwh, start, end);
        error = "";
        return true;
    }

    public static string TimeText(TimeOnly value)
        => value.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static double RateAt(DateTimeOffset utcTime, TimeZoneInfo timeZone, ElectricityTariff tariff)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(tariff);
        var localTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcTime, timeZone).DateTime);
        var night = tariff.NightStartLocal < tariff.NightEndLocal
            ? localTime >= tariff.NightStartLocal && localTime < tariff.NightEndLocal
            : localTime >= tariff.NightStartLocal || localTime < tariff.NightEndLocal;
        return night ? tariff.NightRatePerKwh : tariff.DayRatePerKwh;
    }

    public static double CostForUtcHour(
        DateTimeOffset bucketStartUtc,
        double wattHours,
        TimeZoneInfo timeZone,
        ElectricityTariff tariff)
    {
        if (!double.IsFinite(wattHours) || wattHours <= 0) return 0;
        var utc = bucketStartUtc.ToUniversalTime();
        var cacheKey = new HourlyRateCacheKey(utc.Ticks, timeZone, tariff);
        double averageRate;
        lock (HourlyRateCacheGate)
        {
            if (HourlyRateCache.TryGetValue(cacheKey, out averageRate))
                return wattHours / 1000 * averageRate;
        }

        var rateTotal = 0d;
        for (var minute = 0; minute < 60; minute++)
            rateTotal += RateAt(utc.AddMinutes(minute + .5), timeZone, tariff);
        averageRate = rateTotal / 60;
        lock (HourlyRateCacheGate)
        {
            if (!HourlyRateCache.ContainsKey(cacheKey))
            {
                HourlyRateCache[cacheKey] = averageRate;
                HourlyRateOrder.Enqueue(cacheKey);
                while (HourlyRateOrder.Count > MaximumCachedHourlyRates)
                    HourlyRateCache.Remove(HourlyRateOrder.Dequeue());
            }
        }
        return wattHours / 1000 * averageRate;
    }

    public static ElectricityCostTotals Cost(
        IEnumerable<GpuEnergyBucket> buckets,
        TimeZoneInfo timeZone,
        ElectricityTariff tariff)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        var amount = buckets.Sum(bucket => CostForUtcHour(
            bucket.BucketStartUtc, bucket.WattHours, timeZone, tariff));
        return new ElectricityCostTotals(
            amount,
            tariff.CurrencyCode,
            tariff.DayRatePerKwh,
            tariff.NightRatePerKwh);
    }

    private static bool ValidRate(double value)
        => double.IsFinite(value) && value is >= 0 and <= MaximumRatePerKwh;

    private static bool TryTime(string? value, out TimeOnly result)
        => TimeOnly.TryParseExact(
            value?.Trim(),
            ["H:mm", "HH:mm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);

    private static bool Fail(
        string message,
        out ElectricityTariff tariff,
        out string error)
    {
        tariff = null!;
        error = message;
        return false;
    }

    private readonly record struct HourlyRateCacheKey(
        long BucketStartUtcTicks,
        TimeZoneInfo TimeZone,
        ElectricityTariff Tariff);
}
