namespace LocalLlmConsole.Services;

public enum ShellTokenizationMode
{
    StrictArguments,
    CommandSuggestion
}

public static class ShellArgumentTokenizer
{
    public static IReadOnlyList<string> Tokenize(string? value, ShellTokenizationMode mode)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();

        var strict = mode == ShellTokenizationMode.StrictArguments;
        var text = strict
            ? value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            : value;
        var args = new List<string>();
        var token = new StringBuilder();
        var quote = '\0';
        var tokenStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (strict && c == '\0')
                throw new InvalidOperationException("Custom parameters cannot contain null bytes.");

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                    if (strict) tokenStarted = true;
                    continue;
                }
                if (strict && quote == '"' && c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
                {
                    token.Append(text[++i]);
                    tokenStarted = true;
                    continue;
                }

                token.Append(c);
                tokenStarted = true;
                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                if (strict) tokenStarted = true;
                continue;
            }

            if (c == '\\')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                    continue;
                }
                if (!strict)
                    continue;
                if (i + 1 < text.Length && (char.IsWhiteSpace(text[i + 1]) || text[i + 1] is '\'' or '"'))
                {
                    token.Append(text[++i]);
                    tokenStarted = true;
                    continue;
                }
            }

            if (char.IsWhiteSpace(c))
            {
                if (strict ? tokenStarted : token.Length > 0)
                {
                    args.Add(token.ToString());
                    token.Clear();
                    tokenStarted = false;
                }
                continue;
            }

            token.Append(c);
            tokenStarted = true;
        }

        if (strict && quote != '\0')
            throw new InvalidOperationException("Custom parameters contain an unterminated quote.");
        if (strict ? tokenStarted : token.Length > 0)
            args.Add(token.ToString());
        return args;
    }
}
