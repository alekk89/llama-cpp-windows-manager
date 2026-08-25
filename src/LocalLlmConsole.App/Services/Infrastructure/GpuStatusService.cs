
namespace LocalLlmConsole.Services;

public static class GpuStatusService
{
    private const double BytesPerGiB = 1024.0 * 1024 * 1024;

    public static string FormatNvidiaSmiCsvLine(string line)
    {
        var parts = line.Split(',').Select(part => part.Trim()).ToArray();
        if (parts.Length < 6) return "";
        var index = parts[0];
        var name = parts[1];
        var identity = string.IsNullOrWhiteSpace(name) ? $"GPU {index}" : $"GPU {index}: {name}";
        var observations = new List<string>();
        if (CsvNumber(parts[2]) is { } utilization)
            observations.Add($"{Math.Clamp(utilization, 0, 100):0.#}% load");
        if (CsvNumber(parts[3]) is { } temperature)
            observations.Add($"{temperature:0.#} °C");
        if (CsvNumber(parts[4]) is { } usedMb && CsvNumber(parts[5]) is { } totalMb && totalMb > 0)
            observations.Add($"{usedMb / 1024:0.0}/{totalMb / 1024:0.0} GiB VRAM");
        if (parts.Length > 6 && CsvNumber(parts[6]) is { } power)
            observations.Add($"{power:0.#} W");
        if (parts.Length > 7 && CsvNumber(parts[7]) is { } clock)
            observations.Add($"{clock:0} MHz core");
        return NormalizeMetricSeparators(observations.Count == 0
            ? identity
            : $"{identity} | {string.Join(" | ", observations)}");
    }

    public static string FormatNvidiaPowerCsvLine(string line)
    {
        var parts = line.Split(',').Select(part => part.Trim()).ToArray();
        if (parts.Length < 2) return "";
        var identity = string.IsNullOrWhiteSpace(parts[1])
            ? $"GPU {parts[0]}"
            : $"GPU {parts[0]}: {parts[1]}";
        return parts.Length > 2 && CsvNumber(parts[2]) is { } power && power is >= 0 and <= 2000
            ? $"{identity} | {power:0.#} W"
            : identity;
    }

