namespace LocalLlmConsole.Services;

public enum RuntimeAuthenticationProbeStatus
{
    Verified,
    NotEnforced,
    CredentialRejected,
    Unavailable
}

public sealed record RuntimeAuthenticationProbeResult(
    RuntimeAuthenticationProbeStatus Status,
    string Message)
{
    public bool IsVerified => Status == RuntimeAuthenticationProbeStatus.Verified;
}

public sealed class RuntimeEndpointProbeService
{
    private static readonly string[] AliveProbePaths = ["health", "v1/models"];
    private static readonly string[] RespondingProbePaths = ["health", "v1/models", "metrics"];

    private readonly HttpClient _http;

    public RuntimeEndpointProbeService(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<bool> IsAliveAsync(AppSettings launchSettings, CancellationToken cancellationToken = default)
    {
        foreach (var path in AliveProbePaths)
        {
            try
            {
                using var response = await GetAsync(launchSettings, path, cancellationToken);
                if (response.IsSuccessStatusCode) return true;
            }
            catch
            {
                // Try the next runtime endpoint.
            }
        }

        return false;
    }

    public async Task<bool> IsRespondingAsync(AppSettings launchSettings, CancellationToken cancellationToken = default)
    {
        foreach (var path in RespondingProbePaths)
        {
            try
            {
                using var _ = await GetAsync(launchSettings, path, cancellationToken);
                return true;
            }
            catch
            {
                // Try the next runtime endpoint.
            }
        }

        return false;
    }

    public async Task<IReadOnlyList<string>> ServedModelsAsync(
        AppSettings launchSettings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await RuntimeEndpointService.RuntimeGetStringAsync(
                _http,
                $"{RuntimeEndpointService.LocalOpenAiBaseUrl(launchSettings)}/models",
                launchSettings,
                cancellationToken);
            return RuntimeEndpointService.ExtractServedModelIds(json).ToArray();
        }
        catch
        {
            return [];
        }
    }

    public async Task<RuntimeAuthenticationProbeResult> VerifyAuthenticationAsync(
        AppSettings launchSettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launchSettings);
        if (launchSettings.RequireApiKeyAuth && string.IsNullOrWhiteSpace(launchSettings.ModelApiKey))
            return new(RuntimeAuthenticationProbeStatus.CredentialRejected,
                "The runtime launch has no API key to verify.");

        var endpoint = $"{RuntimeEndpointService.LocalOpenAiBaseUrl(launchSettings)}/chat/completions";
        try
        {
            using var unauthenticatedRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var unauthenticated = await _http.SendAsync(unauthenticatedRequest, cancellationToken);
            if (!launchSettings.RequireApiKeyAuth)
            {
                return unauthenticated.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? new(RuntimeAuthenticationProbeStatus.CredentialRejected,
                        "The local runtime requires an API key although authentication is disabled.")
                    : new(RuntimeAuthenticationProbeStatus.Verified,
                        $"The local runtime accepted an unauthenticated request with HTTP {(int)unauthenticated.StatusCode}, as configured.");
            }
            if (unauthenticated.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
                return new(RuntimeAuthenticationProbeStatus.NotEnforced,
                    $"The runtime accepted an unauthenticated request with HTTP {(int)unauthenticated.StatusCode}.");

            using var authenticatedRequest = RuntimeEndpointService.RuntimeGetRequest(endpoint, launchSettings);
            using var authenticated = await _http.SendAsync(authenticatedRequest, cancellationToken);
            if (authenticated.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(RuntimeAuthenticationProbeStatus.CredentialRejected,
                    "The runtime rejected the configured API key.");

            return new(RuntimeAuthenticationProbeStatus.Verified,
                "The runtime rejected an unauthenticated request and accepted the configured API key.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(RuntimeAuthenticationProbeStatus.Unavailable,
                $"Runtime authentication could not be verified: {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage> GetAsync(
        AppSettings launchSettings,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = RuntimeEndpointService.RuntimeGetRequest(
            $"{RuntimeEndpointService.LocalServerBaseUrl(launchSettings)}/{path}",
            launchSettings);
        return await _http.SendAsync(request, cancellationToken);
    }
}
