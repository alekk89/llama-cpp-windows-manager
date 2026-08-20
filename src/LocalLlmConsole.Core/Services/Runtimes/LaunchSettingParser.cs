
namespace LocalLlmConsole.Services;

public static class LaunchSettingParser
{
    public static int ReadContextSize(string text)
    {
        if (!TryNormalizeContextSize(text, out var value))
            throw new InvalidOperationException("Context size must be 0, a token count, or shorthand like 196k.");
        return value;
    }

    public static int ReadInt(string text, string label, int min, int? max = null)
    {
        if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"{label} must be a whole number.");
        if (value < min) throw new InvalidOperationException($"{label} must be at least {min}.");
        if (max is not null && value > max.Value) throw new InvalidOperationException($"{label} must be no more than {max.Value}.");
        return value;
    }

    public static double ReadDouble(string text, string label, double min, double? max = null)
    {
        if (!double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"{label} must be a number.");
        if (value < min) throw new InvalidOperationException($"{label} must be at least {min.ToString("0.###", CultureInfo.InvariantCulture)}.");
        if (max is not null && value > max.Value) throw new InvalidOperationException($"{label} must be no more than {max.Value.ToString("0.###", CultureInfo.InvariantCulture)}.");
        return value;
    }

    public static bool TryNormalizeContextSize(string text, out int value)
    {
        value = 0;
        var trimmed = (text ?? "").Trim().Replace("_", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal);
        if (trimmed.Length == 0) return false;
        if (string.Equals(trimmed, "0", StringComparison.Ordinal)) return true;

        var multiplier = 1.0;
        if (trimmed.EndsWith("k", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024.0;
            trimmed = trimmed[..^1].Trim();
        }
        else if (trimmed.EndsWith("m", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024.0 * 1024.0;
            trimmed = trimmed[..^1].Trim();
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return false;
        if (parsed < 0) return false;

        var raw = parsed * multiplier;
        if (multiplier == 1.0 && raw is > 0 and < 512)
            raw *= 1024.0;

        var rounded = raw is > 0 and < 1024
            ? 1024
            : (int)Math.Round(raw / 1024.0, MidpointRounding.AwayFromZero) * 1024;
        value = rounded;
        return value is >= 0 and <= 1_048_576;
    }
}
