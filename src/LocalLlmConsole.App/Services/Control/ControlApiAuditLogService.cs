namespace LocalLlmConsole.Services;

public sealed class ControlApiAuditLogService
{
    private readonly string _logPath;
    private readonly Func<int> _maximumLogSizeMb;

    public ControlApiAuditLogService(string logRoot, Func<int> maximumLogSizeMb)
    {
        if (string.IsNullOrWhiteSpace(logRoot))
            throw new ArgumentException("Log root is required.", nameof(logRoot));
        _maximumLogSizeMb = maximumLogSizeMb ?? throw new ArgumentNullException(nameof(maximumLogSizeMb));
        _logPath = Path.Combine(Path.GetFullPath(logRoot), "control-api.log");
    }

    public string LogPath => _logPath;

    public async Task WriteAsync(
        LocalControlRequest request,
        LocalControlApiResponse response,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var method = SafeToken(request.Method, 12);
        var path = SafePath(request.Path);
        var line = $"[{DateTimeOffset.Now:O}] {method} {path} -> {response.StatusCode} ({elapsed.TotalMilliseconds:N0} ms){Environment.NewLine}";
        var maximumBytes = BoundedLogFile.MegabytesToBytes(Math.Clamp(_maximumLogSizeMb(), 1, 4096));
        await BoundedLogFile.AppendAsync(_logPath, line, maximumBytes);
    }

    private static string SafePath(string path)
    {
        var value = (path ?? "").Split('?', 2)[0];
        value = new string(value.Where(character => !char.IsControl(character)).ToArray());
        return value.Length <= 500 ? value : value[..500];
    }

    private static string SafeToken(string value, int maximumLength)
    {
        var token = new string((value ?? "").Where(character => char.IsLetter(character)).ToArray()).ToUpperInvariant();
        return string.IsNullOrWhiteSpace(token) ? "UNKNOWN" : token[..Math.Min(token.Length, maximumLength)];
    }
}
