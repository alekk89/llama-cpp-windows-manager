namespace LocalLlmConsole.Services;

public static class GpuStatusVendorPowerFormatter
{
    public static IReadOnlyList<string> FormatIntelXpuSmi(string output)
    {
        var lines = (output ?? "").Split(
            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            var identity = Regex.Match(lines[index],
                @"^\|\s*(\d+)\s+(Intel.+?)\s{2,}\|",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!identity.Success) continue;
            var metrics = index + 1 < lines.Length ? lines[index + 1] : "";
            var observations = new List<string>();
            AddNumber(observations, metrics, @"(?<!\d)(\d+(?:\.\d+)?)\s*%", "% load", 0, 100);
            AddNumber(observations, metrics, @"(?<!\d)(\d+(?:\.\d+)?)\s*C(?:\s|\||$)", " °C", -20, 125);
            AddMemory(observations, metrics);
            AddNumber(observations, metrics, @"(?<!\d)(\d+(?:\.\d+)?)\s*W\s*/", " W", 0, 2000);
            AddNumber(observations, metrics, @"/\s*(\d+(?:\.\d+)?)\s*W", " W limit", 0, 2000);
            var name = Regex.Replace(identity.Groups[2].Value.Trim(), @"\s+(?:On|Off)$", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var prefix = $"GPU {identity.Groups[1].Value}: {name}";
            result.Add(observations.Count == 0 ? prefix : $"{prefix} | {string.Join(" | ", observations)}");
        }
        return result;
    }

    public static IReadOnlyList<string> FormatAmdSmi(string output)
    {
        var result = new List<string>();
        foreach (var line in (output ?? "").Split(
                     ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var row = Regex.Match(line,
                @"^\|?\s*(\d+)\s+\d+\s+.*?\s+(-?\d+(?:\.\d+)?)\s*°?C\s+(\d+(?:\.\d+)?)\s*W\b(.*)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!row.Success) continue;
            var observations = new List<string>
            {
                $"{row.Groups[2].Value} °C",
                $"{row.Groups[3].Value} W"
            };
            var tail = row.Groups[4].Value;
            var clocks = Regex.Matches(tail, @"(\d+(?:\.\d+)?)\s*Mhz", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (clocks.Count > 0) observations.Add($"{clocks[0].Groups[1].Value} MHz core");
            if (clocks.Count > 1) observations.Add($"{clocks[1].Groups[1].Value} MHz memory");
            var percentages = Regex.Matches(tail, @"(\d+(?:\.\d+)?)\s*%", RegexOptions.CultureInvariant);
            if (percentages.Count > 0) observations.Insert(0, $"{percentages[^1].Groups[1].Value}% load");
            if (percentages.Count > 1) observations.Add($"{percentages[^2].Groups[1].Value}% memory");
            if (percentages.Count > 2) observations.Add($"{percentages[0].Groups[1].Value}% fan");
            var powerLimit = Regex.Match(tail, @"(?<![\d.])(\d+(?:\.\d+)?)\s*W", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (powerLimit.Success) observations.Add($"{powerLimit.Groups[1].Value} W limit");
            result.Add($"GPU {row.Groups[1].Value}: AMD GPU | {string.Join(" | ", observations)}");
        }
        return result;
    }

    private static void AddMemory(List<string> observations, string value)
    {
        var memory = Regex.Match(value, @"(\d+(?:\.\d+)?)\s*MiB\s*/\s*(\d+(?:\.\d+)?)\s*MiB",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!memory.Success
            || !double.TryParse(memory.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var used)
            || !double.TryParse(memory.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var total)
            || total <= 0)
            return;
        observations.Add($"{used / 1024:0.0}/{total / 1024:0.0} GiB VRAM");
    }

    private static void AddNumber(
        List<string> observations,
        string value,
        string pattern,
        string suffix,
        double minimum,
        double maximum)
    {
        var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success
            || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number)
            || number < minimum
            || number > maximum)
            return;
        observations.Add($"{number:0.#}{suffix}");
    }
}
