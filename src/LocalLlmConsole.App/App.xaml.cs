using System.Windows;

namespace LocalLlmConsole;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\llama.cpp-console-single-instance";

    private readonly SingleInstanceApplicationService _singleInstance = new(SingleInstanceApplicationService.AcquireMutexLease);
    private readonly DialogService _dialogs = new(ThemedMessageBox.Show);

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--bootstrap-agent-sidecars-only", StringComparer.OrdinalIgnoreCase))
        {
            var sidecars = new AgentSidecarBootstrapService().InstallPackaged(
                Environment.ProcessPath ?? "",
                AppContext.BaseDirectory,
                verifyBundleContents: true);
            if (sidecars.Status == AgentSidecarBootstrapStatus.Failed)
                Trace.TraceWarning($"Agent control sidecar bootstrap failed: {sidecars.Error}");
            Shutdown(AgentSidecarBootstrapService.VerificationExitCode(sidecars.Status));
            return;
        }

        if (!_singleInstance.TryAcquire(SingleInstanceMutexName))
        {
            _dialogs.Notify(null, "llama.cpp Windows Manager is already running.", "llama.cpp Windows Manager", MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
        _ = BootstrapPackagedSidecarsAsync();
    }

    private static async Task BootstrapPackagedSidecarsAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            var result = await Task.Run(() => new AgentSidecarBootstrapService().InstallPackaged(
                Environment.ProcessPath ?? "",
                AppContext.BaseDirectory));
            if (result.Status == AgentSidecarBootstrapStatus.Failed)
                Trace.TraceWarning($"Agent control sidecar bootstrap failed: {result.Error}");
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Agent control sidecar bootstrap failed: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance.Dispose();
        base.OnExit(e);
    }
}
