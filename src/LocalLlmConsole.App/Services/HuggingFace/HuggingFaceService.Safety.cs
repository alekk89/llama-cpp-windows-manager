namespace LocalLlmConsole.Services;

public sealed partial class HuggingFaceService
{
    private static bool ShouldPersistDownloadProgress(long downloaded, long lastPersistedBytes, DateTimeOffset lastPersistedAt, DateTimeOffset now)
        => downloaded > lastPersistedBytes
            && (downloaded - lastPersistedBytes >= DownloadProgressUpdateBytes
                || now - lastPersistedAt >= DownloadProgressUpdateInterval);

    private async Task CompletePartialDownloadAsync(JobRecord job, HuggingFaceFile file, AppSettings settings, string destination, string partial, long total, CancellationToken cancellationToken)
    {
        if (!File.Exists(partial)) throw new FileNotFoundException("The partial download file is missing.", partial);
        RejectUnsafeExistingFile(partial, "partial download");
        var expectedBytes = ExpectedBytes(file, total);
        var verificationError = await VerifyDownloadedFileAsync(partial, file, expectedBytes, cancellationToken);
        if (!string.IsNullOrWhiteSpace(verificationError))
            throw new InvalidOperationException(verificationError);

        if (File.Exists(destination))
        {
            RejectUnsafeExistingFile(destination, "downloaded model");
            throw new InvalidOperationException("The destination model file already exists. Refusing to overwrite it.");
        }
        File.Move(partial, destination);
        var finalBytes = new FileInfo(destination).Length;
        if (expectedBytes > 0 && finalBytes != expectedBytes)
            throw new InvalidOperationException($"Downloaded file size mismatch after finalization. Expected {expectedBytes:N0} bytes, found {finalBytes:N0} bytes.");

        await CompleteVerifiedPrimaryModelAsync(job, settings, file, destination, expectedBytes > 0 ? expectedBytes : finalBytes, DateTimeOffset.UtcNow, recovered: false, cancellationToken);
    }

