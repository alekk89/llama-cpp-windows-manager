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
        "--cache-type-k-draft", "--cache-type-v-draft", "--metrics",
        // Short aliases that replace application-managed model and companion files.
        "-md", "-mm"
    };

    private const string RemoteContentReason = "downloads models or other content from a remote source";
    private const string FileServingReason = "exposes local files or tools through the HTTP server";
    private const string RemoteHostReason = "sends model data to remote hosts";
    private const string EndpointReason = "changes how the application-managed endpoint is served";

    // Denied everywhere: launch, preview, benchmark validation, profile editing, and profile
    // import. Local tuning and local input files (for example --lora) remain allowed.
    private static readonly Dictionary<string, string> BlockedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["-mu"] = RemoteContentReason,
        ["--model-url"] = RemoteContentReason,
        ["-mmu"] = RemoteContentReason,
        ["--mmproj-url"] = RemoteContentReason,
        ["-hf"] = RemoteContentReason,
        ["-hfr"] = RemoteContentReason,
        ["--hf-repo"] = RemoteContentReason,
        ["-hff"] = RemoteContentReason,
        ["--hf-file"] = RemoteContentReason,
        ["-hfd"] = RemoteContentReason,
        ["-hfrd"] = RemoteContentReason,
        ["--hf-repo-draft"] = RemoteContentReason,
        ["--spec-draft-hf"] = RemoteContentReason,
        ["-hfv"] = RemoteContentReason,
        ["-hfrv"] = RemoteContentReason,
        ["--hf-repo-v"] = RemoteContentReason,
        ["-hffv"] = RemoteContentReason,
        ["--hf-file-v"] = RemoteContentReason,
        ["-hft"] = RemoteContentReason,
        ["--hf-token"] = RemoteContentReason,
        ["-dr"] = RemoteContentReason,
        ["--docker-repo"] = RemoteContentReason,
        ["--path"] = FileServingReason,
        ["--media-path"] = FileServingReason,
        ["--tools"] = FileServingReason,
        ["--rpc"] = RemoteHostReason,
        ["--ssl-key-file"] = EndpointReason,
        ["--ssl-cert-file"] = EndpointReason
    };

    // Local options that choose where llama-server writes files. A user may set them on this
    // machine, but an imported profile bundle is untrusted and must not choose write locations.
    private static readonly HashSet<string> ImportRestrictedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "--slot-save-path", "--log-file", "-lcd", "--lookup-cache-dynamic"
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

    public static bool IsBlocked(string name) => BlockedNames.ContainsKey(name);

    public static bool IsImportRestricted(string name) => ImportRestrictedNames.Contains(name);

    private static bool IsProjectorOffloadSwitch(RuntimeLaunchOptionDefinition option)
        => option.ValueKind == RuntimeLaunchOptionValueKind.Switch
           && option.Aliases.Append(option.Name).All(name =>
               name is "--mmproj-offload" or "--no-mmproj-offload");

    public static bool CanRender(RuntimeLaunchOptionDefinition option)
        => option.Name.StartsWith("--", StringComparison.Ordinal)
           && !option.Aliases.Any(IsAppManaged)
           && !option.Aliases.Any(IsBlocked)
           && !option.Aliases.Any(UnsafeExactNames.Contains)
           && (IsProjectorOffloadSwitch(option)
               || !option.Aliases.Any(alias => UnsafeFragments.Any(fragment => alias.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
           && !UnsupportedDescriptionFragments.Any(fragment => option.Description.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public static void ValidateCustomArguments(IReadOnlyList<string> arguments)
    {
        var problem = CustomArgumentProblems(arguments).FirstOrDefault();
        if (problem is not null)
            throw new InvalidOperationException(problem);
    }

    /// <summary>Returns one message per managed or blocked option in <paramref name="arguments"/>.</summary>
    public static IReadOnlyList<string> CustomArgumentProblems(IReadOnlyList<string> arguments)
        => CustomArgumentProblems(arguments, imported: false);

    /// <summary>
    /// Validates custom parameters from an imported profile bundle: the launch policy plus
    /// rejection of options that choose where files are written.
    /// </summary>
    public static IReadOnlyList<string> ImportedCustomArgumentProblems(IReadOnlyList<string> arguments)
        => CustomArgumentProblems(arguments, imported: true);

    private static IReadOnlyList<string> CustomArgumentProblems(IReadOnlyList<string> arguments, bool imported)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var problems = new List<string>();
        foreach (var argument in arguments.Where(value => value.StartsWith("-", StringComparison.Ordinal)))
        {
            var name = argument.Split('=', 2)[0];
            if (IsAppManaged(name))
                problems.Add($"Custom parameter '{name}' is managed by the application and cannot be overridden.");
            else if (BlockedNames.TryGetValue(name, out var reason))
                problems.Add($"Custom parameter '{name}' is not allowed because it {reason}. Remove it from Custom parameters.");
            else if (imported && IsImportRestricted(name))
                problems.Add($"Custom parameter '{name}' cannot be imported because it chooses where files are written on this computer. Remove it from the imported Custom parameters and set it locally after import if needed.");
        }
        return problems;
    }
}
