
namespace LocalLlmConsole.Services;

public sealed partial class LlamaProcessSupervisor : IDisposable
{
    private static string ToWslPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (value.StartsWith('/')) return value.Replace('\\', '/');
        var full = Path.GetFullPath(value);
        if (full.Length >= 3 && full[1] == ':' && (full[2] == '\\' || full[2] == '/'))
        {
            var drive = char.ToLowerInvariant(full[0]);
            var rest = full[3..].Replace('\\', '/');
            return $"/mnt/{drive}/{rest}";
        }
        return full.Replace('\\', '/');
    }

    private static string WslDirectoryName(string path)
    {
        var normalized = (path ?? "").Replace('\\', '/').TrimEnd('/');
        var split = normalized.LastIndexOf('/');
        return split <= 0 ? "" : normalized[..split];
    }

    private static string WslSiblingDirectory(string path, string sibling)
    {
        var parent = WslDirectoryName(path);
        return string.IsNullOrWhiteSpace(parent) ? sibling : $"{parent.TrimEnd('/')}/{sibling}";
    }

    private static string BashQuote(string value)
    {
        var safe = value ?? "";
        if (safe.IndexOf('\0') >= 0)
            throw new ArgumentException("Shell arguments cannot contain null bytes.");
        return "'" + safe.Replace("'", "'\"'\"'") + "'";
    }
}
