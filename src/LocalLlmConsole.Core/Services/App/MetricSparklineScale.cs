namespace LocalLlmConsole.Services;

/// <summary>Vertical value range of a sparkline plot.</summary>
public readonly record struct MetricSparklineRange(double Minimum, double Maximum)
{
    public double Fraction(double value)
        => Maximum > Minimum ? Math.Clamp((value - Minimum) / (Maximum - Minimum), 0, 1) : 0;
}

/// <summary>Minimum, maximum, and latest values of the samples a sparkline shows.</summary>
public sealed record MetricSparklineSummary(double Minimum, double Maximum, double Latest);

/// <summary>
/// Platform-neutral scale and readout rules for overview sparklines. A stable
/// scale starts at zero and rounds its maximum up to a short 1-1.2-1.5-2-2.5-3-4-5-6-8 step. It
/// grows immediately when a sample exceeds it but shrinks only when every
/// visible sample falls below <see cref="ShrinkThreshold"/> of it, so a small
/// change such as 60 to 55 t/s stays a small change on screen.
/// </summary>
public static class MetricSparklineScale
{
    public const double Headroom = 1.08;
    public const double ShrinkThreshold = .4;
    private static readonly double[] NiceSteps = [1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10];

    public static double NiceCeiling(double value)
    {
        if (!double.IsFinite(value) || value <= 0) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in NiceSteps)
        {
            var candidate = step * magnitude;
            if (candidate >= value * (1 - 1e-12)) return candidate;
        }
        return 10 * magnitude;
    }

    public static double StableMaximum(double observedMaximum, double? previousMaximum)
    {
        var required = Math.Max(0, observedMaximum) * Headroom;
        var target = required > 0 ? NiceCeiling(required) : previousMaximum ?? 1;
        if (previousMaximum is not { } previous || previous <= 0 || target >= previous)
            return target;
        return required < previous * ShrinkThreshold ? target : previous;
    }

    public static MetricSparklineRange FitRange(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum)
            return new(0, 1);
        var span = maximum - minimum;
        var padding = span > Math.Abs(maximum) * 1e-6
            ? span * .1
            : Math.Max(Math.Abs(maximum) * .05, 1e-6);
        return new(Math.Max(0, minimum - padding), maximum + padding);
    }

    public static MetricSparklineSummary? Summarize(IEnumerable<double?> values)
    {
        double? minimum = null, maximum = null, latest = null;
        foreach (var value in values)
        {
            if (value is not { } number || !double.IsFinite(number)) continue;
            minimum = minimum is { } min ? Math.Min(min, number) : number;
            maximum = maximum is { } max ? Math.Max(max, number) : number;
            latest = number;
        }
        return latest is { } last ? new(minimum!.Value, maximum!.Value, last) : null;
    }

    /// <summary>Formats a readout with precision that follows the value's magnitude.</summary>
    public static string FormatValue(double value, IFormatProvider? provider = null)
    {
        var magnitude = Math.Abs(value);
        var format = magnitude >= 1000 ? "N0"
            : magnitude >= 100 ? "0"
            : magnitude >= 10 ? "0.#"
            : magnitude >= 1 ? "0.##"
            : "0.###";
        return value.ToString(format, provider ?? CultureInfo.CurrentCulture);
    }
}
