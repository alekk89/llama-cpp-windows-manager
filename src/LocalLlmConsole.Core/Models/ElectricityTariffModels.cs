namespace LocalLlmConsole.Models;

public sealed record ElectricityTariff(
    string CurrencyCode,
    double DayRatePerKwh,
    double NightRatePerKwh,
    TimeOnly NightStartLocal,
    TimeOnly NightEndLocal);

public sealed record ElectricityCostTotals(
    double Amount,
    string CurrencyCode,
    double DayRatePerKwh,
    double NightRatePerKwh)
{
    public static ElectricityCostTotals Empty(ElectricityTariff tariff)
        => new(0, tariff.CurrencyCode, tariff.DayRatePerKwh, tariff.NightRatePerKwh);
}
