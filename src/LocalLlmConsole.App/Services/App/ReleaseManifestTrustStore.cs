namespace LocalLlmConsole.Services;

public sealed record ReleaseManifestTrustKey(string KeyId, string SubjectPublicKeyInfoBase64);

public sealed class ReleaseManifestTrustStore
{
    private const string MetadataPrefix = "ReleaseManifestKey.";
    private readonly IReadOnlyDictionary<string, byte[]> _keys;

    public ReleaseManifestTrustStore(IEnumerable<ReleaseManifestTrustKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var parsed = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key.KeyId))
                throw new ArgumentException("Release-manifest key IDs cannot be empty.", nameof(keys));
            byte[] publicKey;
            try
            {
                publicKey = Convert.FromBase64String(key.SubjectPublicKeyInfoBase64);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException($"Release-manifest public key '{key.KeyId}' is not valid base64.", nameof(keys), ex);
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out var consumed);
            if (consumed != publicKey.Length || ecdsa.KeySize != 256)
                throw new ArgumentException($"Release-manifest public key '{key.KeyId}' must be an ECDSA P-256 SPKI key.", nameof(keys));
            if (!parsed.TryAdd(key.KeyId, publicKey))
                throw new ArgumentException($"Release-manifest key ID '{key.KeyId}' is duplicated.", nameof(keys));
        }
        _keys = parsed;
    }

    public int Count => _keys.Count;

    public bool TryGetPublicKey(string keyId, out byte[] publicKey)
    {
        if (_keys.TryGetValue(keyId, out var stored))
        {
            publicKey = stored.ToArray();
            return true;
        }
        publicKey = [];
        return false;
    }

    public static ReleaseManifestTrustStore FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var keys = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal))
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Value))
            .Select(attribute => new ReleaseManifestTrustKey(
                attribute.Key[MetadataPrefix.Length..],
                attribute.Value!));
        return new ReleaseManifestTrustStore(keys);
    }
}
