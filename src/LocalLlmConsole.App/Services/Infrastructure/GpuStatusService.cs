
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
        var utilization = parts[2];
        var temperature = parts[3];
        var used = double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var usedMb) ? usedMb / 1024 : 0;
        var total = double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var totalMb) ? totalMb / 1024 : 0;
        var memory = total > 0 ? $"{used:0.0}/{total:0.0} GiB" : $"{parts[4]}/{parts[5]} MiB";
        var identity = string.IsNullOrWhiteSpace(name) ? $"GPU {index}" : $"GPU {index}: {name}";
        return NormalizeMetricSeparators($"{identity} | {utilization}% | {temperature}C | {memory}");
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
            if (observations.Count > 0)
                lines.Add($"Telemetry: {string.Join(" | ", observations)}");

            return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "";
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
            parts.Add($"{Math.Clamp(util, 0, 100):0.#}%");

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
            return $"{usedBytes!.Value / BytesPerGiB:0.0}/{totalBytes!.Value / BytesPerGiB:0.0} GiB";
        if (hasTotal)
            return $"{totalBytes!.Value / BytesPerGiB:0.0} GiB";
        if (hasUsed)
            return $"{usedBytes!.Value / BytesPerGiB:0.0} GiB used";
        return "";
    }

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
