namespace LocalLlmConsole.Services;

public sealed record AcceleratorProbeDevice(
    int Index,
    string Name,
    string NameKey,
    string Vendor,
    string DisplayLine);

public sealed record AcceleratorProbeSummary(
    string DisplayText,
    IReadOnlyList<AcceleratorProbeDevice> Devices)
{
    public bool IsAvailable => Devices.Count > 0;

    public static AcceleratorProbeSummary Parse(string? summary)
    {
        var displayText = string.IsNullOrWhiteSpace(summary) ? "Unavailable" : summary;
        var devices = displayText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseDevice)
            .Where(device => device is not null)
            .Cast<AcceleratorProbeDevice>()
            .ToArray();
        return new AcceleratorProbeSummary(displayText, devices);
    }

    private static AcceleratorProbeDevice? ParseDevice(string line)
    {
        var match = Regex.Match(
            line,
            @"^GPU\s+(\d+)\s*:\s*([^|]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return null;

        var name = match.Groups[2].Value.Trim();
        var nameKey = Regex.Replace(name, @"[\s®™]+", " ").Trim().ToUpperInvariant();
        return new AcceleratorProbeDevice(index, name, nameKey, Vendor(nameKey), line);
    }

    private static string Vendor(string name)
        => name.Contains("NVIDIA", StringComparison.Ordinal) ? "NVIDIA"
            : name.Contains("AMD", StringComparison.Ordinal) || name.Contains("RADEON", StringComparison.Ordinal) ? "AMD"
            : name.Contains("INTEL", StringComparison.Ordinal) || name.Contains("ARC", StringComparison.Ordinal) ? "INTEL"
            : "";
}
