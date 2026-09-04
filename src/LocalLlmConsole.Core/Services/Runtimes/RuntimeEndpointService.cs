using System.Net.Http.Headers;
using System.Net.NetworkInformation;

namespace LocalLlmConsole.Services;

public static class RuntimeEndpointService
{
    public static string LocalServerBaseUrl(AppSettings settings)
    {
        var host = ModelAccessPolicy.RuntimeHost(settings.ModelAccessMode, settings.Host);
        if (string.Equals(host, "0.0.0.0", StringComparison.Ordinal)) host = "127.0.0.1";
        if (string.Equals(host, "::", StringComparison.Ordinal)) host = "::1";
        if (settings.Port <= 0 || settings.Port > 65535)
            throw new InvalidOperationException("Server port must be between 1 and 65535.");
        return $"http://{UrlHost(host)}:{settings.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string LocalOpenAiBaseUrl(AppSettings settings) => $"{LocalServerBaseUrl(settings)}/v1";

    public static string LocalGatewayServerBaseUrl(AppSettings settings)
    {
        if (settings.AutoLoadGatewayPort <= 0 || settings.AutoLoadGatewayPort > 65535)
            throw new InvalidOperationException("Gateway port must be between 1 and 65535.");
        return $"http://127.0.0.1:{settings.AutoLoadGatewayPort.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string LocalGatewayOpenAiBaseUrl(AppSettings settings) => $"{LocalGatewayServerBaseUrl(settings)}/v1";

    public static string ModelApiKeyForClient(AppSettings settings)
        => (settings.ModelApiKey ?? "").Trim();

    public static HttpRequestMessage RuntimeGetRequest(string url, AppSettings settings)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var apiKey = ModelApiKeyForClient(settings);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    public static async Task<string> RuntimeGetStringAsync(HttpClient client, string url, AppSettings settings, CancellationToken cancellationToken = default)
    {
        using var request = RuntimeGetRequest(url, settings);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public static IEnumerable<string> ExtractServedModelIds(string json)
    {
        var root = JsonNode.Parse(json);
        foreach (var arrayName in new[] { "data", "models" })
        {
            if (root?[arrayName] is not JsonArray array) continue;
            foreach (var item in array)
            {
                if (item is JsonObject obj)
                {
                    foreach (var key in new[] { "id", "model", "name" })
                    {
                        var value = obj[key]?.ToString();
                        if (!string.IsNullOrWhiteSpace(value)) yield return value;
                    }
                }
                else
                {
                    var value = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) yield return value;
                }
            }
        }
    }

    public static bool ServedModelMatches(ModelRecord model, string servedModel)
    {
        var served = Path.GetFileName(servedModel);
        var registered = Path.GetFileName(model.ModelPath);
        return string.Equals(servedModel, model.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(servedModel, model.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(served, registered, StringComparison.OrdinalIgnoreCase)
            || string.Equals(served, Path.GetFileNameWithoutExtension(registered), StringComparison.OrdinalIgnoreCase)
            || model.Name.Contains(Path.GetFileNameWithoutExtension(served), StringComparison.OrdinalIgnoreCase);
    }

    public static string EndpointDisplay(AppSettings settings)
    {
        var local = LocalOpenAiBaseUrl(settings);
        if (!ModelAccessPolicy.DirectModelsAllowLanAccess(settings.ModelAccessMode)) return local;

        var lan = LanOpenAiBaseUrl(settings);
        return string.Equals(local, lan, StringComparison.OrdinalIgnoreCase) ? local : $"{local} (LAN: {lan})";
    }

    public static string GatewayEndpointDisplay(AppSettings settings)
    {
        var local = LocalGatewayOpenAiBaseUrl(settings);
        if (!ModelAccessPolicy.GatewayAllowsLanAccess(settings.ModelAccessMode)) return local;

        var lan = $"{LanGatewayServerBaseUrl(settings)}/v1";
        return string.Equals(local, lan, StringComparison.OrdinalIgnoreCase) ? local : $"{local} (LAN: {lan})";
    }

    public static string LanOpenAiBaseUrl(AppSettings settings) => $"{LanServerBaseUrl(settings)}/v1";

    public static string LanGatewayServerBaseUrl(AppSettings settings)
    {
        var host = PreferredLanAddress() ?? Environment.MachineName;
        return $"http://{UrlHost(host)}:{settings.AutoLoadGatewayPort.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string LanServerBaseUrl(AppSettings settings)
    {
        var host = ModelAccessPolicy.RuntimeHost(settings.ModelAccessMode, settings.Host);
        if (string.Equals(host, "0.0.0.0", StringComparison.Ordinal) || string.Equals(host, "::", StringComparison.Ordinal))
            host = PreferredLanAddress() ?? Environment.MachineName;
        return $"http://{UrlHost(host)}:{settings.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string UrlHost(string host)
    {
        if (IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
            return $"[{host}]";
        return host;
    }

    private static string? PreferredLanAddress()
    {
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up) continue;
                if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    var ip = address.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ip)) continue;
                    var bytes = ip.GetAddressBytes();
                    if (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254) continue;
                    return ip.ToString();
                }
            }
        }
        catch
        {
        }

        return null;
    }

}
