namespace LocalLlmConsole.Services;

public static class RuntimeModelAliasService
{
    public static IReadOnlyList<string> ReadAliases(string? customParameters)
    {
        IReadOnlyList<string> arguments;
        try
        {
            arguments = CustomLaunchParameterParser.Parse(customParameters);
        }
        catch (InvalidOperationException)
        {
            // An invalid saved launch command must not hide the entire gateway catalog.
            // Launch validation still reports the malformed command when it is loaded.
            return [];
        }

        var aliases = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            string value;
            if (argument is "--alias" or "-a")
            {
                if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith('-')) continue;
                value = arguments[++index];
            }
            else if (argument.StartsWith("--alias=", StringComparison.Ordinal))
                value = argument[8..];
            else if (argument.StartsWith("-a=", StringComparison.Ordinal))
                value = argument[3..];
            else
                continue;

            foreach (var alias in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!aliases.Contains(alias, StringComparer.Ordinal)) aliases.Add(alias);
            }
        }
        return aliases;
    }
}
