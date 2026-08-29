namespace LocalLlmConsole.Services;

public static class Sha256Digest
{
    public static string NormalizeHex(string? value)
    {
        var text = (value ?? "").Trim();
        return text.Length == 64 && text.All(Uri.IsHexDigit) ? text.ToLowerInvariant() : "";
    }

    public static string NormalizeHexOrAlgorithmPrefix(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            text = text["sha256:".Length..];
        return NormalizeHex(text);
    }

    public static string NormalizeLooseHex(string? value)
    {
        var normalized = new string((value ?? "").Trim().Where(Uri.IsHexDigit).ToArray());
        return NormalizeHex(normalized);
    }
}
