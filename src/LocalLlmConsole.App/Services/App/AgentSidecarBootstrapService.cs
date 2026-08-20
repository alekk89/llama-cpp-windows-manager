using System.IO.Compression;

namespace LocalLlmConsole.Services;

public enum AgentSidecarBootstrapStatus
{
    BundleUnavailable,
    Current,
    Installed,
    Failed
}

public sealed record AgentSidecarBootstrapResult(
    AgentSidecarBootstrapStatus Status,
    IReadOnlyList<string> InstalledFiles,
    IReadOnlyList<string> CurrentFiles,
    string? Error = null);

public sealed class AgentSidecarBootstrapService
{
    public const string ResourceName = "LocalLlmConsole.AgentBootstrap.zip";

    private const long MaximumBundleFileSize = 256L * 1024 * 1024;
    private static readonly string[] RequiredPaths =
    [
        "llwmctl.exe",
        "AGENTS.md",
        "agent.md",
        "docs/CONTROL_API.md",
        "LICENSE",
        "THIRD-PARTY-NOTICES.md",
        "licenses/Apache-2.0.txt",
        "licenses/dotnet/LICENSE.txt",
        "licenses/dotnet/ThirdPartyNotices.txt"
    ];

    private static readonly HashSet<string> AllowedPaths = new(RequiredPaths, StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static int VerificationExitCode(AgentSidecarBootstrapStatus status)
        => status is AgentSidecarBootstrapStatus.Current or AgentSidecarBootstrapStatus.Installed ? 0 : 1;

    public AgentSidecarBootstrapResult InstallEmbedded(
        Assembly assembly,
        string targetRoot,
        bool verifyBundleContents = false)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        using var bundle = assembly.GetManifestResourceStream(ResourceName);
        return bundle is null
            ? new AgentSidecarBootstrapResult(AgentSidecarBootstrapStatus.BundleUnavailable, [], [])
            : Install(bundle, targetRoot, verifyBundleContents);
    }

