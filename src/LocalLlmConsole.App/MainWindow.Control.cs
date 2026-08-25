using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private ILocalAppServiceHost CreateLocalControlService(StateStore stateStore, JobEngine jobs, int port)
    {
        var api = new LocalControlApi(new LocalControlDependencies(
            _workspaceRoot,
            stateStore,
            _sessions,
            ModelServices.Catalog,
            ModelServices.LaunchProfiles,
            RuntimeServices.Runtimes,
            AppServices.HuggingFace,
            _coreServices.Runtime.RuntimeTelemetryApplication,
            _coreServices.Runtime.RuntimeLogTail,
            _coreServices.Runtime.RuntimeEndpointProbe,
            AppServices.LogPageWorkflow,
            new LocalControlActions(
                () => _settings,
                ApplyControlSettingsAsync,
                StartControlModelAsync,
                StopControlModelAsync,
                RefreshControlUiAsync,
                ExecuteControlOperationAsync),
            new ControlApiAuditLogService(
                Path.Combine(_workspaceRoot, "logs"),
                () => _settings.MaxLogFileSizeMb),
            ModelServices.ModelGroups,
            _coreServices.Runtime.EndpointInspection,
            AppServices.LifetimeMetricsApplication));
        _controlApi = api;
        return _serviceFactory.CreateLocalAppService(
            stateStore,
            jobs,
            port,
            api,
            new LocalControlDiscoveryService(_workspaceRoot));
    }

    private Task<AppSettings> ApplyControlSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(() => ApplyControlSettingsOnUiAsync(settings, cancellationToken)).Task.Unwrap();

    private async Task<AppSettings> ApplyControlSettingsOnUiAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var previousCulture = _settings.UiCulture;
        var previousRuntimeLogOrder = _settings.RuntimeLogOrder;
        var persisted = await AppServices.SettingsApplication.PersistAsync(settings, cancellationToken);
        _settings = persisted;
        ApplyGpuEnergyTrackingBoundary();
        _serviceFactory.CreateWindowsStartupRegistrationService().Apply(persisted.StartWithWindows);
        ApplicationThemeService.Apply(persisted.ThemeMode);
        if (!string.Equals(previousCulture, persisted.UiCulture, StringComparison.OrdinalIgnoreCase))
        {
            Loc.LoadLanguage(persisted.UiCulture);
            ApplyLocalizedXamlStrings();
            PopulateLanguageSelector();
        }
        ApplyLaunchSettingsToControls();
        await RestartModelGatewayAsync();
        if (_viewModel.CurrentPage == "Settings")
            ShowSettings();
        await RefreshAllAsync();
        if (!string.Equals(previousRuntimeLogOrder, persisted.RuntimeLogOrder, StringComparison.OrdinalIgnoreCase))
            await RefreshRuntimeLogOrderAsync();
        SetStatus("Settings updated through the local control API.");
        return persisted;
    }

    private Task<LoadedModelSessionSnapshot> StartControlModelAsync(
        RuntimeRecord runtime,
        ModelRecord model,
        AppSettings settings,
        string profileId,
        string profileName,
        CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(() => StartControlModelOnUiAsync(
            runtime,
            model,
            settings,
            profileId,
            profileName,
            cancellationToken)).Task.Unwrap();

    private async Task<LoadedModelSessionSnapshot> StartControlModelOnUiAsync(
        RuntimeRecord runtime,
        ModelRecord model,
        AppSettings settings,
        string profileId,
        string profileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StartModelRuntimeAsync(
            runtime,
            model,
            settings,
            interactivePrompts: false,
            launchProfileId: profileId,
            launchProfileName: profileName);
        return _sessions.SessionForModel(model.Id)
            ?? throw new InvalidOperationException($"The runtime for {model.Name} did not create a managed session.");
    }

    private Task StopControlModelAsync(ModelRecord model, CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(() => StopControlModelOnUiAsync(model, cancellationToken)).Task.Unwrap();

    private async Task StopControlModelOnUiAsync(ModelRecord model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_sessions.SessionForModel(model.Id) is { IsRunning: true })
            await StopModelRuntimeAsync(model);
    }

    private Task RefreshControlUiAsync(CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(() => RefreshControlUiOnUiAsync(cancellationToken)).Task.Unwrap();

    private async Task RefreshControlUiOnUiAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RefreshAllAsync();
    }
}
