namespace LocalLlmConsole.Services;

public sealed partial class HuggingFaceService
{
    private async Task<VisionProjectorDownloadResult> TryDownloadVisionProjectorAsync(AppSettings settings, HuggingFaceFile file, string modelDestination, CancellationToken cancellationToken)
    {
        var projector = VisionProjectorFile(file);
        if (projector is null) return new VisionProjectorDownloadResult("", "");

        var modelDir = Path.GetDirectoryName(modelDestination) ?? settings.ModelsRoot;
        var destination = Path.GetFullPath(Path.Combine(modelDir, projector.Name));
        var partial = destination + ".partial";
        try
        {
            EnsureDestinationInsideModelsRoot(destination, settings.ModelsRoot);
            if (string.Equals(Path.GetFullPath(modelDestination), destination, StringComparison.OrdinalIgnoreCase))
                return new VisionProjectorDownloadResult("", "Vision projector metadata pointed at the primary model file; skipped projector download.");
            if (File.Exists(destination))
            {
                RejectUnsafeExistingFile(destination, "vision projector");
                var existingError = await VerifyDownloadedFileAsync(
                    destination,
                    projector,
                    ExpectedBytes(projector, projector.SizeBytes),
                    cancellationToken);
                return string.IsNullOrWhiteSpace(existingError)
                    ? new VisionProjectorDownloadResult(destination, "")
                    : new VisionProjectorDownloadResult("", $"Existing vision projector did not verify: {existingError}");
            }
            if (projector.SizeBytes <= 0 && string.IsNullOrWhiteSpace(projector.Sha256))
                return new VisionProjectorDownloadResult("", "Vision projector metadata did not include size or SHA-256, so automatic projector download was skipped.");

            Directory.CreateDirectory(modelDir);
            EnsureDiskSpace(destination, projector.SizeBytes);
            if (File.Exists(partial)) RejectUnsafeExistingFile(partial, "partial vision projector download");

            using var request = new HttpRequestMessage(HttpMethod.Get, ResolveUrl(projector.Repo, projector.Path, projector.Revision));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength.GetValueOrDefault();
            EnsureDiskSpace(destination, total);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = OpenSafePartialForWrite(partial, append: false))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            var verificationError = await VerifyDownloadedFileAsync(
                partial,
                projector,
                ExpectedBytes(projector, total),
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(verificationError))
                return new VisionProjectorDownloadResult("", $"Vision projector did not verify: {verificationError}");
            File.Move(partial, destination);
            return new VisionProjectorDownloadResult(destination, "");
        }
        catch (Exception ex)
        {
            return new VisionProjectorDownloadResult("", $"Vision projector download skipped: {ex.Message}");
        }
        finally
        {
            if (File.Exists(partial)) TryDelete(partial);
        }
    }

    private static HuggingFaceFile? VisionProjectorFile(HuggingFaceFile file)
    {
        if (string.IsNullOrWhiteSpace(file.VisionProjectorPath)) return null;
        var name = string.IsNullOrWhiteSpace(file.VisionProjectorName)
            ? Path.GetFileName(file.VisionProjectorPath)
            : file.VisionProjectorName;
        if (string.IsNullOrWhiteSpace(name)) return null;
        return new HuggingFaceFile(
            file.Repo,
            file.VisionProjectorPath,
            name,
            "",
            file.VisionProjectorSizeBytes,
            file.Downloads,
            Revision: file.Revision,
            Sha256: file.VisionProjectorSha256);
    }
}
