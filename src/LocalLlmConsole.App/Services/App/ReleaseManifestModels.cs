namespace LocalLlmConsole.Services;

public sealed record ReleaseManifestArtifact(
    string Name,
    string Role,
    string MediaType,
    long Size,
    string Sha256);

public sealed record ReleaseManifestSbom(string Name, string Sha256);

public sealed record ReleaseManifestDocument(
    int SchemaVersion,
    string ApplicationVersion,
    string Tag,
    string Commit,
    string Repository,
    string ReleaseChannel,
    DateTimeOffset BuiltAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string MinimumSecureUpdaterVersion,
    string SigningKeyId,
    string ExpectedWindowsPublisher,
    IReadOnlyList<ReleaseManifestArtifact>? Artifacts,
    ReleaseManifestSbom? Sbom);

public sealed record ReleaseManifestSignatureEnvelope(
    int SchemaVersion,
    string KeyId,
    string Algorithm,
    string Signature);

public sealed record VerifiedReleaseManifest(
    ReleaseManifestDocument Document,
    string ManifestAssetName,
    string ManifestAssetUrl,
    string SignatureAssetName,
    string SignatureAssetUrl);
