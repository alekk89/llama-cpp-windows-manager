namespace LocalLlmConsole.Services;

public sealed class ModelGatewayRequestAccessPolicy
{
    // Headers the gateway owns on every response. Upstream runtimes must not
    // widen CORS access or weaken caching rules by returning their own values.
    private static readonly HashSet<string> GatewayOwnedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Vary",
        "Cache-Control",
        "X-Content-Type-Options"
    };

    private readonly ModelGatewayOptions _options;

    public ModelGatewayRequestAccessPolicy(ModelGatewayOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void AddResponseHeaders(HttpListenerContext context)
    {
        var origin = context.Request.Headers["Origin"];
        if (!string.IsNullOrWhiteSpace(origin) && IsOriginAllowed(origin))
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "content-type,authorization,x-api-key";
        context.Response.Headers["Vary"] = "Origin";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    public static bool IsGatewayOwnedResponseHeader(string name)
        => name.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase)
           || GatewayOwnedResponseHeaders.Contains(name);

    public bool IsHostAllowed(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader)) return false;
        if (!Uri.TryCreate($"http://{hostHeader.Trim()}", UriKind.Absolute, out var uri)) return false;
        if (uri.Port != _options.Port) return false;
        return _options.AllowLanAccess || ApiSecurity.IsLoopbackHost(uri.Host);
    }

    public bool IsRemoteEndpointAllowed(IPAddress? remoteAddress)
        => _options.AllowLanAccess || remoteAddress is not null && IPAddress.IsLoopback(remoteAddress);

    public bool IsAuthorized(HttpListenerRequest request)
    {
        if (!_options.RequireApiKeyAuth) return true;

        if (ApiSecurity.BearerTokenMatches(request.Headers["Authorization"], _options.ApiKey))
            return true;

        var xApiKey = request.Headers["x-api-key"];
        return !string.IsNullOrWhiteSpace(xApiKey) && ApiSecurity.SecretEquals(xApiKey.Trim(), _options.ApiKey);
    }

    // Requests without an Origin come from non-browser clients and are allowed.
    // A local-only gateway accepts only loopback browser origins. LAN modes
    // always require the API key, so they keep accepting any http(s) browser
    // origin that also presents it. Opaque and malformed origins are rejected.
    public bool IsRequestOriginAllowed(string? origin)
        => string.IsNullOrWhiteSpace(origin) || IsOriginAllowed(origin);

    private bool IsOriginAllowed(string origin)
    {
        var value = origin.Trim();
        if (value.Equals("null", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        return _options.AllowLanAccess || ApiSecurity.IsLoopbackHost(uri.Host);
    }
}
