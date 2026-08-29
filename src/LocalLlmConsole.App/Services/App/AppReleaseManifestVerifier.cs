namespace LocalLlmConsole.Services;

public sealed class AppReleaseManifestVerifier
{
    public const string ManifestAssetName = "release-manifest.json";
    public const string SignatureAssetName = "release-manifest.json.sig";
    private const int MaximumManifestBytes = 256 * 1024;
    private const int MaximumSignatureBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ReleaseManifestTrustStore _trustStore;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<string> _currentVersion;

    public AppReleaseManifestVerifier(
        HttpClient http,
        ReleaseManifestTrustStore trustStore,
        Func<DateTimeOffset>? utcNow = null,
        Func<string>? currentVersion = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _currentVersion = currentVersion ?? AppUpdateService.CurrentVersionLabel;
    }

    public async Task<VerifiedReleaseManifest> DownloadAndVerifyAsync(
        JsonObject release,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await DownloadAndVerifyCoreAsync(release, cancellationToken);
        }
        catch (AppUpdateVerificationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or CryptographicException or FormatException)
        {
            throw AppUpdateVerificationException.Manifest(ex.Message, ex);
        }
    }

    private async Task<VerifiedReleaseManifest> DownloadAndVerifyCoreAsync(
        JsonObject release,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (_trustStore.Count == 0)
            throw new InvalidOperationException("This build has no trusted stable-release manifest key. Automatic stable updates are disabled.");

        var manifestAsset = FindAsset(release, ManifestAssetName);
        var signatureAsset = FindAsset(release, SignatureAssetName);
        var manifestBytes = await DownloadBoundedAsync(manifestAsset.Url, MaximumManifestBytes, cancellationToken);
        var signatureBytes = await DownloadBoundedAsync(signatureAsset.Url, MaximumSignatureBytes, cancellationToken);
        var envelope = Deserialize<ReleaseManifestSignatureEnvelope>(signatureBytes, "release-manifest signature");
        ValidateEnvelope(envelope);
        if (!_trustStore.TryGetPublicKey(envelope.KeyId, out var publicKey))
            throw new InvalidOperationException($"Release manifest uses unknown signing key '{envelope.KeyId}'.");

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Release-manifest signature is not valid base64.", ex);
        }

        using (var ecdsa = ECDsa.Create())
        {
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out var consumed);
            if (consumed != publicKey.Length || ecdsa.KeySize != 256 ||
                !ecdsa.VerifyData(
                    manifestBytes,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence))
            {
                throw new InvalidOperationException("Release-manifest signature verification failed.");
            }
        }

        var manifest = Deserialize<ReleaseManifestDocument>(manifestBytes, "release manifest");
        ValidateManifest(release, manifest, envelope);
        return new VerifiedReleaseManifest(
            manifest,
            manifestAsset.Name,
            manifestAsset.Url,
            signatureAsset.Name,
            signatureAsset.Url);
    }

    private void ValidateManifest(
        JsonObject release,
        ReleaseManifestDocument manifest,
        ReleaseManifestSignatureEnvelope envelope)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported release-manifest schema version {manifest.SchemaVersion}.");
        var minimumUpdaterVersion = ParseVersion(manifest.MinimumSecureUpdaterVersion, "minimum secure updater");
        var currentUpdaterVersion = ParseVersion(_currentVersion(), "current updater");
        if (currentUpdaterVersion.CompareTo(minimumUpdaterVersion) < 0)
        {
            throw new InvalidOperationException(
                $"Release manifest requires updater version v{minimumUpdaterVersion} or later; this build is v{currentUpdaterVersion}.");
        }
        if (!string.Equals(manifest.SigningKeyId, envelope.KeyId, StringComparison.Ordinal))
            throw new InvalidOperationException("Release manifest and detached signature use different key IDs.");
        if (!string.Equals(manifest.Repository, "alekk89/llama-cpp-windows-manager", StringComparison.Ordinal))
            throw new InvalidOperationException("Release manifest names an unexpected repository.");
        if (!string.Equals(manifest.ReleaseChannel, "stable", StringComparison.Ordinal))
            throw new InvalidOperationException($"Release manifest uses unexpected channel '{manifest.ReleaseChannel}'.");
        if (JsonBool(release["draft"]) || JsonBool(release["prerelease"]))
            throw new InvalidOperationException("Stable updates cannot use draft or prerelease GitHub releases.");

        var releaseTag = release["tag_name"]?.ToString() ?? "";
        if (!string.Equals(manifest.Tag, releaseTag, StringComparison.Ordinal) ||
            !string.Equals(manifest.Tag, $"v{manifest.ApplicationVersion}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Release manifest version/tag does not match the GitHub release.");
        }
        if (!Regex.IsMatch(manifest.Commit ?? "", "^[0-9a-f]{40}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Release manifest does not contain a valid full Git commit SHA.");
        if (string.IsNullOrWhiteSpace(manifest.ExpectedWindowsPublisher))
            throw new InvalidOperationException("Release manifest does not name the expected Windows publisher.");

        var now = _utcNow();
        if (manifest.BuiltAtUtc == default || manifest.BuiltAtUtc > now.AddMinutes(15))
            throw new InvalidOperationException("Release manifest contains an invalid future build timestamp.");
        if (manifest.ExpiresAtUtc <= manifest.BuiltAtUtc || manifest.ExpiresAtUtc <= now)
            throw new InvalidOperationException("Release manifest is expired or has an invalid validity interval.");
        if (manifest.Artifacts is null || manifest.Artifacts.Count == 0)
            throw new InvalidOperationException("Release manifest contains no artifacts.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in manifest.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.Name) ||
                !string.Equals(Path.GetFileName(artifact.Name), artifact.Name, StringComparison.Ordinal) ||
                artifact.Name.Contains('/') || artifact.Name.Contains('\\') ||
                !names.Add(artifact.Name))
            {
                throw new InvalidOperationException($"Release manifest contains invalid or duplicate artifact name '{artifact.Name}'.");
            }
            if (string.IsNullOrWhiteSpace(artifact.Role) || artifact.Size <= 0 ||
                string.IsNullOrWhiteSpace(AppUpdateAssetVerifier.NormalizeSha256(artifact.Sha256)))
            {
                throw new InvalidOperationException($"Release manifest contains invalid metadata for '{artifact.Name}'.");
            }
        }

        if (manifest.Sbom is null ||
            !manifest.Artifacts.Any(artifact =>
                string.Equals(artifact.Role, "sbom", StringComparison.Ordinal) &&
                string.Equals(artifact.Name, manifest.Sbom.Name, StringComparison.Ordinal) &&
                string.Equals(artifact.Sha256, manifest.Sbom.Sha256, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Release manifest SBOM metadata does not match an artifact entry.");
        }
    }

    private static void ValidateEnvelope(ReleaseManifestSignatureEnvelope envelope)
    {
        if (envelope.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported release-signature schema version {envelope.SchemaVersion}.");
        if (!string.Equals(envelope.Algorithm, "ECDSA_P256_SHA256", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported release-signature algorithm '{envelope.Algorithm}'.");
        if (string.IsNullOrWhiteSpace(envelope.KeyId) || string.IsNullOrWhiteSpace(envelope.Signature))
            throw new InvalidOperationException("Release-signature envelope is incomplete.");
    }

    private async Task<byte[]> DownloadBoundedAsync(
        string url,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidOperationException("Release trust metadata exceeds the permitted size.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maximumBytes)
                throw new InvalidOperationException("Release trust metadata exceeds the permitted size.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static T Deserialize<T>(byte[] bytes, string label) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new InvalidOperationException($"The {label} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The {label} is malformed.", ex);
        }
    }

    private static (string Name, string Url) FindAsset(JsonObject release, string expectedName)
    {
        var assets = release["assets"]?.AsArray()
            ?? throw new InvalidOperationException("Stable release contains no GitHub assets.");
        var asset = assets
            .OfType<JsonObject>()
            .Select(item => (
                Name: item["name"]?.ToString() ?? "",
                Url: FirstNonBlank(item["browser_download_url"]?.ToString(), item["url"]?.ToString())))
            .FirstOrDefault(item => string.Equals(item.Name, expectedName, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(asset.Name) || !IsHttps(asset.Url))
            throw new InvalidOperationException($"Stable release is missing required trust asset '{expectedName}'.");
        return asset;
    }

    private static bool IsHttps(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool JsonBool(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private static Version ParseVersion(string? value, string label)
    {
        var text = (value ?? "").Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text[1..];
        var suffix = text.IndexOfAny(['-', '+']);
        if (suffix >= 0) text = text[..suffix];
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) text += ".0.0";
        else if (parts.Length == 2) text += ".0";
        if (!Version.TryParse(text, out var version))
            throw new InvalidOperationException($"Release manifest contains an invalid {label} version.");
        return version;
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}
