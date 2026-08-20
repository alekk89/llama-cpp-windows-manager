namespace LocalLlmConsole.Services;

public enum RuntimeInstallationVerificationStatus
{
    Verified,
    Modified,
    Missing,
    NoManifest,
    UnverifiedCustomRuntime
}

public sealed record RuntimeInstallationVerificationResult(
    RuntimeInstallationVerificationStatus Status,
    string Summary,
    int VerifiedFiles,
    IReadOnlyList<string> Problems)
{
    public bool IsVerified => Status == RuntimeInstallationVerificationStatus.Verified;
}

public sealed record RuntimeProvenance(
    bool IsManaged,
    string TrustStatus,
    string Provider,
    string Repository,
    string ReleaseTag,
    string Assets,
    string SourceUrl,
    string Checksums,
    string DownloadedAt,
    string InstalledAt,
    string RuntimeVersion,
    bool CanReverify,
    string Details);

public static class RuntimeInstallationVerificationService
{
    private const string MetadataFileName = "local-llm-runtime.json";

    public static async Task StampManifestAsync(
        string runtimeFolder,
        JsonObject metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var fullRoot = Path.GetFullPath(runtimeFolder);
        var files = new JsonArray();
        foreach (var file in EnumerateInstallationFiles(fullRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            files.Add(new JsonObject
            {
                ["path"] = Path.GetRelativePath(fullRoot, file).Replace('\\', '/'),
                ["sizeBytes"] = info.Length,
                ["sha256"] = await Sha256Async(file, cancellationToken)
            });
        }

        metadata["installedFiles"] = files;
        metadata["manifestCreatedAt"] = DateTimeOffset.UtcNow.ToString("O");
        metadata["lastVerifiedAt"] = DateTimeOffset.UtcNow.ToString("O");
        metadata["lastVerificationStatus"] = "verified";
        metadata["lastVerificationMessage"] = $"Hash verified for {files.Count} installed files.";
    }

    public static async Task<RuntimeInstallationVerificationResult> VerifyAsync(
        RuntimeRecord runtime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var folder = RuntimeMetadataService.Folder(runtime);
        var metadataPath = Path.Combine(folder, MetadataFileName);
        var metadata = ReadMetadata(runtime, preferFile: true);
        var managed = IsManaged(metadata);
        if (!managed)
        {
            return new RuntimeInstallationVerificationResult(
                RuntimeInstallationVerificationStatus.UnverifiedCustomRuntime,
                "Unverified custom runtime. This installation is trusted by the user and was not supplied by the Manager.",
                0,
                []);
        }

        if (metadata?["installedFiles"] is not JsonArray manifest || manifest.Count == 0)
        {
            return new RuntimeInstallationVerificationResult(
                RuntimeInstallationVerificationStatus.NoManifest,
                "Managed runtime installed before file manifests were recorded. Reinstall it to enable hash re-verification.",
                0,
                []);
        }

        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var problems = new List<string>();
        var verified = 0;
        var missing = false;
        foreach (var item in manifest.OfType<JsonObject>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = item["path"]?.ToString() ?? "";
            var expectedHash = NormalizeSha256(item["sha256"]?.ToString());
            if (string.IsNullOrWhiteSpace(relative) || string.IsNullOrWhiteSpace(expectedHash))
            {
                problems.Add("The installation manifest contains an invalid file entry.");
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"Unsafe manifest path: {relative}");
                continue;
            }
            if (!File.Exists(fullPath))
            {
                missing = true;
                problems.Add($"Missing: {relative}");
                continue;
            }

            var expectedSize = item["sizeBytes"]?.GetValue<long>() ?? -1;
            if (expectedSize >= 0 && new FileInfo(fullPath).Length != expectedSize)
            {
                problems.Add($"Size changed: {relative}");
                continue;
            }
            var actualHash = await Sha256Async(fullPath, cancellationToken);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"Hash changed: {relative}");
                continue;
            }
            verified++;
        }

        var status = problems.Count == 0
            ? RuntimeInstallationVerificationStatus.Verified
            : missing ? RuntimeInstallationVerificationStatus.Missing : RuntimeInstallationVerificationStatus.Modified;
        var summary = status == RuntimeInstallationVerificationStatus.Verified
            ? $"Hash verified for {verified} installed files."
            : $"Verification found {problems.Count} problem(s) after checking {verified} unchanged files.";

        if (File.Exists(metadataPath))
        {
            metadata!["lastVerifiedAt"] = DateTimeOffset.UtcNow.ToString("O");
            metadata["lastVerificationStatus"] = status.ToString().ToLowerInvariant();
            metadata["lastVerificationMessage"] = summary;
            await File.WriteAllTextAsync(
                metadataPath,
                metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
        }

        return new RuntimeInstallationVerificationResult(status, summary, verified, problems);
    }

    public static RuntimeProvenance Describe(RuntimeRecord runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var metadata = ReadMetadata(runtime, preferFile: true);
        var managed = IsManaged(metadata);
        var assets = metadata?["assets"] as JsonArray;
        var assetNames = assets?.OfType<JsonObject>()
            .Select(asset => asset["name"]?.ToString() ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray() ?? [];
        var hashes = assets?.OfType<JsonObject>()
            .Select(asset => NormalizeSha256(asset["sha256"]?.ToString()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value[..Math.Min(12, value.Length)])
            .ToArray() ?? [];
        var hasManifest = metadata?["installedFiles"] is JsonArray manifest && manifest.Count > 0;
        var lastStatus = metadata?["lastVerificationStatus"]?.ToString() ?? "";
        var trustStatus = !managed
            ? "Unverified custom runtime"
            : lastStatus.Equals("modified", StringComparison.OrdinalIgnoreCase)
                ? "Local files modified"
                : lastStatus.Equals("missing", StringComparison.OrdinalIgnoreCase)
                    ? "Managed runtime files missing"
                    : hasManifest ? "Hash verified" : "Managed runtime; re-verification unavailable";
        var provider = metadata?["packageSource"]?.ToString()
            ?? metadata?["source"]?.ToString()
            ?? (managed ? "Managed source build" : "User supplied");
        var repository = metadata?["repoUrl"]?.ToString() ?? "";
        var sourceUrl = metadata?["releaseUrl"]?.ToString()
            ?? metadata?["sourceUrl"]?.ToString()
            ?? repository;
        var version = metadata?["runtimeVersion"]?.ToString()
            ?? metadata?["releaseTag"]?.ToString()
            ?? metadata?["commit"]?.ToString()
            ?? "";
        var installedAt = metadata?["managedInstalledAt"]?.ToString()
            ?? metadata?["registeredAt"]?.ToString()
            ?? "";
        var downloadedAt = metadata?["downloadedAt"]?.ToString()
            ?? metadata?["publishedAt"]?.ToString()
            ?? "";

        var detailLines = new List<string> { trustStatus };
        AddDetail(detailLines, "Provider", provider);
        AddDetail(detailLines, "Repository", repository);
        AddDetail(detailLines, "Release", metadata?["releaseTag"]?.ToString() ?? "");
        AddDetail(detailLines, "Assets", string.Join(", ", assetNames));
        AddDetail(detailLines, "Source", sourceUrl);
        AddDetail(detailLines, "SHA-256", string.Join(", ", hashes));
        AddDetail(detailLines, "Checksum status", metadata?["checksumStatus"]?.ToString() ?? "");
        AddDetail(detailLines, "Signature status", metadata?["signatureStatus"]?.ToString() ?? "");
        AddDetail(detailLines, "Downloaded", DisplayTimestamp(downloadedAt));
        AddDetail(detailLines, "Installed", DisplayTimestamp(installedAt));
        AddDetail(detailLines, "Backend", $"{runtime.Mode} {runtime.Backend}");
        AddDetail(detailLines, "Runtime version", version);
        AddDetail(detailLines, "Last verification", metadata?["lastVerifiedAt"]?.ToString() ?? "");

        return new RuntimeProvenance(
            managed,
            trustStatus,
            provider,
            repository,
            metadata?["releaseTag"]?.ToString() ?? "",
            string.Join(", ", assetNames),
            sourceUrl,
            string.Join(", ", hashes),
            downloadedAt,
            installedAt,
            version,
            managed && hasManifest,
            string.Join(Environment.NewLine, detailLines));
    }

    private static JsonObject? ReadMetadata(RuntimeRecord runtime, bool preferFile)
    {
        if (preferFile)
        {
            try
            {
                var path = Path.Combine(RuntimeMetadataService.Folder(runtime), MetadataFileName);
                if (File.Exists(path))
                    return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            }
            catch
            {
                // Fall back to the registered metadata snapshot.
            }
        }

        try
        {
            var registered = JsonNode.Parse(runtime.MetadataJson) as JsonObject;
            return registered?["runtimeMetadata"] as JsonObject ?? registered;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsManaged(JsonObject? metadata)
        => !string.IsNullOrWhiteSpace(metadata?["managedPackageId"]?.ToString())
           || !string.IsNullOrWhiteSpace(metadata?["managedPresetId"]?.ToString());

    private static IEnumerable<string> EnumerateInstallationFiles(string root)
        => Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).Where(file => !Path.GetFileName(file).Equals(MetadataFileName, StringComparison.OrdinalIgnoreCase));

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string NormalizeSha256(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit) ? normalized : "";
    }

    private static void AddDetail(ICollection<string> lines, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{label}: {value}");
    }

    private static string DisplayTimestamp(string value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : value;
}
