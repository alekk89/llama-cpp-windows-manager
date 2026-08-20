namespace LocalLlmConsole.Services;

public static class RuntimePackageAssetVerifier
{
    public static async Task VerifyAsync(
        HttpClient client,
        RuntimePackageAsset asset,
        string assetPath,
        bool requireChecksum,
        CancellationToken cancellationToken = default)
    {
        VerifySize(asset, assetPath);

        var expected = NormalizeSha256(asset.Sha256);
        if (!string.IsNullOrWhiteSpace(asset.Sha256) && string.IsNullOrWhiteSpace(expected))
            throw new InvalidOperationException($"Runtime package asset {asset.Name} includes an invalid SHA-256 digest.");

        if (string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(asset.ChecksumUrl))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.ChecksumUrl);
            request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("LocalLlmConsole", "1.0"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            expected = ExtractSha256(await response.Content.ReadAsStringAsync(cancellationToken), asset.Name);
            if (string.IsNullOrWhiteSpace(expected))
                throw new InvalidOperationException($"The SHA-256 companion file does not contain a checksum for {asset.Name}.");
        }

        if (string.IsNullOrWhiteSpace(expected))
        {
            if (requireChecksum)
                throw new InvalidOperationException($"Runtime package asset {asset.Name} is missing SHA-256 verification metadata.");
            return;
        }

        var actual = await ComputeSha256Async(assetPath, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)))
            throw new InvalidOperationException($"Runtime package checksum mismatch for {asset.Name}. Expected SHA-256 {expected}, found {actual}.");
    }

    public static string ExtractSha256(string checksumText, string assetName = "")
    {
        foreach (var line in (checksumText ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line, @"(?i)\b(?:sha256:)?(?<hash>[a-f0-9]{64})\b");
            if (!match.Success) continue;
            if (string.IsNullOrWhiteSpace(assetName) || line.Contains(assetName, StringComparison.OrdinalIgnoreCase))
                return match.Groups["hash"].Value.ToLowerInvariant();
        }

        return "";
    }

    public static string NormalizeSha256(string value)
    {
        var text = (value ?? "").Trim();
        if (text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            text = text["sha256:".Length..];
        return Regex.IsMatch(text, @"\A[a-fA-F0-9]{64}\z")
            ? text.ToLowerInvariant()
            : "";
    }

    private static void VerifySize(RuntimePackageAsset asset, string assetPath)
    {
        if (asset.SizeBytes <= 0) return;
        var actual = new FileInfo(assetPath).Length;
        if (actual != asset.SizeBytes)
            throw new InvalidOperationException($"Runtime package size mismatch for {asset.Name}. Expected {asset.SizeBytes:N0} bytes, found {actual:N0} bytes.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        => await FileSystemSafetyService.Sha256Async(path, cancellationToken);
}
