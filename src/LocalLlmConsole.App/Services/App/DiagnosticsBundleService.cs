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
    string CpuSummary,
    IReadOnlyList<DiagnosticProbeRecord>? Probes = null,
    IReadOnlyList<SessionLifecycleDiagnosticEvent>? SessionEvents = null,
    BuildAndUpdateDiagnostics? BuildAndUpdate = null);

public sealed record DiagnosticsBundleResult(string ArchivePath, int IncludedLogCount);

public static class DiagnosticsBundleService
{
    private const int MaximumLogCount = 10;
    private const int MaximumLogCharacters = 256_000;
    private const int MaximumProbeCount = 32;
    private const int MaximumSessionEventCount = 200;
    private const int MaximumDiagnosticFieldCharacters = 4_096;

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
        await WriteJsonEntryAsync(archive, "probes.json", BuildProbes(request), cancellationToken);
        await WriteJsonEntryAsync(archive, "session-events.json", BuildSessionEvents(request), cancellationToken);
        await WriteJsonEntryAsync(archive, "build-and-update.json", BuildBuildAndUpdate(request), cancellationToken);

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
            schemaVersion = 2,
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
                request.Settings.GatewayAutoLoadModels,
                request.Settings.DirectModelAliasSuffix,
                request.Settings.SameModelLoadPolicy,
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

    private static object BuildProbes(DiagnosticsBundleRequest request)
        => new
        {
            schemaVersion = 1,
            maximumRecords = MaximumProbeCount,
            records = (request.Probes ?? [])
                .OrderByDescending(probe => probe.EndedAtUtc)
                .Take(MaximumProbeCount)
                .Select(probe => new
                {
                    name = SafeField(probe.Name, request),
                    version = SafeField(probe.Version, request),
                    provider = SafeField(probe.Provider, request),
                    probe.Attempted,
                    probe.StartedAtUtc,
                    probe.EndedAtUtc,
                    durationMilliseconds = Math.Max(0, Math.Min(300_000, (long)(probe.EndedAtUtc - probe.StartedAtUtc).TotalMilliseconds)),
                    classification = SafeField(probe.Classification, request),
                    exitCodeCategory = SafeField(probe.ExitCodeCategory, request),
                    parserResult = SafeField(probe.ParserResult, request),
                    standardOutputExcerpt = SafeField(probe.StandardOutputExcerpt, request),
                    standardErrorExcerpt = SafeField(probe.StandardErrorExcerpt, request),
                    capabilityFlags = (probe.CapabilityFlags ?? new Dictionary<string, bool>())
                        .Take(32)
                        .ToDictionary(pair => SafeField(pair.Key, request), pair => pair.Value, StringComparer.Ordinal),
                    toolVersion = SafeField(probe.ToolVersion, request)
                })
        };

    private static object BuildSessionEvents(DiagnosticsBundleRequest request)
        => new
        {
            schemaVersion = 1,
            maximumRecords = MaximumSessionEventCount,
            records = (request.SessionEvents ?? [])
                .OrderByDescending(item => item.TimestampUtc)
                .Take(MaximumSessionEventCount)
                .OrderBy(item => item.TimestampUtc)
                .Select(item => new
                {
                    sessionId = SafeIdentifier(item.SessionId),
                    modelId = SafeIdentifier(item.ModelId),
                    runtimeId = SafeIdentifier(item.RuntimeId),
                    previousState = SafeField(item.PreviousState, request),
                    newState = SafeField(item.NewState, request),
                    item.TimestampUtc,
                    initiatingActor = SafeField(item.InitiatingActor, request),
                    reasonCode = SafeField(item.ReasonCode, request),
                    processExitCategory = SafeField(item.ProcessExitCategory, request),
                    readinessResult = SafeField(item.ReadinessResult, request),
                    stopVerificationResult = SafeField(item.StopVerificationResult, request)
                })
        };

    private static object BuildBuildAndUpdate(DiagnosticsBundleRequest request)
    {
        var details = request.BuildAndUpdate ?? new BuildAndUpdateDiagnostics(
            BuildCommit: "unknown",
            ReleaseChannel: "development",
            InstallMode: "unknown",
            WindowsSignatureStatus: "unknown",
            ManifestVerificationStatus: "not-checked",
            LastUpdateCheckResult: "not-available");
        return new
        {
            schemaVersion = 1,
            applicationVersion = request.AppVersion,
            buildCommit = SafeField(details.BuildCommit, request),
            releaseChannel = SafeField(details.ReleaseChannel, request),
            installMode = SafeField(details.InstallMode, request),
            windowsSignatureStatus = SafeField(details.WindowsSignatureStatus, request),
            manifestVerificationStatus = SafeField(details.ManifestVerificationStatus, request),
            lastUpdateCheckResult = SafeField(details.LastUpdateCheckResult, request)
        };
    }

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
        redacted = Regex.Replace(redacted, @"(?i)(https?://)[^/\s:@]+:[^@\s/]+@", "$1[redacted]@");
        redacted = Regex.Replace(redacted, @"\\\\[A-Za-z0-9._-]+\\[A-Za-z0-9$._-]+(?:\\[^\s\""'<>|]+)*", "[unc-path]");
        redacted = Regex.Replace(redacted, @"(?i)\b[A-Z]:[\\/](?:[^\s\""'<>|]+)", "[path]");
        redacted = Regex.Replace(redacted, @"(?i)(?:/home/[^/\s]+|/mnt/[a-z]/Users/[^/\s]+)(?:/[^\s\""'<>|]+)*", "[path]");
        redacted = Regex.Replace(redacted, @"(?i)(--prompt(?:=|\s+))\S+", "$1[excluded]");
        redacted = Regex.Replace(redacted, "(?i)(\\\"(?:prompt|completion|messages?|requestBody)\\\"\\s*:\\s*\\\")[^\\\"]*", "$1[excluded]");
        return redacted;
    }

    private static string SafeField(string? value, DiagnosticsBundleRequest request)
    {
        var bounded = (value ?? "").Length <= MaximumDiagnosticFieldCharacters
            ? value ?? ""
            : (value ?? "")[..MaximumDiagnosticFieldCharacters];
        return RedactDiagnosticText(bounded, request);
    }

    private static string SafeIdentifier(string? value)
    {
        var bounded = (value ?? "").Length <= 128 ? value ?? "" : (value ?? "")[..128];
        return Regex.Replace(bounded, "[^A-Za-z0-9_.:-]", "_");
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

    private static async Task WriteJsonEntryAsync(
        ZipArchive archive,
        string entryName,
        object value,
        CancellationToken cancellationToken)
        => await WriteEntryAsync(
            archive,
            entryName,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
}
