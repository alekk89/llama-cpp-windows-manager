namespace LocalLlmConsole.Services;

public static class HostHardwareSnapshotParser
{
    public static HostHardwareSnapshot Parse(string? summary, DateTimeOffset? capturedAt = null)
    {
        var normalized = string.IsNullOrWhiteSpace(summary) ? "Unavailable" : summary.Trim();
        var lines = normalized.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cpuLine = lines.FirstOrDefault(line => line.StartsWith("CPU:", StringComparison.OrdinalIgnoreCase));
        var telemetry = lines.Where(line => line.StartsWith("Telemetry:", StringComparison.OrdinalIgnoreCase))
            .Select(StripPrefix)
            .ToArray();
        var cpu = ParseCpu(cpuLine, telemetry);
        var memory = ParseMemory(lines.FirstOrDefault(line => line.StartsWith("RAM:", StringComparison.OrdinalIgnoreCase)));
        var process = ParseProcess(lines.FirstOrDefault(line => line.StartsWith("Process:", StringComparison.OrdinalIgnoreCase)));
        var gpus = lines.Select(ParseGpu).Where(gpu => gpu is not null).Select(gpu => gpu!).ToArray();
        return new HostHardwareSnapshot(
            normalized,
            cpu,
            memory,
            process,
            gpus,
            (capturedAt ?? DateTimeOffset.UtcNow).ToUniversalTime());
    }

    private static HostCpuSnapshot? ParseCpu(string? cpuLine, IReadOnlyList<string> telemetry)
    {
        if (string.IsNullOrWhiteSpace(cpuLine) && telemetry.Count == 0) return null;
        var raw = string.Join(" | ", telemetry);
        var topology = Regex.Match(raw, @"(\d+)\s*C\s*/\s*(\d+)\s*T",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new HostCpuSnapshot(
            StripPrefix(cpuLine ?? ""),
            Percentage(raw),
            Number(raw, @"(\d+(?:\.\d+)?)\s*°\s*C(?:\s*(?:\||$))"),
            Number(raw, @"(\d+(?:\.\d+)?)\s*MHz(?:\s+core)?"),
            topology.Success
                ? int.Parse(topology.Groups[1].Value, CultureInfo.InvariantCulture)
                : Integer(raw, @"(\d+)\s+cores?"),
            topology.Success
                ? int.Parse(topology.Groups[2].Value, CultureInfo.InvariantCulture)
                : Integer(raw, @"(\d+)\s+threads?"));
    }

    private static HostMemorySnapshot? ParseMemory(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var raw = StripPrefix(line);
        var capacity = MemoryPair(raw);
        return new HostMemorySnapshot(
            Percentage(raw),
            capacity?.Used,
            capacity?.Total,
            Number(raw, @"(\d+(?:\.\d+)?)\s*MHz"));
    }

    private static HostProcessSnapshot? ParseProcess(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var raw = StripPrefix(line);
        return new HostProcessSnapshot(
            Number(raw, @"(\d+(?:\.\d+)?)\s*%\s*CPU"),
            Number(raw, @"(\d+(?:\.\d+)?)\s*GiB\s*(?:private\s*)?RAM"));
    }

    private static HostGpuSnapshot? ParseGpu(string line)
    {
        var match = Regex.Match(line, @"^GPU\s+(\d+)\s*:\s*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return null;

        var raw = match.Groups[2].Value.Trim();
        var name = raw.Split('|', StringSplitOptions.TrimEntries)[0];
        var capacity = MemoryPair(raw);
        var throttle = Regex.Match(raw, @"throttle\s+(active|none)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new HostGpuSnapshot(
            index,
            name,
            Percentage(raw),
            capacity?.Used,
            capacity?.Total,
            Number(raw, @"(\d+(?:\.\d+)?)\s*W(?=\s*(?:/|\||$))"),
            Number(raw, @"(\d+(?:\.\d+)?)\s*MHz(?:\s+core)?"),
            Number(raw, @"(\d+(?:\.\d+)?)\s*°\s*C"),
            Number(raw, @"(\d+(?:\.\d+)?)\s*°\s*C\s+(?:memory|VRAM)"),
            Number(raw, @"(\d+(?:\.\d+)?)\s*MHz\s+memory"),
            Number(raw, @"(\d+(?:\.\d+)?)\s*%\s+memory"),
            Number(raw, @"(\d+(?:\.\d+)?)\s*%\s+fan"),
            Number(raw, @"(\d+(?:\.\d+)?)\s*W\s+limit"),
            throttle.Success
                ? throttle.Groups[1].Value.Equals("active", StringComparison.OrdinalIgnoreCase)
                : null);
    }

    private static string StripPrefix(string value)
    {
        var separator = value.IndexOf(':');
        return separator >= 0 ? value[(separator + 1)..].Trim() : value.Trim();
    }

    private static double? Percentage(string value)
    {
        var parsed = Number(value, @"(?<![\d.])(\d+(?:\.\d+)?)\s*%");
        return parsed is null ? null : Math.Clamp(parsed.Value, 0, 100);
    }

    private static double? Number(string value, string pattern)
    {
        var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
               && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
               && double.IsFinite(result)
            ? result
            : null;
    }

    private static int? Integer(string value, string pattern)
    {
        var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
               && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static (double Used, double Total)? MemoryPair(string value)
    {
        var match = Regex.Match(value, @"(\d+(?:\.\d+)?)/(\d+(?:\.\d+)?)\s*GiB",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
               && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var used)
               && double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var total)
            ? (used, total)
            : null;
    }
}
