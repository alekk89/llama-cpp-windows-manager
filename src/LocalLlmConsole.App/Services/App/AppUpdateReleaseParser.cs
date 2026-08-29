namespace LocalLlmConsole.Services;

public static class AppUpdateReleaseParser
{
    private static readonly string[] PortableExeNames = [AppUpdateService.PortableExeName];

    public static AppUpdateInfo ParseLatestRelease(JsonObject release, string currentVersion)
    {
        var latestVersion = FirstNonBlank(
            release["tag_name"]?.ToString(),
            release["name"]?.ToString());
        if (string.IsNullOrWhiteSpace(latestVersion))
            throw AppUpdateVerificationException.Trust("The GitHub release has no tag name.");

        var assets = release["assets"]?.AsArray();
        var asset = SelectPortableAsset(assets);
        var checksum = SelectChecksumAsset(assets, asset.Name);
        var latest = NormalizeVersion(latestVersion);
        var current = NormalizeVersion(currentVersion);
        return new AppUpdateInfo(
            IsVersionNewer(latest, current),
            VersionLabel(currentVersion),
            VersionLabel(latestVersion),
            FirstNonBlank(release["name"]?.ToString(), VersionLabel(latestVersion)),
            release["body"]?.ToString() ?? "",
            release["html_url"]?.ToString() ?? AppUpdateService.RepositoryUrl,
            asset.Name,
            asset.Url,
            asset.Size,
            checksum.Name,
            checksum.Url);
    }

    public static AppUpdateInfo ParseVerifiedLatestRelease(
        JsonObject release,
        string currentVersion,
        VerifiedReleaseManifest verifiedManifest)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(verifiedManifest);
        var manifest = verifiedManifest.Document;
        var latestVersion = FirstNonBlank(release["tag_name"]?.ToString(), release["name"]?.ToString());
        if (string.IsNullOrWhiteSpace(latestVersion))
            throw AppUpdateVerificationException.Manifest("The GitHub release has no tag name.");
        if (!string.Equals(VersionLabel(latestVersion), VersionLabel(manifest.Tag), StringComparison.Ordinal) ||
            !string.Equals(VersionLabel(latestVersion), VersionLabel(manifest.ApplicationVersion), StringComparison.Ordinal))
        {
            throw AppUpdateVerificationException.Manifest("Verified release-manifest version does not match the GitHub release.");
        }

