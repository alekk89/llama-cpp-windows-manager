using System.IO.Compression;

namespace LocalLlmConsole.Services;

public sealed record DiagnosticsBundleRequest(
    string OutputDirectory,
    string LogsDirectory,
    string AppVersion,
    AppSettings Settings,
    IReadOnlyList<ModelRecord> Models,
    IReadOnlyList<RuntimeRecord> Runtimes,
    IReadOnlyList<JobRecord> Jobs,
    IReadOnlyList<LoadedModelSessionSnapshot> Sessions,
    WslEnvironmentReport Wsl,
    string GpuSummary,
    string CpuSummary);

public sealed record DiagnosticsBundleResult(string ArchivePath, int IncludedLogCount);

public static class DiagnosticsBundleService
{
    private const int MaximumLogCount = 10;
    private const int MaximumLogCharacters = 256_000;

    public static async Task<DiagnosticsBundleResult> CreateAsync(
        DiagnosticsBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);

        return await Task.Run(() => CreateCoreAsync(request, cancellationToken), cancellationToken);
    }

    private static async Task<DiagnosticsBundleResult> CreateCoreAsync(
        DiagnosticsBundleRequest request,
        CancellationToken cancellationToken)
    {

        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var archivePath = UniqueArchivePath(outputDirectory, DateTimeOffset.Now);
        var logs = RecentLogs(request.LogsDirectory);

        await using var stream = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);

        await WriteEntryAsync(
            archive,
            "README.txt",
            "This diagnostics bundle was created locally by llama.cpp Windows Manager.\r\n"
            + "It intentionally excludes API keys, control tokens, database contents, launch arguments, and full model/runtime paths.\r\n"
            + "Log redaction is best effort. Review the archive before sharing it.\r\n",
            cancellationToken);
        await WriteEntryAsync(
            archive,
            "summary.json",
            JsonSerializer.Serialize(BuildSummary(request), new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        var included = 0;
        foreach (var log in logs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = LogFileService.Tail(log, MaximumLogCharacters);
                text = RedactDiagnosticText(text, request);
                await WriteEntryAsync(
                    archive,
                    $"logs/{included + 1:D2}-{SafeFileName(Path.GetFileName(log))}",
                    text,
                    cancellationToken);
                included++;
            }
            catch (IOException)
            {
                // A log may rotate or disappear while the bundle is being created.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue with the remaining readable logs.
            }
        }

        return new DiagnosticsBundleResult(archivePath, included);
    }

    private static object BuildSummary(DiagnosticsBundleRequest request)
        => new
        {
            schemaVersion = 1,
            createdAt = DateTimeOffset.UtcNow,
            application = new
            {
                name = "llama.cpp Windows Manager",
                version = request.AppVersion,
                framework = RuntimeInformation.FrameworkDescription,
                operatingSystem = RuntimeInformation.OSDescription,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                osArchitecture = RuntimeInformation.OSArchitecture.ToString()
            },
            settings = new
            {
                request.Settings.UiCulture,
                request.Settings.ThemeMode,
                request.Settings.ModelAccessMode,
                request.Settings.AutoLoadGatewayEnabled,
                request.Settings.AutoLoadGatewayPort,
                request.Settings.AutoLoadGatewayPolicy,
                request.Settings.RequireApiKeyAuth,
                request.Settings.EnableMetrics,
                request.Settings.GpuMode,
                request.Settings.CudaPackagePreference
            },
            hardware = new
            {
                gpu = RedactDiagnosticText(request.GpuSummary, request),
                cpu = RedactDiagnosticText(request.CpuSummary, request)
            },
            wsl = new
            {
                request.Wsl.WslExeFound,
                request.Wsl.WslWorking,
                request.Wsl.Status,
                request.Wsl.DefaultDistro,
                request.Wsl.RecommendedDistro,
                distros = request.Wsl.Distros.Select(distro => new
                {
                    distro.Name,
                    distro.State,
                    distro.Version,
                    distro.IsDefault,
                    distro.IsUbuntu
                })
            },
            models = request.Models.Select(model => new
            {
                model.Id,
                model.Name,
                file = Path.GetFileName(model.ModelPath),
                ownership = model.Ownership.ToString(),
                available = File.Exists(model.ModelPath),
                sizeBytes = ExistingFileSize(model.ModelPath)
            }),
            runtimes = request.Runtimes.Select(runtime =>
            {
                var provenance = RuntimeInstallationVerificationService.Describe(runtime);
                return new
                {
                    runtime.Id,
                    runtime.Name,
                    mode = runtime.Mode.ToString(),
                    backend = runtime.Backend.ToString(),
                    executable = Path.GetFileName(runtime.ExecutablePath),
                    available = File.Exists(runtime.ExecutablePath),
                    provenance.IsManaged,
                    provenance.TrustStatus,
                    provenance.Provider,
                    provenance.Repository,
                    provenance.ReleaseTag,
                    provenance.RuntimeVersion,
                    provenance.CanReverify
                };
            }),
            sessions = request.Sessions.Select(session => new
            {
                session.SessionId,
                session.ModelId,
                session.ModelName,
                session.RuntimeId,
                session.RuntimeName,
                mode = session.Mode.ToString(),
                backend = session.Backend.ToString(),
                status = session.Status.ToString(),
                endpointHealth = session.EndpointHealth.ToString(),
                session.IsRunning,
                session.StartedAt,
                session.StoppedAt,
                session.ProcessId
            }),
            jobs = request.Jobs.Select(job => new
            {
                job.Id,
                job.Kind,
                status = job.Status.ToString(),
                job.CreatedAt,
                job.UpdatedAt
            })
        };

    private static string[] RecentLogs(string logsDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(logsDirectory) || !Directory.Exists(logsDirectory)) return [];
            return Directory.EnumerateFiles(logsDirectory, "*.log", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            })
                .Select(path => new FileInfo(path))
                .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(MaximumLogCount)
                .Select(file => file.FullName)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string RedactDiagnosticText(string text, DiagnosticsBundleRequest request)
    {
        var redacted = LogFileService.RedactSensitiveText(text, request.Settings.ModelApiKey);
        redacted = ReplacePath(redacted, request.Settings.WorkspaceRoot, "[workspace]");
        redacted = ReplacePath(redacted, request.Settings.ModelsRoot, "[models]");
        redacted = ReplacePath(redacted, request.Settings.RuntimeRoot, "[runtimes]");
        redacted = ReplacePath(redacted, request.Settings.CacheRoot, "[cache]");
        foreach (var model in request.Models)
            redacted = ReplacePath(redacted, model.ModelPath, $"[model]/{Path.GetFileName(model.ModelPath)}");
        foreach (var runtime in request.Runtimes)
            redacted = ReplacePath(redacted, runtime.ExecutablePath, $"[runtime]/{Path.GetFileName(runtime.ExecutablePath)}");

        redacted = Regex.Replace(
            redacted,
            "(?i)(\\\"?(?:apiKey|modelApiKey|controlToken|token)\\\"?\\s*[:=]\\s*\\\"?)[^\\\"\\s,}]+",
            "$1[redacted]");
        return redacted;
    }

    private static string ReplacePath(string text, string path, string replacement)
    {
        if (string.IsNullOrWhiteSpace(path)) return text;
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return text;
        }

        if (fullPath.Length < 4) return text;
        var result = text.Replace(fullPath, replacement, StringComparison.OrdinalIgnoreCase);
        return result.Replace(fullPath.Replace('\\', '/'), replacement, StringComparison.OrdinalIgnoreCase);
    }

    private static long? ExistingFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch
        {
            return null;
        }
    }

    private static string UniqueArchivePath(string outputDirectory, DateTimeOffset timestamp)
    {
        var stem = $"llwm-diagnostics-{timestamp:yyyyMMdd-HHmmss}";
        for (var suffix = 0; suffix < 1000; suffix++)
        {
            var name = suffix == 0 ? $"{stem}.zip" : $"{stem}-{suffix}.zip";
            var candidate = Path.Combine(outputDirectory, name);
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(outputDirectory, $"{stem}-{Guid.NewGuid():N}.zip");
    }

    private static string SafeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string((fileName ?? "log.log").Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "log.log" : safe;
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string entryName,
        string text,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(text.AsMemory(), cancellationToken);
    }
}
