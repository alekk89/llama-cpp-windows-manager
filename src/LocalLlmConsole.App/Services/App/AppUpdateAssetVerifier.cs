namespace LocalLlmConsole.Services;

public static class AppUpdateAssetVerifier
{
    public static async Task VerifyChecksumAssetAsync(
        HttpClient http,
        AppUpdateInfo update,
        string assetPath,
        CancellationToken cancellationToken)
    {
        var expected = NormalizeSha256(update.ExpectedSha256);
        if (!string.IsNullOrWhiteSpace(update.ExpectedSha256) && string.IsNullOrWhiteSpace(expected))
            throw AppUpdateVerificationException.Asset("The latest GitHub release includes an invalid SHA-256 checksum. Refusing to install without verification.");
        if (string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(update.ChecksumAssetUrl))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.ChecksumAssetUrl);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            expected = ExtractSha256(await response.Content.ReadAsStringAsync(cancellationToken), update.AssetName);
            if (string.IsNullOrWhiteSpace(expected))
                throw AppUpdateVerificationException.Asset($"The SHA-256 companion file does not contain a checksum for {update.AssetName}. Refusing to install without verification.");
        }

        if (string.IsNullOrWhiteSpace(expected))
            throw AppUpdateVerificationException.Asset("The latest GitHub release asset could not be verified.");

        var actual = await ComputeSha256Async(assetPath, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)))
            throw AppUpdateVerificationException.Asset($"Update checksum mismatch. Expected SHA-256 {expected}, found {actual}.");
    }

    public static string ExtractSha256(string checksumText, string assetName = "")
    {
        foreach (var line in (checksumText ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line, @"(?i)\b[a-f0-9]{64}\b");
            if (!match.Success) continue;
            if (string.IsNullOrWhiteSpace(assetName) || line.Contains(assetName, StringComparison.OrdinalIgnoreCase))
                return match.Value.ToLowerInvariant();
        }

        return "";
    }

    public static string NormalizeSha256(string value)
        => Sha256Digest.NormalizeLooseHex(value);

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        => await FileSystemSafetyService.Sha256Async(path, cancellationToken);
}
