namespace LocalLlmConsole.Services;

public static class RuntimeMtpLogParser
{
    private static readonly Regex MtpStatisticsPattern = new(
        @"statistics\s+(?:draft-mtp|mtp)\s*:.*?#gen tokens\s*=\s*(?<generated>[\d,]+).*?#acc tokens\s*=\s*(?<accepted>[\d,]+)(?:.*?dur\(b,g,a\)\s*=\s*(?<batchMs>[-+0-9.,eE]+)\s*,\s*(?<generatedMs>[-+0-9.,eE]+)\s*,\s*(?<acceptedMs>[-+0-9.,eE]+)\s*ms)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DraftAcceptancePattern = new(
        @"draft acceptance(?:\s+rate)?\s*=\s*[-+0-9.eE]+\s*\(\s*(?<accepted>[\d,]+)\s+accepted\s*/\s*(?<generated>[\d,]+)\s+generated\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static RuntimeMtpTokenSnapshot? Parse(string raw)
        => SnapshotFromMatches(MtpStatisticsPattern.Matches(raw))
           ?? SnapshotFromMatches(DraftAcceptancePattern.Matches(raw));

    private static RuntimeMtpTokenSnapshot? SnapshotFromMatches(MatchCollection matches)
    {
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var generated = ParseCounter(match.Groups["generated"].Value);
            var accepted = ParseCounter(match.Groups["accepted"].Value);
            var generatedSeconds = ParseMilliseconds(match.Groups["generatedMs"].Value);
            var acceptedSeconds = generatedSeconds;
            if (generated is not null || accepted is not null)
                return new RuntimeMtpTokenSnapshot(generated, accepted, generatedSeconds, acceptedSeconds);
        }

        return null;
    }

    private static double? ParseCounter(string raw)
    {
        var normalized = raw.Replace(",", "", StringComparison.Ordinal).Trim();
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double? ParseMilliseconds(string raw)
    {
        var value = ParseCounter(raw);
        return value is > 0 ? value.Value / 1000 : null;
    }
}
