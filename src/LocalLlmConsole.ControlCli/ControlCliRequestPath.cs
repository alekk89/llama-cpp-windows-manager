namespace LocalLlmConsole.ControlCli;

/// <summary>
/// Raw request paths are guarded by segment. HttpClient resolves dot segments
/// and backslashes before sending, so only canonical paths are accepted; the
/// guarded route is then exactly the route the Manager receives.
/// </summary>
internal static class ControlCliRequestPath
{
    public static string[] Segments(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Contains('#'))
            throw NonCanonical(path);
        var question = path.IndexOf('?');
        var route = question >= 0 ? path[..question] : path;
        var segments = new List<string>();
        foreach (var raw in route.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var decoded = Uri.UnescapeDataString(raw);
            if (raw.Contains('\\') || decoded.Contains('\\') || decoded is "." or "..")
                throw NonCanonical(path);
            segments.Add(decoded);
        }
        return [.. segments];
    }

    private static InvalidOperationException NonCanonical(string path)
        => new($"Raw request path '{path}' is not canonical. Remove '.', '..', backslash, and fragment segments and send the resolved /api/v1 route.");
}
