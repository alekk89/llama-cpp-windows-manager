
namespace LocalLlmConsole.Services;

public sealed class ApiSecurity
{
    public string SessionToken { get; } = GenerateHexToken(48);

    public static string GenerateHexToken(int byteCount) => RandomNumberGenerator.GetHexString(byteCount).ToLowerInvariant();

    public static string StrongBearerSecretOrNew(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var secret = (candidate ?? "").Trim();
            if (IsStrongBearerSecret(secret))
                return secret;
        }

        return GenerateHexToken(32);
    }

    public static bool IsStrongBearerSecret(string? value)
    {
        var secret = (value ?? "").Trim();
        return secret.Length >= 32 && !secret.Any(char.IsWhiteSpace);
    }

    public bool IsLocalOriginAllowed(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return true;
        if (origin.Equals("null", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        return IsLoopbackHost(uri.Host);
    }

    public bool IsLocalHostHeaderAllowed(string? hostHeader, int expectedPort)
    {
        if (string.IsNullOrWhiteSpace(hostHeader)) return false;
        if (!Uri.TryCreate($"http://{hostHeader.Trim()}", UriKind.Absolute, out var uri)) return false;
        return uri.Port == expectedPort && IsLoopbackHost(uri.Host);
    }

    public bool IsAuthorized(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization)) return false;
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var supplied = authorization[prefix.Length..].Trim();
        return SecretEquals(supplied, SessionToken);
    }

    public static bool BearerTokenMatches(string? authorization, string expectedSecret)
    {
        if (string.IsNullOrWhiteSpace(authorization)) return false;
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return SecretEquals(authorization[prefix.Length..].Trim(), expectedSecret);
    }

    public static bool SecretEquals(string supplied, string expected)
    {
        if (supplied.Length != expected.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(supplied),
            System.Text.Encoding.UTF8.GetBytes(expected));
    }

    public static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
