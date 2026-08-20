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
            var sidecars = new AgentSidecarBootstrapService().InstallEmbedded(
                typeof(App).Assembly,
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

        var startupSidecars = new AgentSidecarBootstrapService().InstallEmbedded(typeof(App).Assembly, AppContext.BaseDirectory);
        if (startupSidecars.Status == AgentSidecarBootstrapStatus.Failed)
            Trace.TraceWarning($"Agent control sidecar bootstrap failed: {startupSidecars.Error}");

        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance.Dispose();
        base.OnExit(e);
    }
}
