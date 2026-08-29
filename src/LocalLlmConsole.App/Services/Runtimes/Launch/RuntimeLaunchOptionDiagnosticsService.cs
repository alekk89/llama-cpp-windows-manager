namespace LocalLlmConsole.Services;

public sealed record RuntimeLaunchOptionDiagnostic(
    string RuntimeId,
    string RuntimeName,
    RuntimeMode Mode,
    RuntimeBackend Backend,
    string ExecutablePath,
    string WslDistro,
    long ExecutableLastWriteUtcTicks,
    long ExecutableSizeBytes,
    DateTimeOffset RecordedAt,
    int? HelpExitCode,
    string HelpBanner,
    string HelpSha256,
    int HelpCharacterCount,
    int ParsedOptionCount,
    int RenderedOptionCount,
    string Status,
    string Message);

public sealed class RuntimeLaunchOptionDiagnosticsService
{
    private readonly string _diagnosticsRoot;

    public RuntimeLaunchOptionDiagnosticsService(string diagnosticsRoot)
    {
        _diagnosticsRoot = string.IsNullOrWhiteSpace(diagnosticsRoot)
            ? throw new ArgumentException("Runtime option diagnostics root is required.", nameof(diagnosticsRoot))
            : Path.GetFullPath(diagnosticsRoot);
    }

    public string DiagnosticPath(RuntimeChoice runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var safeId = Regex.Replace(runtime.Id, "[^A-Za-z0-9._-]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(safeId)) safeId = "runtime";
        return Path.Combine(_diagnosticsRoot, $"{safeId}.json");
    }

    public async Task RecordAsync(
        RuntimeChoice runtime,
        string wslDistro,
        int? helpExitCode,
        string? helpText,
        int parsedOptionCount,
        int renderedOptionCount,
        string status,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var help = helpText ?? "";
        var file = ExecutableFile(runtime.ExecutablePath);
        var diagnostic = new RuntimeLaunchOptionDiagnostic(
            runtime.Id,
            runtime.DisplayName,
            runtime.Mode,
            runtime.Backend,
            runtime.ExecutablePath,
            wslDistro ?? "",
            file?.LastWriteTimeUtc.Ticks ?? 0,
            file?.Length ?? 0,
            DateTimeOffset.UtcNow,
            helpExitCode,
            HelpBanner(help),
            help.Length == 0 ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(help))).ToLowerInvariant(),
            help.Length,
            Math.Max(0, parsedOptionCount),
            Math.Max(0, renderedOptionCount),
            status?.Trim() ?? "",
            message?.Trim() ?? "");

        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(_diagnosticsRoot);
            var path = DiagnosticPath(runtime);
            temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(diagnostic, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not persist runtime launch-option diagnostics: {ex.Message}");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    private static FileInfo? ExecutableFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file : null;
        }
        catch
        {
            return null;
        }
    }

    private static string HelpBanner(string help)
    {
        var banner = help.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        return banner.Length <= 240 ? banner : banner[..240];
    }
}
