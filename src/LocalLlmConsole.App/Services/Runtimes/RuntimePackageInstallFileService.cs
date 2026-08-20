using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace LocalLlmConsole.Services;

public static class RuntimePackageInstallFileService
{
    public static string InstallDir(string runtimeRoot, RuntimePackageSelection selection)
        => Path.Combine(runtimeRoot, $"{selection.Preset.Id}-{ModelCatalogService.SafeId(selection.ReleaseTag)}");

    public static string DownloadCacheDir(string cacheRoot, RuntimePackageSelection selection)
        => Path.Combine(cacheRoot, "runtime-packages", selection.Preset.Id, ModelCatalogService.SafeId(selection.ReleaseTag));

    public static async Task DownloadAssetAsync(HttpClient client, RuntimePackageAsset asset, string destination, CancellationToken cancellationToken = default)
        => await DownloadAssetAsync(client, asset, destination, requireChecksum: true, cancellationToken);

    public static async Task DownloadAssetAsync(HttpClient client, RuntimePackageAsset asset, string destination, bool requireChecksum, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LocalLlmConsole", "1.0"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (asset.SizeBytes > 0
            && response.Content.Headers.ContentLength is { } contentLength
            && contentLength != asset.SizeBytes)
        {
            throw new InvalidOperationException($"Runtime package size mismatch for {asset.Name}. Expected {asset.SizeBytes:N0} bytes, server reported {contentLength:N0} bytes.");
        }

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(destination))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        try
        {
            await RuntimePackageAssetVerifier.VerifyAsync(client, asset, destination, requireChecksum, cancellationToken);
        }
        catch
        {
            try
            {
                File.Delete(destination);
            }
            catch (Exception deleteEx)
            {
                Trace.TraceWarning($"Could not delete failed runtime package download {destination}: {deleteEx.Message}");
            }
            throw;
        }
    }

    public static void ExtractArchive(string archivePath, string destination)
    {
        Directory.CreateDirectory(destination);
        var staging = Path.Combine(Path.GetTempPath(), $"llama-runtime-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            ExtractArchiveToDirectory(archivePath, staging);
            FlattenSingleTopLevelRuntimeDirectory(staging);
            MergeDirectory(staging, destination);
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only; extraction failure is reported by the caller.
            }
        }
    }

    public static string FindRuntimeExecutable(string installDir, RuntimeMode mode)
    {
        var preferredName = mode == RuntimeMode.Native ? "llama-server.exe" : "llama-server";
        var fallbackName = mode == RuntimeMode.Native ? "llama-server" : "llama-server.exe";
        var preferred = FindExecutableByName(installDir, preferredName);
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred;
        var fallback = FindExecutableByName(installDir, fallbackName);
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
        throw new InvalidOperationException("No llama-server executable was found in the extracted runtime package.");
    }

    public static string RuntimeFolderFromExecutable(string executablePath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(executablePath)) ?? "";
        if (Path.GetFileName(folder).Equals("bin", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(folder) ?? folder;
        return folder;
    }

    public static async Task StampManagedMetadataAsync(string runtimeFolder, string installRoot, RuntimePackageSelection selection, CancellationToken cancellationToken = default)
    {
        var metadata = new JsonObject
        {
            ["name"] = selection.Preset.Label,
            ["runtime"] = RuntimeBuildCatalogService.ModeKey(selection.Preset.Mode),
            ["backend"] = RuntimeBuildCatalogService.BackendKey(selection.Preset.Backend),
            ["managedPackageId"] = selection.Preset.Id,
            ["managedPresetId"] = selection.Preset.Id,
            ["sourcePresetId"] = selection.Preset.SourcePresetId,
            ["source"] = RuntimePackageSourceCatalog.PackageSourceKey(selection.Preset),
            ["packageSource"] = RuntimePackageSourceCatalog.PackageSourceLabel(selection.Preset),
            ["repoUrl"] = RuntimePackageSourceCatalog.RepositoryUrlFor(selection.Preset),
            ["releaseApiUrl"] = RuntimePackageSourceCatalog.ReleaseApiUrlFor(selection.Preset),
            ["releaseTag"] = selection.ReleaseTag,
            ["releaseUrl"] = selection.ReleaseUrl,
            ["publishedAt"] = selection.PublishedAt.ToString("O"),
            ["downloadedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["runtimeVersion"] = selection.ReleaseTag,
            ["checksumStatus"] = "verified",
            ["signatureStatus"] = "not-provided",
            ["installRoot"] = Path.GetFullPath(installRoot),
            ["managedInstalledAt"] = DateTimeOffset.UtcNow.ToString("O")
        };

        var assets = new JsonArray();
        foreach (var asset in selection.AllAssets)
        {
            assets.Add(new JsonObject
            {
                ["name"] = asset.Name,
                ["downloadUrl"] = asset.DownloadUrl,
                ["sizeBytes"] = asset.SizeBytes,
                ["sha256"] = asset.Sha256,
                ["checksumUrl"] = asset.ChecksumUrl
            });
        }
        metadata["assets"] = assets;

        await RuntimeInstallationVerificationService.StampManifestAsync(runtimeFolder, metadata, cancellationToken);

        Directory.CreateDirectory(runtimeFolder);
        await File.WriteAllTextAsync(
            Path.Combine(runtimeFolder, "local-llm-runtime.json"),
            metadata.ToJsonString(),
            cancellationToken);
    }

    private static void ExtractArchiveToDirectory(string archivePath, string destination)
    {
        var name = Path.GetFileName(archivePath);
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ArchiveSafetyService.ValidateZipArchiveEntries(archivePath, destination);
            ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
            return;
        }

        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            ArchiveSafetyService.ValidateTarGzipArchiveEntries(archivePath, destination);
            using var file = File.OpenRead(archivePath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
            return;
        }

        throw new InvalidOperationException($"Unsupported runtime archive format: {name}");
    }

    private static string? FindExecutableByName(string folder, string name)
    {
        var direct = Path.Combine(folder, name);
        if (File.Exists(direct)) return Path.GetFullPath(direct);
        var bin = Path.Combine(folder, "bin", name);
        if (File.Exists(bin)) return Path.GetFullPath(bin);
        return Directory.EnumerateFiles(folder, "llama-server*", SafeRecursiveEnumeration())
            .FirstOrDefault(file => Path.GetFileName(file).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static void FlattenSingleTopLevelRuntimeDirectory(string destination)
    {
        var files = Directory.EnumerateFiles(destination, "*", SearchOption.TopDirectoryOnly).ToArray();
        var directories = Directory.EnumerateDirectories(destination, "*", SearchOption.TopDirectoryOnly).ToArray();
        if (files.Length > 0 || directories.Length != 1) return;
        var child = directories[0];

        foreach (var childFile in Directory.EnumerateFiles(child, "*", SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(destination, Path.GetFileName(childFile));
            if (File.Exists(target)) File.Delete(target);
            File.Move(childFile, target);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(child, "*", SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(destination, Path.GetFileName(childDirectory));
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(childDirectory, target);
        }

        Directory.Delete(child, recursive: true);
    }

    private static void MergeDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            if (relative == ".") continue;
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static EnumerationOptions SafeRecursiveEnumeration() => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };
}
