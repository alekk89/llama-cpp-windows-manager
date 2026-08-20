namespace LocalLlmConsole.Services;

public static class LaunchArgumentText
{
    public static string Format(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Where(argument => !string.IsNullOrWhiteSpace(argument)).Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(character => char.IsWhiteSpace(character) || character is '"' or '\\')) return argument;
        return '"' + argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    }
}
