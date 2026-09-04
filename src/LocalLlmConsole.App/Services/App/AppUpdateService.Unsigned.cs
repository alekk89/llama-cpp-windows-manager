namespace LocalLlmConsole.Services;

public sealed partial class AppUpdateService
{
    private static bool RequiresSignedUpdates()
        => !string.Equals(typeof(AppUpdateService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "RequireSignedUpdates")?.Value,
            "false", StringComparison.OrdinalIgnoreCase);

    private static bool HasManifestAssets(JsonObject release)
        => release["assets"]?.AsArray().OfType<JsonObject>().Any(asset =>
            string.Equals(asset["name"]?.ToString(), AppReleaseManifestVerifier.ManifestAssetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(asset["name"]?.ToString(), AppReleaseManifestVerifier.ManifestAssetName + ".sig", StringComparison.OrdinalIgnoreCase)) == true;

    private void ValidateUnsignedUpdate(AppUpdateInfo update)
    {
        if (!Version.TryParse(update.LatestVersion.TrimStart('v', 'V'), out var latest) ||
            !Version.TryParse(_currentVersion.Split('+', 2)[0].TrimStart('v', 'V'), out var current) || latest <= current)
            throw AppUpdateVerificationException.Trust("The selected release is not newer than the installed application.");
        if (update.AssetName != PortableExeName || update.AssetSize <= 0 ||
            update.ChecksumAssetName != PortableExeName + ".sha256")
            throw AppUpdateVerificationException.Asset("Unsigned updates require the portable EXE and its SHA-256 companion.");
        var prefix = RepositoryUrl + "/releases/download/" + update.LatestVersion + "/";
        if (!IsReleaseAssetUrl(update.AssetUrl, prefix + PortableExeName) ||
            !IsReleaseAssetUrl(update.ChecksumAssetUrl, prefix + PortableExeName + ".sha256"))
            throw AppUpdateVerificationException.Trust("Unsigned updates must use HTTPS assets from the official repository and matching release tag.");
        if (!string.IsNullOrWhiteSpace(update.ExpectedSha256))
            throw AppUpdateVerificationException.Asset("Unsigned updates must verify the published SHA-256 companion file.");
    }

    private static bool IsReleaseAssetUrl(string value, string expected)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort &&
            string.Equals(uri.AbsoluteUri, expected, StringComparison.Ordinal);
}
