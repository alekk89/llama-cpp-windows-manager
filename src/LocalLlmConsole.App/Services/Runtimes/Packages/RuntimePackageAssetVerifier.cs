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
    private static readonly Regex BsdChecksumLine = new(
        @"^SHA-?256\s*\((?<name>.+)\)\s*=\s*(?<hash>[a-f0-9]{64})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex GnuChecksumLine = new(
        @"^(?:sha256:)?(?<hash>[a-f0-9]{64})(?:\s+\*?(?<name>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    /// <summary>
    /// Reads a SHA-256 digest from GNU (<c>hash  [*]name</c>), BSD (<c>SHA256 (name) = hash</c>),
    /// or single-digest companion files. Named entries must match the asset file name exactly;
    /// directory prefixes are ignored. A nameless digest is accepted only when it is the file's
    /// sole entry.
    /// </summary>
    public static string ExtractSha256(string checksumText, string assetName = "")
    {
        var entries = ParseChecksumEntries(checksumText).ToArray();
        if (string.IsNullOrWhiteSpace(assetName))
            return entries.FirstOrDefault().Hash ?? "";

        var expectedName = assetName.Trim();
        foreach (var (hash, name) in entries)
        {
            if (name is not null && string.Equals(FileNameOf(name), expectedName, StringComparison.OrdinalIgnoreCase))
                return hash;
        }

        return entries is [{ Name: null } sole] ? sole.Hash : "";
    }

    private static IEnumerable<(string Hash, string? Name)> ParseChecksumEntries(string checksumText)
    {
        foreach (var rawLine in (checksumText ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var match = BsdChecksumLine.Match(line);
            if (!match.Success) match = GnuChecksumLine.Match(line);
            if (!match.Success) continue;
            var name = match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : null;
            yield return (match.Groups["hash"].Value.ToLowerInvariant(), string.IsNullOrEmpty(name) ? null : name);
        }
    }

    private static string FileNameOf(string path)
    {
        var normalized = path.Replace('\\', '/');
        var separator = normalized.LastIndexOf('/');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

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
