
namespace LocalLlmConsole.Services;

public static class DisplayFormatService
{
    public static string Elapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed.TotalMinutes < 1)
            return $"{Math.Max(0, (int)Math.Floor(elapsed.TotalSeconds))}s";
        if (elapsed.TotalHours < 1)
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s";
        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:00}m {elapsed.Seconds:00}s";
    }

    public static string MetricNumber(double value)
        => RuntimeMetrics.IsFinite(value) ? value.ToString("N3", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.') : "";

    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    public static string BytesOrZero(long bytes)
        => bytes <= 0 ? "0 B" : Bytes(bytes);

    public static string TrimForDisplay(string value, int maxChars)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "No release notes were provided." : value.Trim();
        return text.Length <= maxChars ? text : text[..maxChars] + "\n\n...";
    }
}
