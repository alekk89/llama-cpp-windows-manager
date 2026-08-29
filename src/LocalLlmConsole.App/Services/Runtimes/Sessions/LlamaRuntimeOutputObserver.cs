namespace LocalLlmConsole.Services;

public static class LlamaRuntimeOutputObserver
{
    public static bool Observe(string? line, BoundedLogWriter? log, string apiKey)
    {
        if (line is null)
            return false;

        try
        {
            log?.WriteLine(LogFileService.RedactSensitiveText(line, apiKey));
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not write llama runtime output to log: {ex.Message}");
        }
        return LooksLoaded(line);
    }

    public static bool LooksLoaded(string line)
        => line.Contains("HTTP server", StringComparison.OrdinalIgnoreCase) && line.Contains("listening", StringComparison.OrdinalIgnoreCase)
            || line.Contains("server is listening", StringComparison.OrdinalIgnoreCase)
            || line.Contains("listening on", StringComparison.OrdinalIgnoreCase)
            || line.Contains("server started", StringComparison.OrdinalIgnoreCase)
            || line.Contains("server listening", StringComparison.OrdinalIgnoreCase)
            || line.Contains("model loaded", StringComparison.OrdinalIgnoreCase);
}
