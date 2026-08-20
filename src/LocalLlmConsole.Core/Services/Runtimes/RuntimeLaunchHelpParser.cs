using System.Text.RegularExpressions;

namespace LocalLlmConsole.Services;

public static partial class RuntimeLaunchHelpParser
{
    public static IReadOnlyList<RuntimeLaunchOptionDefinition> Parse(string? helpText)
    {
        if (string.IsNullOrWhiteSpace(helpText)) return [];

        var lines = helpText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(rawLine => AnsiEscape().Replace(rawLine, "").TrimEnd())
            .ToArray();
        var declarationIndent = lines
            .Where(LooksLikeOptionLine)
            .Select(LeadingWhitespace)
            .DefaultIfEmpty(0)
            .Min();
        var maximumDeclarationIndent = declarationIndent + 8;
        var definitions = new List<RuntimeLaunchOptionDefinition>();
        PendingOption? pending = null;
        foreach (var line in lines)
        {
            if (LeadingWhitespace(line) <= maximumDeclarationIndent && TryReadOptionLine(line, out var next))
            {
                AddDefinition(definitions, pending);
                pending = next;
                continue;
            }

            if (pending is null) continue;
            var continuation = line.Trim();
            if (continuation.Length == 0 || continuation.StartsWith("-----", StringComparison.Ordinal))
            {
                AddDefinition(definitions, pending);
                pending = null;
                continue;
            }

            if (!continuation.StartsWith("(env:", StringComparison.OrdinalIgnoreCase))
                pending.Description = JoinText(pending.Description, continuation);
        }

        AddDefinition(definitions, pending);

        return definitions
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool LooksLikeOptionLine(string line)
    {
        var match = OptionName().Match(line);
        return match.Success && line[..match.Index].All(char.IsWhiteSpace);
    }

    private static int LeadingWhitespace(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index])) index++;
        return index;
    }

    private static bool TryReadOptionLine(string line, out PendingOption option)
    {
        option = null!;
        var firstName = OptionName().Match(line);
        if (!firstName.Success || line[..firstName.Index].Any(character => !char.IsWhiteSpace(character))) return false;

        var descriptionStart = FindDescriptionStart(line, firstName.Index + firstName.Length);
        var declaration = descriptionStart >= 0 ? line[firstName.Index..descriptionStart] : line[firstName.Index..];
        var description = descriptionStart >= 0 ? line[descriptionStart..].Trim() : "";
        var nameMatches = OptionName().Matches(declaration).ToArray();
        var names = nameMatches.Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length == 0) return false;

        var lastName = nameMatches[^1];
        var valueHint = declaration[(lastName.Index + lastName.Length)..].Trim(' ', ',', '=', '\t');
        option = new PendingOption(names, valueHint, description);
        return true;
    }

    private static int FindDescriptionStart(string line, int searchStart)
    {
        for (var gap = DescriptionGap().Match(line, searchStart); gap.Success; gap = gap.NextMatch())
        {
            var before = line[..gap.Index].TrimEnd();
            var after = line[(gap.Index + gap.Length)..].TrimStart();
            var alignedAlias = before.EndsWith(",", StringComparison.Ordinal)
                               && OptionName().Match(after) is { Success: true, Index: 0 };
            if (!alignedAlias) return gap.Index + gap.Length;
        }

        return -1;
    }

    private static void AddDefinition(ICollection<RuntimeLaunchOptionDefinition> definitions, PendingOption? pending)
    {
        if (pending is null) return;
        var primary = pending.Names.FirstOrDefault(name => name.StartsWith("--", StringComparison.Ordinal)) ?? pending.Names[0];
        var choices = ChoiceValues(pending.ValueHint, pending.Description);
        var kind = InferValueKind(primary, pending.ValueHint, pending.Description, choices);
        definitions.Add(new RuntimeLaunchOptionDefinition(
            primary,
            pending.Names,
            pending.ValueHint,
            pending.Description,
            kind,
            choices,
            AdvertisedDefault($"{pending.ValueHint} {pending.Description}")));
    }

    private static string JoinText(string first, string second)
        => string.IsNullOrWhiteSpace(first) ? second : $"{first} {second}";

    private static IReadOnlyList<string> ChoiceValues(string valueHint, string description)
    {
        var match = ChoiceGroup().Match(valueHint);
        if (match.Success)
        {
            var values = match.Groups["values"].Value;
            var separator = values.Contains('|', StringComparison.Ordinal) ? '|' : ',';
            if (separator != ',' || string.Equals(match.Groups["open"].Value, "{", StringComparison.Ordinal))
                return DistinctChoices(values.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var bullets = BulletChoice().Matches(description)
            .Select(candidate => candidate.Groups["value"].Value)
            .ToArray();
        if (bullets.Length >= 2) return DistinctChoices(bullets);

        var labeled = LabeledChoice().Matches(description)
            .Select(candidate => candidate.Groups["value"].Value)
            .ToArray();
        if (labeled.Length >= 3) return DistinctChoices(labeled);

        if (!description.Contains('>') && !description.Contains('<'))
        {
            var numeric = NumericChoice().Matches(description)
                .Select(candidate => candidate.Groups["value"].Value)
                .ToArray();
            if (numeric.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 3)
                return DistinctChoices(numeric);
        }

        return [];
    }

    private static IReadOnlyList<string> DistinctChoices(IEnumerable<string> choices)
        => choices.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string AdvertisedDefault(string description)
    {
        var match = DefaultValuePattern().Match(description);
        if (!match.Success) return "";
        return match.Groups["value"].Value.Trim().Trim('"', '\'');
    }

    private static RuntimeLaunchOptionValueKind InferValueKind(
        string name,
        string valueHint,
        string description,
        IReadOnlyList<string> choices)
    {
        if (string.IsNullOrWhiteSpace(valueHint)) return RuntimeLaunchOptionValueKind.Switch;
        if (choices.Count > 1) return RuntimeLaunchOptionValueKind.Choice;
        var semanticText = $"{name} {valueHint} {description}";
        if (name.EndsWith("-dir", StringComparison.OrdinalIgnoreCase)
            || name.Equals("--slot-save-path", StringComparison.OrdinalIgnoreCase)
            || semanticText.Contains("directory", StringComparison.OrdinalIgnoreCase)
            || semanticText.Contains("folder", StringComparison.OrdinalIgnoreCase))
            return RuntimeLaunchOptionValueKind.Directory;
        if (name.Contains("file", StringComparison.OrdinalIgnoreCase)
            || name.Contains("path", StringComparison.OrdinalIgnoreCase)
            || valueHint.Contains("FILE", StringComparison.OrdinalIgnoreCase)
            || valueHint.Contains("PATH", StringComparison.OrdinalIgnoreCase)
            || string.Equals(valueHint, "FNAME", StringComparison.OrdinalIgnoreCase))
            return RuntimeLaunchOptionValueKind.File;
        return RuntimeLaunchOptionValueKind.Text;
    }

    [GeneratedRegex("\\x1B(?:[@-Z\\\\-_]|\\[[0-?]*[ -/]*[@-~])")]
    private static partial Regex AnsiEscape();

    [GeneratedRegex("(?<!\\S)-{1,2}[A-Za-z](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?![A-Za-z0-9*-])")]
    private static partial Regex OptionName();

    [GeneratedRegex("\\s{2,}")]
    private static partial Regex DescriptionGap();

    [GeneratedRegex("(?<open>[\\[<{(])(?<values>[^\\]}>)]*[|,][^\\]}>)]*)[\\]}>)]")]
    private static partial Regex ChoiceGroup();

    [GeneratedRegex("(?:^|\\s)-\\s+(?<value>[A-Za-z0-9][A-Za-z0-9+.-]*)\\s*:")]
    private static partial Regex BulletChoice();

    [GeneratedRegex("(?:^|[\\s,])[A-Za-z][A-Za-z0-9 -]*\\((?<value>-?\\d+(?:\\.\\d+)?)\\)")]
    private static partial Regex LabeledChoice();

    [GeneratedRegex("(?:^|[\\s,(])(?<value>-?\\d+(?:\\.\\d+)?)\\s*(?:=|:|-(?=[A-Za-z]))")]
    private static partial Regex NumericChoice();

    [GeneratedRegex("(?i:\\bdefault(?:\\s+value)?\\s*(?::|=|\\bis\\b)\\s*)(?<value>\"[^\"]*\"|'[^']*'|[^\\s,)\\]]+)")]
    private static partial Regex DefaultValuePattern();

    private sealed record PendingOption(IReadOnlyList<string> Names, string ValueHint, string InitialDescription)
    {
        public string Description { get; set; } = InitialDescription;
    }
}
