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
            AppServices.LifetimeMetricsApplication,
            () => AppServices.Benchmarks.Value));
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
        _coreServices.Ui.SettingsAutoApply.Cancel();
        var previousSettings = _settings;
        var previousCulture = _settings.UiCulture;
        var previousRuntimeLogOrder = _settings.RuntimeLogOrder;
        var persisted = await AppServices.SettingsApplication.PersistAsync(settings, cancellationToken);
        _settings = persisted;
        ApplyTrayIconVisibilityPreference();
        ApplyGpuEnergyTrackingBoundary();
        if (previousSettings.StartWithWindows != persisted.StartWithWindows)
            _serviceFactory.CreateWindowsStartupRegistrationService().Apply(persisted.StartWithWindows);
        ApplicationThemeService.Apply(persisted.ThemeMode);
        ApplicationUiScaleService.Apply(persisted.UiScalePercent); ApplicationFontScaleService.Apply(persisted.FontScalePercent);
        if (!string.Equals(previousCulture, persisted.UiCulture, StringComparison.OrdinalIgnoreCase))
        {
            Loc.LoadLanguage(persisted.UiCulture);
            ApplyLocalizedXamlStrings();
            PopulateLanguageSelector();
        }
        ApplyLaunchSettingsToControls();
        if (AppSettingsApplicationService.GatewaySettingsChanged(previousSettings, persisted))
            await RestartModelGatewayAsync();
        if (_viewModel.CurrentPage == "Settings")
            ShowSettings();
        await RefreshAllAsync();
        if (!string.Equals(previousRuntimeLogOrder, persisted.RuntimeLogOrder, StringComparison.OrdinalIgnoreCase))
            await RefreshRuntimeLogOrderAsync();
        SetStatus(Loc.T("Control.SettingsUpdated"));
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
        return _sessions.SessionForProfile(model.Id, profileId)
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
