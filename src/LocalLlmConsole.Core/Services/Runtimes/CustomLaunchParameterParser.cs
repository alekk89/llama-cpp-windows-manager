namespace LocalLlmConsole.Services;

public static class CustomLaunchParameterParser
{
    public static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        var args = new List<string>();
        var token = new StringBuilder();
        var quote = '\0';
        var tokenStarted = false;
        var text = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\0')
                throw new InvalidOperationException("Custom parameters cannot contain null bytes.");

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                    tokenStarted = true;
                    continue;
                }
                if (quote == '"' && c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
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
                tokenStarted = true;
                continue;
            }

            if (c == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next == '\n')
                {
                    i++;
                    continue;
                }
                if (char.IsWhiteSpace(next) || next is '\'' or '"')
                {
                    token.Append(next);
                    tokenStarted = true;
                    i++;
                    continue;
                }
            }

            if (char.IsWhiteSpace(c))
            {
                if (tokenStarted)
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

        if (quote != '\0')
            throw new InvalidOperationException("Custom parameters contain an unterminated quote.");

        if (tokenStarted)
            args.Add(token.ToString());

        return args;
    }
}