    public AgentSidecarBootstrapResult Install(
        Stream bundle,
        string targetRoot,
        bool verifyBundleContents = false)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);

        string? stagingRoot = null;
        var installed = new List<string>();
        var current = new List<string>();

        try
        {
            var normalizedRoot = Path.GetFullPath(targetRoot);
            Directory.CreateDirectory(normalizedRoot);

            using var archive = new ZipArchive(bundle, ZipArchiveMode.Read, leaveOpen: true);
            var entries = ValidateArchive(archive);
            var manifest = ReadManifest(entries["manifest.json"]);
            var manifestFiles = ValidateManifest(manifest);
            var currentTargets = CurrentTargets(normalizedRoot, manifestFiles);
            if (!verifyBundleContents && currentTargets.Count == RequiredPaths.Length)
                return new AgentSidecarBootstrapResult(AgentSidecarBootstrapStatus.Current, [], RequiredPaths);

            stagingRoot = Path.Combine(normalizedRoot, $".llwm-sidecars-{Environment.ProcessId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingRoot);

            foreach (var relativePath in RequiredPaths)
            {
                var file = manifestFiles[relativePath];
                var stagedPath = ResolveContainedPath(stagingRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);

                if (!entries.TryGetValue(relativePath, out var entry))
                {
                    throw new InvalidDataException($"Embedded sidecar archive is missing '{relativePath}'.");
                }
                if (entry.Length != file.Size)
                {
                    throw new InvalidDataException($"Embedded sidecar '{relativePath}' has an unexpected size.");
                }

                using var input = entry.Open();
                using var output = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var stagedHash = CopyAndComputeSha256(input, output);
                if (!string.Equals(stagedHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Embedded sidecar '{relativePath}' failed SHA-256 validation.");
                }
            }

            foreach (var relativePath in RequiredPaths)
            {
                var stagedPath = ResolveContainedPath(stagingRoot, relativePath);
                var targetPath = ResolveContainedPath(normalizedRoot, relativePath);
                var expectedHash = manifestFiles[relativePath].Sha256!;

                if (currentTargets.Contains(relativePath))
                {
                    current.Add(relativePath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                var temporaryTarget = targetPath + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
                try
                {
                    File.Copy(stagedPath, temporaryTarget, overwrite: false);
                    if (!string.Equals(ComputeSha256(temporaryTarget), expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException($"Sidecar copy validation failed for '{relativePath}'.");
                    }

                    File.Move(temporaryTarget, targetPath, overwrite: true);
                    installed.Add(relativePath);
                }
                finally
                {
                    if (File.Exists(temporaryTarget))
                    {
                        File.Delete(temporaryTarget);
                    }
                }
            }

            var status = installed.Count == 0
                ? AgentSidecarBootstrapStatus.Current
                : AgentSidecarBootstrapStatus.Installed;
            return new AgentSidecarBootstrapResult(status, installed, current);
        }
        catch (Exception ex)
        {
            return new AgentSidecarBootstrapResult(AgentSidecarBootstrapStatus.Failed, installed, current, ex.Message);
        }
        finally
        {
            if (stagingRoot is not null && Directory.Exists(stagingRoot))
            {
                try
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"Could not remove agent-sidecar staging directory '{stagingRoot}': {ex.Message}");
                }
            }
        }
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateArchive(ZipArchive archive)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var relativePath = NormalizeRelativePath(entry.FullName);
            if (entry.Name.Length == 0)
            {
                if (!string.Equals(relativePath, "docs", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Embedded sidecar archive contains unexpected directory '{entry.FullName}'.");
                }

                continue;
            }

            if (!string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase) && !AllowedPaths.Contains(relativePath))
            {
                throw new InvalidDataException($"Embedded sidecar archive contains unexpected path '{entry.FullName}'.");
            }

            if (entry.Length > MaximumBundleFileSize)
            {
                throw new InvalidDataException($"Embedded sidecar '{relativePath}' exceeds the size limit.");
            }

            if (!entries.TryAdd(relativePath, entry))
            {
                throw new InvalidDataException($"Embedded sidecar archive contains duplicate path '{relativePath}'.");
            }
        }

        if (!entries.ContainsKey("manifest.json"))
        {
            throw new InvalidDataException("Embedded sidecar archive has no manifest.json.");
        }

        return entries;
    }

    private static AgentSidecarManifest ReadManifest(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<AgentSidecarManifest>(stream, ManifestJsonOptions);
        return manifest ?? throw new InvalidDataException("Embedded sidecar manifest is empty.");
    }

    private static Dictionary<string, AgentSidecarManifestFile> ValidateManifest(AgentSidecarManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidDataException("Embedded sidecar manifest has no version.");
        }

        if (manifest.Files is null || manifest.Files.Count != RequiredPaths.Length)
        {
            throw new InvalidDataException("Embedded sidecar manifest does not contain every required file.");
        }

        var files = new Dictionary<string, AgentSidecarManifestFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (file is null)
            {
                throw new InvalidDataException("Embedded sidecar manifest contains an empty file record.");
            }

            var relativePath = NormalizeRelativePath(file.Path ?? "");
            if (!AllowedPaths.Contains(relativePath) || !files.TryAdd(relativePath, file))
            {
                throw new InvalidDataException($"Embedded sidecar manifest contains invalid path '{file.Path}'.");
            }

            if (file.Size < 0 || file.Size > MaximumBundleFileSize)
            {
                throw new InvalidDataException($"Embedded sidecar manifest has an invalid size for '{relativePath}'.");
            }

            if (file.Sha256 is null || file.Sha256.Length != 64 || file.Sha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException($"Embedded sidecar manifest has an invalid SHA-256 for '{relativePath}'.");
            }
        }

        if (RequiredPaths.Any(required => !files.ContainsKey(required)))
        {
            throw new InvalidDataException("Embedded sidecar manifest does not contain every required file.");
        }

        return files;
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("Embedded sidecar archive contains an empty path.");
        }

        var normalized = path.Replace('\\', '/').TrimEnd('/');
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"Embedded sidecar archive contains unsafe path '{path}'.");
        }

        return normalized;
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Embedded sidecar path escapes the target directory: '{relativePath}'.");
        }

        return resolved;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static HashSet<string> CurrentTargets(
        string root,
        IReadOnlyDictionary<string, AgentSidecarManifestFile> manifestFiles)
    {
        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in RequiredPaths)
        {
            var targetPath = ResolveContainedPath(root, relativePath);
            var expected = manifestFiles[relativePath];
            if (File.Exists(targetPath)
                && new FileInfo(targetPath).Length == expected.Size
                && string.Equals(ComputeSha256(targetPath), expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                current.Add(relativePath);
            }
        }
        return current;
    }

    private static string CopyAndComputeSha256(Stream input, Stream output)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
        }
        output.Flush();
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private sealed record AgentSidecarManifest(string? Version, IReadOnlyList<AgentSidecarManifestFile?>? Files);
    private sealed record AgentSidecarManifestFile(string? Path, long Size, string? Sha256);
}
