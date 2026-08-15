namespace LocalLlmConsole.Services;

public enum RuntimeLaunchOptionValueKind
{
    Switch,
    Text,
    Choice,
    File,
    Directory
}

public sealed record RuntimeLaunchOptionDefinition(
    string Name,
    IReadOnlyList<string> Aliases,
    string ValueHint,
    string Description,
    RuntimeLaunchOptionValueKind ValueKind,
    IReadOnlyList<string> Choices,
    string DefaultValue = "",
    string EnabledName = "",
    string DisabledName = "");

public static class RuntimeLaunchOptionPolicy
{
    private static readonly HashSet<string> ManagedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "-m", "--model", "--model-url", "--model-draft", "--mtp-head", "--mmproj",
        "--host", "--port", "--api-key", "--api-key-file", "--hf-token", "--hf-repo",
        "--ctx-size", "--n-gpu-layers", "--split-mode", "--device", "--tensor-split",
        "--parallel", "--batch-size", "--ubatch-size", "--threads", "--flash-attn",
        "--cache-type-k", "--cache-type-v", "--kv-offload", "--no-kv-offload", "--kv-unified", "--no-kv-unified",
        "--cache-ram", "--ctx-checkpoints", "--checkpoint-min-step", "--cont-batching", "--no-cont-batching",
        "--reasoning", "--reasoning-format", "--reasoning-effort", "--reasoning-budget", "--reasoning-budget-message",
        "--reasoning-preserve", "--no-reasoning-preserve", "--no-mmproj", "--image-min-tokens", "--image-max-tokens",
        "--jinja", "--no-jinja", "--mmap", "--no-mmap", "--mlock", "--rope-scaling", "--rope-scale",
        "--rope-freq-base", "--rope-freq-scale", "--temp", "--top-k", "--top-p", "--min-p", "--predict", "--seed",
        "--repeat-last-n", "--repeat-penalty", "--presence-penalty", "--frequency-penalty", "--spec-type",
        "--n-gpu-layers-draft", "--spec-draft-n-min", "--spec-draft-n-max", "--spec-draft-p-split", "--spec-draft-p-min",
        "--cache-type-k-draft", "--cache-type-v-draft", "--metrics"
    };

    private static readonly string[] UnsafeFragments =
    [
        "help", "version", "completion", "list-", "print-", "api-key", "token", "password", "credential",
        "model", "mmproj", "adapter", "control-vector", "rpc", "host", "port", "webui", "ssl", "public",
        "agent", "mcp", "cors", "cache-list", "tools"
    ];

    private static readonly HashSet<string> UnsafeExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "--path", "--media-path", "--api-prefix", "--docker-repo", "--hf-file", "--spec-draft-hf", "--hf-repo-draft"
    };

    private static readonly string[] UnsupportedDescriptionFragments =
    [
        "argument has been removed",
        "deprecated",
        "download weights from the internet"
    ];

    public static bool IsAppManaged(string name) => ManagedNames.Contains(name);

    public static bool CanRender(RuntimeLaunchOptionDefinition option)
        => option.Name.StartsWith("--", StringComparison.Ordinal)
           && !option.Aliases.Any(IsAppManaged)
           && !option.Aliases.Any(UnsafeExactNames.Contains)
           && !option.Aliases.Any(alias => UnsafeFragments.Any(fragment => alias.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
           && !UnsupportedDescriptionFragments.Any(fragment => option.Description.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public static void ValidateCustomArguments(IReadOnlyList<string> arguments)
    {
        foreach (var argument in arguments.Where(value => value.StartsWith("-", StringComparison.Ordinal)))
        {
            var name = argument.Split('=', 2)[0];
            if (IsAppManaged(name))
                throw new InvalidOperationException($"Custom parameter '{name}' is managed by the application and cannot be overridden.");
        }
    }
}
