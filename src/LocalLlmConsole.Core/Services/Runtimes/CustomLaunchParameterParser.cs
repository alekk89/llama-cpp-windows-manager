namespace LocalLlmConsole.Services;

public static class CustomLaunchParameterParser
{
    public static IReadOnlyList<string> Parse(string? value)
        => ShellArgumentTokenizer.Tokenize(value, ShellTokenizationMode.StrictArguments);
}
