namespace LocalLlmConsole.ControlCli;

internal sealed class Arguments
{
    private readonly Dictionary<string, List<string>> _options = new(StringComparer.OrdinalIgnoreCase);

    public Arguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var item = args[i];
            if (!item.StartsWith("--", StringComparison.Ordinal))
            {
                Positionals.Add(item);
                continue;
            }
            var option = item[2..];
            var equals = option.IndexOf('=');
            if (equals >= 0)
            {
                Add(option[..equals], option[(equals + 1)..]);
                continue;
            }
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                Add(option, args[++i]);
            else
                Add(option, "true");
        }
    }

    public List<string> Positionals { get; } = [];
    public bool Has(string name) => _options.ContainsKey(name);
    public string? Value(string name) => _options.TryGetValue(name, out var values) ? values.LastOrDefault() : null;
    public IReadOnlyList<string> Values(string name) => _options.TryGetValue(name, out var values) ? values : [];

    private void Add(string name, string value)
    {
        if (!_options.TryGetValue(name, out var values)) _options[name] = values = [];
        values.Add(value);
    }
}
