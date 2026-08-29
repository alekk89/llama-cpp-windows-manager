namespace LocalLlmConsole.Services;

public static partial class HuggingFaceLaunchSettingsSuggester
{
    private static IEnumerable<string> TokenizeShell(string command)
        => ShellArgumentTokenizer.Tokenize(command, ShellTokenizationMode.CommandSuggestion);
}
