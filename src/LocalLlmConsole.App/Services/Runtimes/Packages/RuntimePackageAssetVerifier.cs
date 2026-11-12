namespace LocalLlmConsole.Services;

public static class RuntimePackageChecksumStatus
{
    /// <summary>Every downloaded asset matched a published SHA-256 digest.</summary>
    public const string Verified = "verified";

    /// <summary>The release published no SHA-256 metadata and the user chose to install without it.</summary>
    public const string SkippedByUser = "skipped-by-user";

    public static string Combine(IEnumerable<string> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return statuses.All(status => status == Verified) ? Verified : SkippedByUser;
    }

    public static string Describe(string status)
        => status switch
        {
            Verified => "Verified against the published SHA-256 digest",
            SkippedByUser => "Not verified: the release published no SHA-256 digest and installation without verification was confirmed",
            _ => status
        };
}

public static class RuntimePackageAssetVerifier
{
    /// <summary>Verifies size and SHA-256 and returns the resulting <see cref="RuntimePackageChecksumStatus"/>.</summary>
    public static async Task<string> VerifyAsync(
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
            return RuntimePackageChecksumStatus.SkippedByUser;
        }

        var actual = await ComputeSha256Async(assetPath, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)))
            throw new InvalidOperationException($"Runtime package checksum mismatch for {asset.Name}. Expected SHA-256 {expected}, found {actual}.");
        return RuntimePackageChecksumStatus.Verified;
    }

    public static string ExtractSha256(string checksumText, string assetName = "")
        => Sha256ChecksumFile.FindSha256(checksumText, assetName);

    public static string NormalizeSha256(string value)
        => Sha256Digest.NormalizeHexOrAlgorithmPrefix(value);

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
