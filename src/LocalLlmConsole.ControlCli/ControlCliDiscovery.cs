using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalLlmConsole.ControlCli;

internal sealed record DiscoveryDocument(
    int Version,
    int ProcessId,
    string BaseUrl,
    string ProtectedToken,
    string WorkspaceRoot,
    DateTimeOffset StartedAt);

internal static class ControlCliDiscovery
{
    private const string ProtectedPrefix = "dpapi:v1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LocalLlmConsole:model-api-key:v1");

    public static DiscoveryDocument Discover(Arguments args)
    {
        var paths = new List<string>();
        if (args.Value("connection") is { Length: > 0 } explicitPath) paths.Add(explicitPath);
        if (args.Value("workspace") is { Length: > 0 } workspace) paths.Add(Path.Combine(workspace, "state", "control.json"));
        foreach (var variable in new[] { "LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE", "LLAMA_CPP_CONSOLE_WORKSPACE", "LOCAL_LLM_CONSOLE_WORKSPACE" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } root)
                paths.Add(Path.Combine(root, "state", "control.json"));
        }
        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "llama.cpp Windows Manager", "control.json"));
        paths.Add(Path.Combine(AppContext.BaseDirectory, "data", "state", "control.json"));

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try
            {
                var document = JsonSerializer.Deserialize(
                    File.ReadAllText(path),
                    ControlCliJsonContext.Default.DiscoveryDocument);
                if (document is null || string.IsNullOrWhiteSpace(document.BaseUrl) || string.IsNullOrWhiteSpace(document.ProtectedToken)) continue;
                if (document.ProcessId > 0)
                {
                    try { _ = Process.GetProcessById(document.ProcessId); }
                    catch { continue; }
                }
                return document;
            }
            catch
            {
                // Try the next discovery location.
            }
        }
        throw new InvalidOperationException("llama.cpp Windows Manager control endpoint was not found. Start the app, or pass --connection <control.json> / --workspace <path>.");
    }

    public static string Unprotect(string value)
    {
        if (!value.StartsWith(ProtectedPrefix, StringComparison.Ordinal)) return value;
        var protectedBytes = Convert.FromBase64String(value[ProtectedPrefix.Length..]);
        var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
