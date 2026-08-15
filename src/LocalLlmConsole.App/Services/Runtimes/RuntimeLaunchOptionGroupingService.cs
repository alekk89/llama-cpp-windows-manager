namespace LocalLlmConsole.Services;

public sealed record RuntimeLaunchOptionGroup(
    string Id,
    string Title,
    IReadOnlyList<RuntimeLaunchOptionDefinition> Options);

public static class RuntimeLaunchOptionGroupingService
{
    private static readonly RuntimeLaunchOptionGroupRule[] Rules =
    [
        Rule("performance-memory", "Performance & Memory",
            "cpu", "thread", "threads", "numa", "batch", "memory", "mmap", "mlock", "tensor", "tensors", "gpu", "kv", "cache", "flash", "priority", "prio", "poll", "affinity", "offload"),
        Rule("context-model", "Context & Model Behavior",
            "context", "ctx", "rope", "yarn", "swa", "embedding", "pooling", "attention", "chat", "jinja", "template", "bos", "eos"),
        Rule("generation-sampling", "Generation & Sampling",
            "sampler", "samplers", "sampling", "temperature", "temp", "top-k", "top-p", "min-p", "penalty", "penalties", "repeat", "grammar", "seed", "logit", "logits", "predict"),
        Rule("speculative-draft", "Speculative & Draft",
            "spec", "speculative", "draft", "lookup", "mtp", "ngram"),
        Rule("vision-multimodal", "Vision & Multimodal",
            "vision", "image", "multimodal", "audio", "media"),
        Rule("server-slots", "Server & Slots",
            "server", "slot", "http", "timeout", "parallel", "continuous", "connection", "request", "websocket", "health"),
        Rule("diagnostics-output", "Diagnostics & Output",
            "log", "verbose", "debug", "trace", "print", "dump", "check", "benchmark", "timing", "display")
    ];

    public static IReadOnlyList<RuntimeLaunchOptionGroup> Group(IReadOnlyList<RuntimeLaunchOptionDefinition> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var buckets = Rules.ToDictionary(rule => rule.Id, _ => new List<RuntimeLaunchOptionDefinition>(), StringComparer.Ordinal);
        var other = new List<RuntimeLaunchOptionDefinition>();

        foreach (var option in options)
        {
            var searchable = SearchText(option);
            var rule = Rules.FirstOrDefault(candidate => candidate.Terms.Any(term => ContainsTerm(searchable, term)));
            if (rule is null)
                other.Add(option);
            else
                buckets[rule.Id].Add(option);
        }

        var groups = Rules
            .Where(rule => buckets[rule.Id].Count > 0)
            .Select(rule => new RuntimeLaunchOptionGroup(rule.Id, rule.Title, buckets[rule.Id].ToArray()))
            .ToList();
        if (other.Count > 0)
            groups.Add(new RuntimeLaunchOptionGroup("other", "Other Runtime Options", other));
        return groups;
    }

    private static bool ContainsTerm(string text, string term)
    {
        var start = 0;
        while ((start = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = start == 0 || !char.IsLetterOrDigit(text[start - 1]);
            var end = start + term.Length;
            var after = end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (before && after) return true;
            start++;
        }

        return false;
    }

    private static string SearchText(RuntimeLaunchOptionDefinition option)
        => $"{option.Name} {string.Join(' ', option.Aliases)} {option.ValueHint} {option.Description}";

    private static RuntimeLaunchOptionGroupRule Rule(string id, string title, params string[] terms)
        => new(id, title, terms);

    private sealed record RuntimeLaunchOptionGroupRule(string Id, string Title, IReadOnlyList<string> Terms);
}
