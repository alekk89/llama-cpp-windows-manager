namespace LocalLlmConsole.Services;

public sealed class ModelGatewayRequestAccessPolicy
{
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

    public bool IsHostAllowed(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader)) return false;
        if (!Uri.TryCreate($"http://{hostHeader.Trim()}", UriKind.Absolute, out var uri)) return false;
        if (uri.Port != _options.Port) return false;
        return _options.AllowLanAccess || ApiSecurity.IsLoopbackHost(uri.Host);
    }

    public bool IsAuthorized(HttpListenerRequest request)
    {
        if (ApiSecurity.BearerTokenMatches(request.Headers["Authorization"], _options.ApiKey))
            return true;

        var xApiKey = request.Headers["x-api-key"];
        return !string.IsNullOrWhiteSpace(xApiKey) && ApiSecurity.SecretEquals(xApiKey.Trim(), _options.ApiKey);
    }

    private bool IsOriginAllowed(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        return _options.AllowLanAccess || ApiSecurity.IsLoopbackHost(uri.Host);
    }
}
