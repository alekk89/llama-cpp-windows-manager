namespace LocalLlmConsole.Services;

public sealed partial class LlamaProcessSupervisor : IDisposable
{
    private static string? ResolveMtpHeadPath(string modelPath, string configuredHeadPath, string speculativeType)
        => ModelCatalogService.ResolveMtpHeadPath(modelPath, configuredHeadPath, speculativeType);
}
