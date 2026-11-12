namespace LocalLlmConsole.Services;

/// <summary>
/// Authenticated control request body limits. Most routes accept 1 MiB. Profile
/// import carries the bundle as a JSON string; escaping can expand each bundle
/// byte to at most six bytes (<c>\u00XX</c>), so that route accepts six times
/// the advertised bundle limit plus the normal allowance for the other fields.
/// The bundle itself is still limited to <see cref="ProfileBundleCodec.MaximumBytes"/>.
/// </summary>
public static class ControlRequestBodyLimits
{
    public const int DefaultBytes = 1024 * 1024;
    public const int ProfileImportBytes = (ProfileBundleCodec.MaximumBytes * 6) + DefaultBytes;

    public static int For(string method, string path)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        var question = path.IndexOf('?');
        var segments = (question >= 0 ? path[..question] : path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        var profileImport = method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && segments.Length == 6
            && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("v1", StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("models", StringComparison.OrdinalIgnoreCase)
            && segments[4].Equals("profiles", StringComparison.OrdinalIgnoreCase)
            && segments[5].Equals("import", StringComparison.OrdinalIgnoreCase);
        return profileImport ? ProfileImportBytes : DefaultBytes;
    }
}
