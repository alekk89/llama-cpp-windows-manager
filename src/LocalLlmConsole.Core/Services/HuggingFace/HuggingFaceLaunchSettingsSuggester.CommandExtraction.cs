namespace LocalLlmConsole.Services;

public static partial class HuggingFaceLaunchSettingsSuggester
{
    private static string ExtractPreferredLlamaCommand(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        return ExtractCommand(markdown, "llama-server")
            ?? ExtractCommand(markdown, "llama-cli")
            ?? "";
    }

    private static string? ExtractCommand(string markdown, string executable)
    {
        var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var index = normalized.IndexOf(executable, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;

        var start = normalized.LastIndexOf('\n', index);
        start = start < 0 ? 0 : start + 1;
        var end = start;
        var lines = normalized[start..].Split('\n');
        var builder = new StringBuilder();
        var collecting = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim().Trim('`');
            if (!collecting)
            {
                if (!line.Contains(executable, StringComparison.OrdinalIgnoreCase))
                    continue;
                collecting = true;
            }
            else if (string.IsNullOrWhiteSpace(line) || line.StartsWith("* * *", StringComparison.Ordinal))
            {
                break;
            }
            else if (!rawLine.StartsWith(' ') && !rawLine.StartsWith('\t') && !line.StartsWith('-') && !builder.ToString().TrimEnd().EndsWith('\\'))
            {
                break;
            }

            builder.Append(line).Append(' ');
            end += rawLine.Length + 1;
            if (!line.EndsWith('\\') && builder.Length > 0 && end > index && !HasLikelyContinuation(lines, builder))
                break;
            if (builder.Length > 4000) break;
        }

        return builder.ToString().Replace("\\ ", " ").Trim();
    }

    private static bool HasLikelyContinuation(string[] lines, StringBuilder builder)
        => builder.ToString().TrimEnd().EndsWith('\\')
            || lines.Any(line => line.TrimStart().StartsWith("--", StringComparison.Ordinal));
}
