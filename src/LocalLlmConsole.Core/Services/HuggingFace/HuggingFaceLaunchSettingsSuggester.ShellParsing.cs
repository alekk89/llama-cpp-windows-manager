namespace LocalLlmConsole.Services;

public static partial class HuggingFaceLaunchSettingsSuggester
{
    private static IEnumerable<string> TokenizeShell(string command)
    {
        var token = new StringBuilder();
        var quote = '\0';
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                    continue;
                }
                token.Append(c);
                continue;
            }
            if (c is '\'' or '"')
            {
                quote = c;
                continue;
            }
            if (c == '\\')
            {
                if (i + 1 < command.Length && command[i + 1] == '\n')
                {
                    i++;
                    continue;
                }
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    token.Clear();
                }
                continue;
            }
            token.Append(c);
        }
        if (token.Length > 0)
            yield return token.ToString();
    }
}
