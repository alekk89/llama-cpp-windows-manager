namespace LocalLlmConsole.Services;

public sealed record RuntimePackagePreset(
    string Id,
    string Label,
    RuntimeBackend Backend,
    RuntimeMode Mode,
    string SourcePresetId,
    string ReleaseApiUrl = "",
    string ReleasePageUrl = "",
    string PackageSourceLabel = "",
    string PackageSourceKey = "",
    string RepositoryUrl = "");

public sealed record RuntimePackageAsset(
    string Name,
    string DownloadUrl,
    long SizeBytes,
    string Sha256 = "",
    string ChecksumUrl = "");

public sealed record RuntimePackageRelease(
    string TagName,
    string TargetCommit,
    string HtmlUrl,
    DateTimeOffset PublishedAt,
    IReadOnlyList<RuntimePackageAsset> Assets);

public sealed record RuntimePackageSelection(
    RuntimePackagePreset Preset,
    string ReleaseTag,
    string ReleaseUrl,
    DateTimeOffset PublishedAt,
    RuntimePackageAsset PrimaryAsset,
    IReadOnlyList<RuntimePackageAsset> AdditionalAssets)
{
    public IReadOnlyList<RuntimePackageAsset> AllAssets => [PrimaryAsset, .. AdditionalAssets];
    public string AssetSummary => string.Join(", ", AllAssets.Select(asset => asset.Name));
}

public sealed record RuntimePackageUpdateState(
    bool HasUpdate,
    string LocalTag,
    string LatestTag,
    string ReleaseUrl,
    string AssetSummary,
    DateTimeOffset CheckedAt,
    string LocalIdentity = "",
    string TargetCommit = "",
    bool IsAvailable = true);

public sealed class RuntimePackageAssetUnavailableException : InvalidOperationException
{
    public RuntimePackageAssetUnavailableException(string message) : base(message)
    {
    }
}
