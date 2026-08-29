namespace LocalLlmConsole.Services;

public sealed record RuntimeBuildPrerequisiteRequest(
    RuntimeMode Mode,
    RuntimeBackend Backend,
    string WslDistro);

public sealed class RuntimeBuildPrerequisiteService
{
    private readonly RuntimeToolPrerequisiteService _runtimeTools;

    public RuntimeBuildPrerequisiteService(RuntimeToolPrerequisiteService runtimeTools)
    {
        _runtimeTools = runtimeTools ?? throw new ArgumentNullException(nameof(runtimeTools));
    }

    public async Task EnsureReadyAsync(RuntimeBuildPrerequisiteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Mode == RuntimeMode.Wsl)
        {
            await _runtimeTools.EnsureWslDistroReadyAsync(request.WslDistro, cancellationToken);
            await _runtimeTools.EnsureWslBuildToolsReadyAsync(request.Backend, request.WslDistro, cancellationToken);
            return;
        }

        _runtimeTools.EnsureWindowsBuildToolsReady(request.Backend);
    }

    public async Task EnsurePackageInstallReadyAsync(RuntimePackagePreset preset, string wslDistro, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (preset.Mode == RuntimeMode.Wsl)
            await _runtimeTools.EnsureWslDistroReadyAsync(wslDistro, cancellationToken);
    }
}
