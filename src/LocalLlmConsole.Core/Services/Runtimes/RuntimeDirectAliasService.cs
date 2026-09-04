using System.Globalization;
using System.Text.RegularExpressions;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public static class RuntimeDirectAliasService
{
    public static string ShortModelId(string modelPath)
    {
        var name = modelPath.Replace('\\', '/').Split('/').Last();
        if (name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) name = name[..^5];
        name = Regex.Replace(name, @"-\d{5}-of-\d{5}$", "", RegexOptions.CultureInvariant);
        name = new string(name.Select(character => char.IsWhiteSpace(character) || character == ',' ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(name) ? "model" : name;
    }

    public static string ValidateSuffix(string? suffix)
    {
        var value = (suffix ?? "").Trim();
        if (value.Length > 64 || value.Any(character => char.IsControl(character) || character is ',' or '/' or '\\'))
            throw new InvalidOperationException("Direct model ID suffix must be at most 64 characters and cannot contain commas, slashes or control characters.");
        return value;
    }

    public static AppSettings ForLaunch(AppSettings settings, string modelPath, IEnumerable<string> occupiedAliases)
    {
        var arguments = CustomLaunchParameterParser.Parse(settings.CustomParameters);
        var aliases = RuntimeModelAliasService.ReadAliases(settings.CustomParameters);
        var suffix = ValidateSuffix(settings.DirectModelAliasSuffix);
        var occupied = occupiedAliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requested = aliases.Count == 0 ? [ShortModelId(modelPath)] : aliases;
        var effective = new List<string>();
        foreach (var alias in requested)
        {
            // Recovered sessions carry their effective arguments; do not append the suffix again.
            var alreadySuffixed = suffix.Length > 0 && Regex.IsMatch(alias,
                Regex.Escape(suffix) + @"(?::\d+)?$", RegexOptions.CultureInvariant);
            var preferred = alreadySuffixed ? alias : alias + suffix;
            var candidate = preferred;
            for (var number = 2; !occupied.Add(candidate); number++)
                candidate = preferred + ":" + number.ToString(CultureInfo.InvariantCulture);
            effective.Add(candidate);
        }

        var remaining = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is "--alias" or "-a")
            {
                if (index + 1 < arguments.Count && !arguments[index + 1].StartsWith('-')) index++;
            }
            else if (!argument.StartsWith("--alias=", StringComparison.Ordinal) && !argument.StartsWith("-a=", StringComparison.Ordinal))
                remaining.Add(argument);
        }
        remaining.Add("--alias");
        remaining.Add(string.Join(',', effective));
        return settings with { CustomParameters = string.Join(" ", remaining.Select(LaunchArgumentText.Quote)) };
    }
}