    private async Task CompleteVerifiedPrimaryModelAsync(
        JobRecord job,
        AppSettings settings,
        HuggingFaceFile file,
        string destination,
        long completedBytes,
        DateTimeOffset timestamp,
        bool recovered,
        CancellationToken cancellationToken)
    {
        await RegisterDownloadedHuggingFaceModelAsync(settings, file, destination, timestamp, recovered, new VisionProjectorDownloadResult("", ""));
        await _jobs.UpdateAsync(job, JobStatus.Completed, JsonSerializer.Serialize(new DownloadJobPayload(file, destination, completedBytes, completedBytes), JsonOptions), cancellationToken);

        try
        {
            var projector = await TryDownloadVisionProjectorAsync(settings, file, destination, cancellationToken);
            var suggestedProfile = await TryGetSuggestedLaunchProfileAsync(settings, file, cancellationToken);
            if (!string.IsNullOrWhiteSpace(projector.LocalPath) || !string.IsNullOrWhiteSpace(projector.Error) || suggestedProfile is not null)
            {
                await RegisterDownloadedHuggingFaceModelAsync(settings, file, destination, timestamp, recovered, projector, suggestedProfile);
                if (!string.IsNullOrWhiteSpace(projector.Error))
                    await _jobs.UpdateAsync(job, JobStatus.Completed, JsonSerializer.Serialize(new DownloadJobPayload(file, destination, completedBytes, completedBytes, projector.Error), JsonOptions), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // The primary model is already registered and complete; app shutdown can skip optional enrichment safely.
        }
        catch (Exception ex)
        {
            await _jobs.UpdateAsync(job, JobStatus.Completed, JsonSerializer.Serialize(new DownloadJobPayload(file, destination, completedBytes, completedBytes, $"Optional post-download setup skipped: {ex.Message}"), JsonOptions), CancellationToken.None);
        }
    }

    private async Task<ModelRecord> RegisterDownloadedHuggingFaceModelAsync(
        AppSettings settings,
        HuggingFaceFile file,
        string destination,
        DateTimeOffset timestamp,
        bool recovered,
        VisionProjectorDownloadResult projector,
        SuggestedLaunchProfile? suggestedProfile = null)
    {
        var record = await _catalog.RegisterDownloadedAsync(settings.ModelsRoot, file.Name, destination, JsonSerializer.Serialize(new
        {
            file.Repo,
            file.Path,
            sourceRepo = file.Repo,
            sourceFile = file.Path,
            file.PipelineTag,
            file.LibraryName,
            file.Tags,
            file.Revision,
            file.Sha256,
            file.License,
            file.HasVisionProjector,
            file.VisionProjectorPath,
            file.VisionProjectorName,
            file.VisionProjectorSizeBytes,
            file.VisionProjectorSha256,
            visionProjectorLocalPath = projector.LocalPath,
            visionProjectorDownloadError = projector.Error,
            file.HasConfig,
            file.HasTokenizer,
            file.HasAdapter,
            file.HasDraftOrMtp,
            file.CapabilityHints,
            suggestedLaunchSettingsSource = suggestedProfile?.Source,
            suggestedLaunchSettings = suggestedProfile?.Settings,
            downloadedAt = recovered ? (DateTimeOffset?)null : timestamp,
            recoveredAt = recovered ? timestamp : (DateTimeOffset?)null
        }, JsonOptions));

        if (suggestedProfile is not null && await _store.GetModelLaunchSettingsAsync(record.Id) is null)
            await _store.SaveModelLaunchSettingsAsync(record.Id, suggestedProfile.Settings);

        return record;
    }

    private static void TryDelete(string path)
    {
        try
        {
            RejectUnsafeExistingFile(path, "partial download");
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not delete Hugging Face partial download {path}: {ex.Message}");
        }
    }

    private static long ExpectedBytes(HuggingFaceFile file, long total)
        => total > 0 ? total : file.SizeBytes;

    private static async Task<string> VerifyDownloadedFileAsync(
        string path,
        HuggingFaceFile file,
        long expectedBytes,
        CancellationToken cancellationToken)
    {
        var expectedSha256 = NormalizeSha256(file.Sha256);
        if (expectedBytes <= 0 && string.IsNullOrWhiteSpace(expectedSha256))
            return "Downloaded file is missing both expected size and SHA-256 metadata.";

        if (expectedBytes > 0)
        {
            var bytes = new FileInfo(path).Length;
            if (bytes != expectedBytes)
                return $"Downloaded file size mismatch. Expected {expectedBytes:N0} bytes, found {bytes:N0} bytes.";
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actual = await ComputeSha256Async(path, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedSha256), Convert.FromHexString(actual)))
                return $"Downloaded file checksum mismatch. Expected SHA-256 {expectedSha256}, found {actual}.";
        }

        return "";
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        => await FileSystemSafetyService.Sha256Async(path, cancellationToken);

    private static string NormalizeSha256(string value)
    {
        var normalized = new string((value ?? "").Trim().Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        return normalized.Length == 64 ? normalized : "";
    }

    private static string ExtractLicense(IEnumerable<string> tags)
    {
        const string prefix = "license:";
        return tags.FirstOrDefault(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..] ?? "";
    }

    private static void ValidateHuggingFaceFile(HuggingFaceFile file)
    {
        var name = file.Name ?? "";
        var repo = file.Repo ?? "";
        var path = file.Path ?? "";
        var repoParts = repo.Split('/');
        if (repoParts.Length != 2 || repoParts.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Invalid Hugging Face repository id.");
        if (repo.Contains("..", StringComparison.Ordinal) || repo.Contains('\\') || repo.Contains('?') || repo.Contains('#'))
            throw new InvalidOperationException("Invalid Hugging Face repository id.");
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid Hugging Face model path.");
        if (path.Contains('\\') || path.Contains('?') || path.Contains('#') || path.Any(char.IsControl))
            throw new InvalidOperationException("Invalid Hugging Face model path.");
        if (path.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new InvalidOperationException("Invalid Hugging Face model path.");
        if (!name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only GGUF model downloads are supported.");
        ValidateLocalModelFileName(name);
        if (!string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Model filename metadata does not match the download path.");

        if (string.IsNullOrWhiteSpace(file.VisionProjectorPath)) return;
        var projectorName = string.IsNullOrWhiteSpace(file.VisionProjectorName)
            ? Path.GetFileName(file.VisionProjectorPath)
            : file.VisionProjectorName;
        if (!file.VisionProjectorPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
            || !IsVisionProjectorFile(file.VisionProjectorPath))
            throw new InvalidOperationException("Invalid Hugging Face vision projector path.");
        if (file.VisionProjectorPath.StartsWith("/", StringComparison.Ordinal) || file.VisionProjectorPath.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid Hugging Face vision projector path.");
        if (file.VisionProjectorPath.Contains('\\') || file.VisionProjectorPath.Contains('?') || file.VisionProjectorPath.Contains('#') || file.VisionProjectorPath.Any(char.IsControl))
            throw new InvalidOperationException("Invalid Hugging Face vision projector path.");
        if (file.VisionProjectorPath.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new InvalidOperationException("Invalid Hugging Face vision projector path.");
        ValidateLocalModelFileName(projectorName);
        if (!string.Equals(Path.GetFileName(file.VisionProjectorPath), projectorName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Vision projector filename metadata does not match the projector path.");
    }

    private static void ValidateLocalModelFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Invalid Hugging Face model filename.");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(char.IsControl))
            throw new InvalidOperationException("Model filename contains characters that are not safe on Windows.");
        if (name.StartsWith("-", StringComparison.Ordinal))
            throw new InvalidOperationException("Model filename cannot begin with a dash.");
        if (name.EndsWith(" ", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal))
            throw new InvalidOperationException("Model filename cannot end with a space or dot.");
        var stem = Path.GetFileNameWithoutExtension(name);
        if (ReservedWindowsFileNames.Contains(stem))
            throw new InvalidOperationException("Model filename uses a reserved Windows device name.");
    }

    private static void EnsureDiskSpace(string destination, long bytesNeeded)
    {
        if (bytesNeeded <= 0) return;
        var root = Path.GetPathRoot(Path.GetFullPath(destination));
        if (string.IsNullOrWhiteSpace(root)) return;
        var drive = new DriveInfo(root);
        if (!drive.IsReady) return;
        if (drive.AvailableFreeSpace < bytesNeeded)
            throw new IOException($"Not enough free disk space for the download. Need {bytesNeeded:N0} bytes, available {drive.AvailableFreeSpace:N0} bytes.");
    }

    private static void EnsureDestinationInsideModelsRoot(string destination, string modelsRoot)
    {
        var root = Path.GetFullPath(modelsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(destination);
        var relative = Path.GetRelativePath(root, target);
        if (string.IsNullOrWhiteSpace(relative)
            || string.Equals(relative, ".", StringComparison.Ordinal)
            || string.Equals(relative, "..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Refusing to download outside the configured models folder.");

        RejectReparsePointAncestor(root, target);
    }

    private static void RejectReparsePointAncestor(string root, string target)
    {
        var current = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
        while (!string.IsNullOrWhiteSpace(current)
            && Path.GetRelativePath(root, current) is var relative
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative))
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Refusing to download through a symlink or junction inside the models folder.");
            if (string.Equals(Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
                return;
            current = Path.GetDirectoryName(current);
        }
    }

    private static FileStream OpenSafePartialForWrite(string partial, bool append)
    {
        if (File.Exists(partial))
        {
            RejectUnsafeExistingFile(partial, "partial download");
            return new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        }

        return new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }

    private static void RejectUnsafeExistingFile(string path, string label)
    {
        if (!File.Exists(path)) return;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Refusing to use {label} because it is a symlink or junction: {path}");
        if (OperatingSystem.IsWindows()
            && TryGetHardLinkCount(path, out var linkCount)
            && linkCount > 1)
            throw new InvalidOperationException($"Refusing to use {label} because it has multiple hard links: {path}");
    }

    private static bool TryGetHardLinkCount(string path, out uint linkCount)
    {
        linkCount = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info)) return false;
            linkCount = info.NumberOfLinks;
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    public static DownloadJobPayload? ParseDownloadPayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            var node = JsonNode.Parse(payloadJson);
            if (node is null) return null;
            if (node["file"] is not null || node["File"] is not null)
                return node.Deserialize<DownloadJobPayload>(JsonOptions);

            var file = node.Deserialize<HuggingFaceFile>(JsonOptions);
            return file is null ? null : new DownloadJobPayload(file, "");
        }
        catch
        {
            return null;
        }
    }
}