    public static string MergeNvidiaExtendedSummary(IReadOnlyList<string> baseLines, string extendedCsv)
    {
        var extensions = new Dictionary<int, string>();
        foreach (var line in (extendedCsv ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',').Select(part => part.Trim()).ToArray();
            if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;
            var observations = new List<string>();
            if (CsvNumber(parts[1]) is { } memoryClock && memoryClock is > 0 and <= 10000)
                observations.Add($"{memoryClock:0} MHz memory");
            if (parts.Length > 2 && CsvNumber(parts[2]) is { } memoryActivity && memoryActivity is >= 0 and <= 100)
                observations.Add($"{memoryActivity:0.#}% memory");
            if (parts.Length > 3 && CsvNumber(parts[3]) is { } fan && fan is >= 0 and <= 100)
                observations.Add($"{fan:0.#}% fan");
            if (parts.Length > 4 && CsvNumber(parts[4]) is { } powerLimit && powerLimit is > 0 and <= 2000)
                observations.Add($"{powerLimit:0.#} W limit");
            if (parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5])
                && !parts[5].Contains("N/A", StringComparison.OrdinalIgnoreCase))
            {
                var normalized = parts[5].Trim();
                var active = !normalized.Equals("0", StringComparison.OrdinalIgnoreCase)
                             && !normalized.Equals("0x0000000000000000", StringComparison.OrdinalIgnoreCase);
                observations.Add(active ? "throttle active" : "throttle none");
            }
            if (parts.Length > 6 && CsvNumber(parts[6]) is { } memoryTemperature
                && memoryTemperature is >= -20 and <= 125)
                observations.Add($"{memoryTemperature:0.#} °C memory");
            if (observations.Count > 0) extensions[index] = string.Join(" | ", observations);
        }

        return string.Join(Environment.NewLine, baseLines.Select(line =>
        {
            var match = Regex.Match(line, @"^GPU\s+(\d+)\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success
                   && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                   && extensions.TryGetValue(index, out var extension)
                ? $"{line} | {extension}"
                : line;
        }));
    }

    public static string NormalizeMetricSeparators(string text)
        => Regex.Replace(text.Trim(), @"\s*\|\s*", " | ");

    public static IReadOnlyList<string> FormatWindowsGpuStatusJson(string output)
    {
        var json = ExtractJsonPayload(output);
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            var rows = new List<string>();
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                    AddWindowsGpuRow(rows, element);
            }
            else
            {
                AddWindowsGpuRow(rows, document.RootElement);
            }

            return rows;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string FormatWindowsCpuTemperatureJson(string output)
    {
        var json = ExtractJsonPayload(output);
        if (string.IsNullOrWhiteSpace(json)) return "";

        try
        {
            using var document = JsonDocument.Parse(json);
            var temperature = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray()
                    .Select(CpuTemperatureCelsius)
                    .Where(value => value is not null)
                    .Max()
                : CpuTemperatureCelsius(document.RootElement);
            return temperature is { } value
                ? $"CPU: {Math.Clamp(value, -20, 125):0.#}C"
                : "";
        }
        catch
        {
            return "";
        }
    }

    public static string FormatWindowsCpuStatusJson(string output)
    {
        var json = ExtractJsonPayload(output);
        if (string.IsNullOrWhiteSpace(json)) return "";

        try
        {
            using var document = JsonDocument.Parse(json);
            var element = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().FirstOrDefault()
                : document.RootElement;
            if (element.ValueKind != JsonValueKind.Object) return "";

            var name = CleanCpuName(JsonString(element, "Name"));
            var utilization = JsonDouble(element, "Utilization") ?? JsonDouble(element, "LoadPercentage");
            var physicalCores = JsonInt(element, "PhysicalCores") ?? JsonInt(element, "NumberOfCores");
            var logicalProcessors = JsonInt(element, "LogicalProcessors") ?? JsonInt(element, "NumberOfLogicalProcessors");
            var currentClock = JsonDouble(element, "CurrentClockMHz") ?? JsonDouble(element, "CurrentClockSpeed");
            var temperature = CpuTemperatureCelsius(element);
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(name)) lines.Add($"CPU: {name}");

            var observations = new List<string>();
            if (utilization is { } load && double.IsFinite(load))
                observations.Add($"{Math.Clamp(load, 0, 100):0.#}% load");
            if (physicalCores is > 0 && logicalProcessors is > 0)
                observations.Add($"{physicalCores}C/{logicalProcessors}T");
            if (temperature is { } celsius && double.IsFinite(celsius))
                observations.Add($"{Math.Clamp(celsius, -20, 125):0.#} °C thermal");
            if (currentClock is { } clock && double.IsFinite(clock) && clock > 0)
                observations.Add($"{clock:0} MHz core");
            if (observations.Count > 0)
                lines.Add($"Telemetry: {string.Join(" | ", observations)}");

            return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "";
        }
        catch
        {
            return "";
        }
    }

    public static string FormatWindowsMemoryStatusJson(string output)
    {
        var json = ExtractJsonPayload(output);
        if (string.IsNullOrWhiteSpace(json)) return "";

        try
        {
            using var document = JsonDocument.Parse(json);
            var element = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().FirstOrDefault()
                : document.RootElement;
            if (element.ValueKind != JsonValueKind.Object) return "";
            var used = JsonDouble(element, "UsedBytes");
            var total = JsonDouble(element, "TotalBytes");
            var usage = JsonDouble(element, "UsagePercent");
            var clock = JsonDouble(element, "ClockMHz");
            if (total is not > 0 || !double.IsFinite(total.Value)) return "";

            var usedGiB = used is >= 0 && double.IsFinite(used.Value) ? used.Value / BytesPerGiB : 0;
            var usagePercent = usage is { } percent && double.IsFinite(percent)
                ? Math.Clamp(percent, 0, 100)
                : Math.Clamp(100 * usedGiB / (total.Value / BytesPerGiB), 0, 100);
            var summary = $"RAM: {usedGiB:0.0}/{total.Value / BytesPerGiB:0.0} GiB | {usagePercent:0.#}%";
            return clock is { } clockMhz && double.IsFinite(clockMhz) && clockMhz > 0
                ? $"{summary} | {clockMhz:0} MHz"
                : summary;
        }
        catch
        {
            return "";
        }
    }

    public static string FormatIntelArcStatus(string? syclLsLine)
    {
        if (string.IsNullOrWhiteSpace(syclLsLine))
            return "Intel Arc GPU";

        var text = syclLsLine.Trim();
        var lastBracket = text.LastIndexOf(']');
        if (lastBracket >= 0 && lastBracket + 1 < text.Length)
            text = text[(lastBracket + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(text)) return "Intel Arc GPU";
        return text.Length > 96 ? $"{text[..93]}..." : text;
    }

    public static VramMemorySnapshot? ParseMemoryLine(string line)
    {
        var parts = line.Split(',').Select(part => part.Trim()).ToArray();
        if (parts.Length < 2) return null;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var freeMb)) return null;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var totalMb)) return null;
        return new VramMemorySnapshot(freeMb / 1024, totalMb / 1024);
    }

    public static string FirstSyclGpuLine(string output)
        => (output ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Contains("level_zero", StringComparison.OrdinalIgnoreCase)
                && line.Contains("gpu", StringComparison.OrdinalIgnoreCase)) ?? "";

    private static void AddWindowsGpuRow(List<string> rows, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return;

        var name = CleanGpuName(JsonString(element, "Name"));
        if (string.IsNullOrWhiteSpace(name)) return;

        var index = JsonInt(element, "Index") ?? rows.Count;
        var utilization = JsonDouble(element, "Utilization");
        var usedBytes = JsonDouble(element, "MemoryUsedBytes");
        var totalBytes = JsonDouble(element, "MemoryTotalBytes");
        var parts = new List<string> { name };

        if (utilization is { } util && double.IsFinite(util))
            parts.Add($"{Math.Clamp(util, 0, 100):0.#}% load");

        var memory = FormatGpuMemory(usedBytes, totalBytes);
        if (!string.IsNullOrWhiteSpace(memory))
            parts.Add(memory);

        rows.Add(NormalizeMetricSeparators($"GPU {index}: {string.Join(" | ", parts)}"));
    }

    private static string FormatGpuMemory(double? usedBytes, double? totalBytes)
    {
        var hasUsed = usedBytes is { } used && double.IsFinite(used) && used >= 0;
        var hasTotal = totalBytes is { } total && double.IsFinite(total) && total > 0;
        if (hasUsed && hasTotal)
            return $"{usedBytes!.Value / BytesPerGiB:0.0}/{totalBytes!.Value / BytesPerGiB:0.0} GiB VRAM";
        if (hasTotal)
            return $"{totalBytes!.Value / BytesPerGiB:0.0} GiB VRAM";
        if (hasUsed)
            return $"{usedBytes!.Value / BytesPerGiB:0.0} GiB VRAM used";
        return "";
    }

    private static double? CsvNumber(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
           && double.IsFinite(parsed)
            ? parsed
            : null;

    private static string CleanGpuName(string? name)
    {
        var cleaned = Regex.Replace(name ?? "", @"\s+", " ").Trim();
        return cleaned.Length > 72 ? $"{cleaned[..69]}..." : cleaned;
    }

    private static string CleanCpuName(string? name)
    {
        var cleaned = Regex.Replace(name ?? "", @"\s+", " ").Trim();
        cleaned = Regex.Replace(cleaned, @"\s+\d+-Core Processor$", "", RegexOptions.IgnoreCase);
        return cleaned.Length > 64 ? $"{cleaned[..61]}..." : cleaned;
    }

    private static double? CpuTemperatureCelsius(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var celsius = JsonDouble(element, "TemperatureCelsius")
            ?? JsonDouble(element, "Celsius")
            ?? JsonDouble(element, "Temperature");
        if (celsius is { } direct && double.IsFinite(direct))
            return direct;

        var kelvinTenths = JsonDouble(element, "CurrentTemperature");
        return kelvinTenths is { } raw && double.IsFinite(raw)
            ? (raw / 10.0) - 273.15
            : null;
    }

    private static string ExtractJsonPayload(string output)
    {
        var text = (output ?? "").Trim();
        if (text.Length == 0) return "";

        var arrayStart = text.IndexOf('[');
        var objectStart = text.IndexOf('{');
        var start = arrayStart >= 0 && objectStart >= 0
            ? Math.Min(arrayStart, objectStart)
            : Math.Max(arrayStart, objectStart);
        if (start < 0) return "";

        var arrayEnd = text.LastIndexOf(']');
        var objectEnd = text.LastIndexOf('}');
        var end = Math.Max(arrayEnd, objectEnd);
        return end >= start ? text[start..(end + 1)] : "";
    }

    private static string? JsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? JsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)) return null;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value) ? value : null;
    }

    private static double? JsonDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)) return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)) return value;
        return null;
    }
}
