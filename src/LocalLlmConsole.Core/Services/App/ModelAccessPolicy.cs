namespace LocalLlmConsole.Services;

public static class ModelAccessPolicy
{
    public static string Normalize(string? text)
    {
        var value = (text ?? "").Trim()
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("+", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

        return value switch
        {
            "gateway" or "gatewaylan" or "gatewayonly" or "gatewaylanonly" or "router" or "routerlan" or "routeronly" or "routerlanonly" => "gateway",
            "models" or "modellan" or "modelsnetwork" or "modelsaccess" or "modelsnetworkaccess" or "direct" or "directlan" or "directonly" or "directlanonly" or "directmodels" or "directmodelslan" or "directmodelsonly" or "directmodelslanonly" => "models",
            "both" or "all" or "gatewaydirect" or "gatewaydirectlan" or "routerdirect" or "routerdirectlan" or "gatewaymodels" or "gatewaymodelslan" => "both",
            "lan" or "lanaccess" or "network" or "networkaccess" => "both",
            _ => "local"
        };
    }

    public static bool GatewayAllowsLanAccess(string? text)
        => Normalize(text) is "gateway" or "both";

    public static bool DirectModelsAllowLanAccess(string? text)
        => Normalize(text) is "models" or "both";

    public static string RuntimeHost(string? accessMode)
        => DirectModelsAllowLanAccess(accessMode) ? "0.0.0.0" : "127.0.0.1";
}
