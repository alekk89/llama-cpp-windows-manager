namespace LocalLlmConsole.Services;

public delegate Task<bool> RuntimeEndpointRespondingProbe(AppSettings launchSettings, CancellationToken cancellationToken);

public delegate Task<bool> RuntimeLoopbackPortProbe(int port, CancellationToken cancellationToken);

public sealed record RuntimeLaunchPrerequisiteRequest(
    RuntimeRecord Runtime,
    AppSettings LaunchSettings,
    RuntimeEndpointRespondingProbe EndpointRespondingAsync);

public sealed class RuntimeLaunchPrerequisiteService
{
    private readonly RuntimeToolPrerequisiteService _runtimeTools;
    private readonly RuntimeLoopbackPortProbe _isLoopbackPortOpenAsync;

    public RuntimeLaunchPrerequisiteService(
        WslEnvironmentService wslEnvironment,
        WindowsEnvironmentService windowsEnvironment,
        IProcessRunner processRunner)
        : this(new RuntimeToolPrerequisiteService(wslEnvironment, windowsEnvironment, processRunner))
    {
    }

    public RuntimeLaunchPrerequisiteService(RuntimeToolPrerequisiteService runtimeTools)
        : this(runtimeTools, IsLoopbackPortOpenAsync)
    {
    }

    public RuntimeLaunchPrerequisiteService(
        WslEnvironmentReportReader readWslReportAsync,
        WindowsToolSnapshotReader readWindowsTools,
        IProcessRunner processRunner,
        RuntimeLoopbackPortProbe isLoopbackPortOpenAsync,
        Func<string>? wslExecutablePath = null)
        : this(new RuntimeToolPrerequisiteService(readWslReportAsync, readWindowsTools, processRunner, wslExecutablePath), isLoopbackPortOpenAsync)
    {
    }

    public RuntimeLaunchPrerequisiteService(
        RuntimeToolPrerequisiteService runtimeTools,
        RuntimeLoopbackPortProbe isLoopbackPortOpenAsync)
    {
        _runtimeTools = runtimeTools ?? throw new ArgumentNullException(nameof(runtimeTools));
        _isLoopbackPortOpenAsync = isLoopbackPortOpenAsync ?? throw new ArgumentNullException(nameof(isLoopbackPortOpenAsync));
    }

    public async Task EnsureReadyAsync(RuntimeLaunchPrerequisiteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.EndpointRespondingAsync);

        RuntimeAvailabilityService.EnsureAvailable(request.Runtime);

        if (request.Runtime.Mode == RuntimeMode.Wsl)
            await _runtimeTools.EnsureWslDistroReadyAsync(request.LaunchSettings.WslDistro, cancellationToken);
        if (request.Runtime.Mode == RuntimeMode.Wsl && request.Runtime.Backend == RuntimeBackend.Sycl)
            await _runtimeTools.EnsureWslSyclToolsReadyAsync(request.LaunchSettings.WslDistro, cancellationToken);
        if (request.Runtime.Mode == RuntimeMode.Native && request.Runtime.Backend == RuntimeBackend.Sycl)
            _runtimeTools.EnsureWindowsSyclToolsReady();

        if (await IsRuntimePortOccupiedAsync(request.LaunchSettings, request.EndpointRespondingAsync, cancellationToken))
            throw new InvalidOperationException($"Port {request.LaunchSettings.Port} is already in use. Stop the existing process or choose a different model port before launching llama.cpp.");
    }

    private async Task<bool> IsRuntimePortOccupiedAsync(
        AppSettings launchSettings,
        RuntimeEndpointRespondingProbe endpointRespondingAsync,
        CancellationToken cancellationToken)
    {
        var port = launchSettings.Port;
        if (port is < 1 or > 65535) return false;
        if (await endpointRespondingAsync(launchSettings, cancellationToken)) return true;
        return await _isLoopbackPortOpenAsync(port, cancellationToken);
    }

    private static async Task<bool> IsLoopbackPortOpenAsync(int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        try
        {
            var connectTask = client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).AsTask();
            var completed = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
            if (completed != connectTask) return false;
            await connectTask;
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
