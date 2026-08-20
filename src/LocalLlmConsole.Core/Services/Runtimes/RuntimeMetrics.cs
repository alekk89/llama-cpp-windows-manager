
namespace LocalLlmConsole.Services;

public sealed record PrometheusSample(string Name, string Labels, double Value, string RawValue, string Type, string Help);

public static class RuntimeMetrics
{
    public static IReadOnlyList<PrometheusSample> ParsePrometheus(string raw)
    {
        var help = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var samples = new List<PrometheusSample>();

        foreach (var rawLine in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("# HELP ", StringComparison.Ordinal))
            {
                var payload = line["# HELP ".Length..].Trim();
                var split = payload.IndexOf(' ');
                if (split > 0) help[payload[..split]] = payload[(split + 1)..].Trim();
                continue;
            }

            if (line.StartsWith("# TYPE ", StringComparison.Ordinal))
            {
                var payload = line["# TYPE ".Length..].Trim();
                var split = payload.IndexOf(' ');
                if (split > 0) types[payload[..split]] = payload[(split + 1)..].Trim();
                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal)) continue;

            var valueSplit = LastWhitespace(line);
            if (valueSplit <= 0 || valueSplit >= line.Length - 1) continue;

            var nameAndLabels = line[..valueSplit].Trim();
            var rawValue = line[(valueSplit + 1)..].Trim();
            var name = nameAndLabels;
            var labels = "";
            var labelStart = nameAndLabels.IndexOf('{');
            if (labelStart > 0)
            {
                name = nameAndLabels[..labelStart];
                labels = nameAndLabels[labelStart..].Trim();
            }

            samples.Add(new PrometheusSample(
                name,
                labels,
                ParsePrometheusDouble(rawValue),
                rawValue,
                types.TryGetValue(name, out var type) ? type : "",
                help.TryGetValue(name, out var helpText) ? helpText : ""));
        }

        return samples;
    }

    public static double? Sum(IReadOnlyList<PrometheusSample> samples, string[] include, string[] exclude)
    {
        var values = Matching(samples, include, exclude).Select(sample => sample.Value).Where(IsFinite).ToArray();
        return values.Length == 0 ? null : values.Sum();
    }

    public static double? First(IReadOnlyList<PrometheusSample> samples, string[] include, string[] exclude)
    {
        foreach (var sample in Matching(samples, include, exclude))
        {
            if (IsFinite(sample.Value)) return sample.Value;
        }
        return null;
    }

    public static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    private static IEnumerable<PrometheusSample> Matching(IReadOnlyList<PrometheusSample> samples, string[] include, string[] exclude)
    {
        foreach (var sample in samples)
        {
            var name = NormalizeMetricName(sample.Name);
            if (include.Any(term => !name.Contains(NormalizeMetricName(term), StringComparison.OrdinalIgnoreCase))) continue;
            if (exclude.Any(term => name.Contains(NormalizeMetricName(term), StringComparison.OrdinalIgnoreCase))) continue;
            yield return sample;
        }
    }

    private static string NormalizeMetricName(string value)
        => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "_");

    private static int LastWhitespace(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(text[i])) return i;
        }
        return -1;
    }

    private static double ParsePrometheusDouble(string rawValue)
    {
        if (string.Equals(rawValue, "+Inf", StringComparison.OrdinalIgnoreCase) || string.Equals(rawValue, "Inf", StringComparison.OrdinalIgnoreCase))
            return double.PositiveInfinity;
        if (string.Equals(rawValue, "-Inf", StringComparison.OrdinalIgnoreCase))
            return double.NegativeInfinity;
        if (string.Equals(rawValue, "NaN", StringComparison.OrdinalIgnoreCase))
            return double.NaN;
        return double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN;
    }
}