        var selected = SelectVerifiedPortableAsset(release["assets"]?.AsArray(), manifest.Artifacts ?? []);
        return new AppUpdateInfo(
            IsVersionNewer(NormalizeVersion(latestVersion), NormalizeVersion(currentVersion)),
            VersionLabel(currentVersion),
            VersionLabel(latestVersion),
            FirstNonBlank(release["name"]?.ToString(), VersionLabel(latestVersion)),
            release["body"]?.ToString() ?? "",
            release["html_url"]?.ToString() ?? AppUpdateService.RepositoryUrl,
            selected.Asset.Name,
            selected.Url,
            selected.Asset.Size,
            ExpectedSha256: selected.Asset.Sha256,
            AuthenticityVerified: true,
            ReleaseChannel: manifest.ReleaseChannel,
            ManifestKeyId: manifest.SigningKeyId,
            ManifestCommit: manifest.Commit,
            ManifestExpiresAtUtc: manifest.ExpiresAtUtc,
            ExpectedWindowsPublisher: manifest.ExpectedWindowsPublisher);
    }

    public static AppUpdateInfo NoUpdateAvailable(string currentVersion, string message = "No updates are available.")
        => new(false, VersionLabel(currentVersion), VersionLabel(currentVersion), message, message, AppUpdateService.RepositoryUrl, "", "", 0);

    public static bool IsPortableExeName(string name)
        => PortableExeNames.Any(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static (string Name, string Url, long Size) SelectPortableAsset(JsonArray? assets)
    {
        if (assets is null) return ("", "", 0);
        var candidates = assets
            .OfType<JsonObject>()
            .Select(asset => (
                Name: asset["name"]?.ToString() ?? "",
                Url: FirstNonBlank(asset["browser_download_url"]?.ToString(), asset["url"]?.ToString()),
                Size: JsonLong(asset["size"])))
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) && !string.IsNullOrWhiteSpace(asset.Url))
            .ToList();

        return PortableExeNames
            .Select(name => candidates.FirstOrDefault(asset => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(asset => !string.IsNullOrWhiteSpace(asset.Name))
            is var exact && !string.IsNullOrWhiteSpace(exact.Name) ? exact
            : candidates.FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && asset.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
                is var zip && !string.IsNullOrWhiteSpace(zip.Name) ? zip
            : candidates.FirstOrDefault(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static (ReleaseManifestArtifact Asset, string Url) SelectVerifiedPortableAsset(
        JsonArray? releaseAssets,
        IReadOnlyList<ReleaseManifestArtifact> manifestArtifacts)
    {
        if (releaseAssets is null)
            throw AppUpdateVerificationException.Asset("The GitHub release contains no assets.");
        var published = releaseAssets
            .OfType<JsonObject>()
            .Select(asset => (
                Name: asset["name"]?.ToString() ?? "",
                Url: FirstNonBlank(asset["browser_download_url"]?.ToString(), asset["url"]?.ToString()),
                Size: JsonLong(asset["size"])))
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) && IsHttps(asset.Url))
            .ToDictionary(asset => asset.Name, StringComparer.OrdinalIgnoreCase);
        var allowed = manifestArtifacts
            .Where(artifact =>
                string.Equals(artifact.Role, "application", StringComparison.Ordinal) ||
                string.Equals(artifact.Role, "portable-archive", StringComparison.Ordinal))
            .ToDictionary(artifact => artifact.Name, StringComparer.OrdinalIgnoreCase);

        var orderedNames = allowed.Keys
            .Where(name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            .Concat([AppUpdateService.PortableExeName])
            .Concat(allowed.Keys.Order(StringComparer.OrdinalIgnoreCase));
        foreach (var name in orderedNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!allowed.TryGetValue(name, out var artifact) || !published.TryGetValue(name, out var releaseAsset)) continue;
            if (releaseAsset.Size != artifact.Size)
                throw AppUpdateVerificationException.Asset($"GitHub asset size for '{name}' does not match the signed release manifest.");
            return (artifact, releaseAsset.Url);
        }
        throw AppUpdateVerificationException.Asset("The GitHub release has no portable Windows asset listed by the signed release manifest.");
    }

    private static (string Name, string Url) SelectChecksumAsset(JsonArray? assets, string assetName)
    {
        if (assets is null || string.IsNullOrWhiteSpace(assetName)) return ("", "");
        var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            assetName + ".sha256",
            assetName + ".sha256.txt",
            Path.ChangeExtension(assetName, ".sha256")
        };
        return assets
            .OfType<JsonObject>()
            .Select(asset => (
                Name: asset["name"]?.ToString() ?? "",
                Url: FirstNonBlank(asset["browser_download_url"]?.ToString(), asset["url"]?.ToString())))
            .FirstOrDefault(asset => expectedNames.Contains(asset.Name));
    }

    private static Version NormalizeVersion(string value)
    {
        var text = (value ?? "").Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text[1..];
        var prerelease = text.IndexOfAny(['-', '+']);
        if (prerelease >= 0) text = text[..prerelease];
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) text += ".0.0";
        else if (parts.Length == 2) text += ".0";
        return Version.TryParse(text, out var version) ? version : new Version(0, 0, 0);
    }

    private static bool IsVersionNewer(Version latest, Version current) => latest.CompareTo(current) > 0;

    private static string VersionLabel(string value)
    {
        var text = (value ?? "").Trim();
        return text.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? text : $"v{text}";
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static long JsonLong(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<long>(out var number) ? number : 0;

    private static bool IsHttps(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
