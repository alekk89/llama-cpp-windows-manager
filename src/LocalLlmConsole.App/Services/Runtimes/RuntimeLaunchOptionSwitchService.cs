namespace LocalLlmConsole.Services;

public static class RuntimeLaunchOptionSwitchService
{
    public static IReadOnlyList<RuntimeLaunchOptionDefinition> Normalize(
        IReadOnlyList<RuntimeLaunchOptionDefinition> options)
    {
        var switches = options.Where(option => option.ValueKind == RuntimeLaunchOptionValueKind.Switch).ToArray();
        var consumed = new HashSet<RuntimeLaunchOptionDefinition>();
        var normalized = new List<RuntimeLaunchOptionDefinition>(options.Count);

        foreach (var option in options)
        {
            if (option.ValueKind != RuntimeLaunchOptionValueKind.Switch)
            {
                normalized.Add(option);
                continue;
            }

            if (!consumed.Add(option)) continue;
            var names = LongNames(option).ToArray();
            var enabledName = names.FirstOrDefault(name => !IsNegative(name)) ?? "";
            var disabledName = names.FirstOrDefault(IsNegative) ?? "";

            RuntimeLaunchOptionDefinition? counterpart = null;
            if (!string.IsNullOrWhiteSpace(enabledName) && string.IsNullOrWhiteSpace(disabledName))
            {
                var expectedDisabled = NegativeName(enabledName);
                counterpart = switches.FirstOrDefault(candidate =>
                    !ReferenceEquals(candidate, option)
                    && LongNames(candidate).Contains(expectedDisabled, StringComparer.OrdinalIgnoreCase));
                disabledName = counterpart is null ? "" : expectedDisabled;
            }
            else if (string.IsNullOrWhiteSpace(enabledName) && !string.IsNullOrWhiteSpace(disabledName))
            {
                var expectedEnabled = PositiveName(disabledName);
                counterpart = switches.FirstOrDefault(candidate =>
                    !ReferenceEquals(candidate, option)
                    && LongNames(candidate).Contains(expectedEnabled, StringComparer.OrdinalIgnoreCase));
                enabledName = counterpart is null ? "" : expectedEnabled;
            }

            if (counterpart is not null) consumed.Add(counterpart);
            var aliases = option.Aliases
                .Concat(counterpart?.Aliases ?? [])
                .Append(option.Name)
                .Append(counterpart?.Name ?? "")
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var displayName = !string.IsNullOrWhiteSpace(enabledName)
                ? enabledName
                : disabledName;
            normalized.Add(option with
            {
                Name = displayName,
                Aliases = aliases,
                EnabledName = enabledName,
                DisabledName = disabledName
            });
        }

        return normalized;
    }

    public static string DisplayFlag(RuntimeLaunchOptionDefinition option)
        => !string.IsNullOrWhiteSpace(option.EnabledName)
            ? option.EnabledName
            : IsNegative(option.DisabledName) ? PositiveName(option.DisabledName) : option.Name;

    private static IEnumerable<string> LongNames(RuntimeLaunchOptionDefinition option)
        => option.Aliases.Append(option.Name)
            .Where(name => name.StartsWith("--", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool IsNegative(string name)
        => name.StartsWith("--no-", StringComparison.OrdinalIgnoreCase);

    private static string NegativeName(string enabledName)
        => $"--no-{enabledName.TrimStart('-')}";

    private static string PositiveName(string disabledName)
        => $"--{disabledName[5..]}";
}
