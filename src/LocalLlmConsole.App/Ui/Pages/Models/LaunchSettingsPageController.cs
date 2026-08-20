namespace LocalLlmConsole;

public sealed record LaunchSettingsPageControllerActions(
    Func<AppSettings> Settings,
    Action<AppSettings> SetSettings,
    Func<ModelRecord?> SelectedModel,
    Func<string> SelectedProfileId,
    Func<string> SelectedRuntimeId,
    Func<MainWindowLoadedModelServices> ModelServices,
    Func<string, Func<Task>, Task> RunBusyAsync,
    Action<Func<Task>, string> RunBackground,
    Func<string?, Task> RefreshRuntimeSelectorAsync,
    Func<ModelRecord?, CancellationToken, Task> ApplyModelCapabilitiesAsync,
    Func<Task> RefreshModelsAsync,
    Action<string> SelectProfileAfterRefresh,
    Func<Task> RefreshOverviewModelsAsync,
    Func<Task> PersistSettingsAsync,
    Action UpdateControlVisibility,
    Action UpdateRuntimeCommandPreview,
    Action UpdateContextSizeSuggestion,
    Action NormalizeContextSize,
    Action CancelRuntimeOptionDiscovery,
    Func<OpenFilePickerRequest, string?> PickOpenFile,
    Action<string> SetStatus);

public sealed class LaunchSettingsPageController
{
    private readonly string _workspaceRoot;
    private readonly LaunchSettingsPanelState _panel;
    private readonly MainWindowCoreUiServices _ui;
    private readonly MainWindowCoreModelServices _models;
    private readonly LaunchSettingsPageControllerActions _actions;

    public LaunchSettingsPageController(
        string workspaceRoot,
        LaunchSettingsPanelState panel,
        MainWindowCoreUiServices ui,
        MainWindowCoreModelServices models,
        LaunchSettingsPageControllerActions actions)
    {
        _workspaceRoot = workspaceRoot;
        _panel = panel;
        _ui = ui;
        _models = models;
        _actions = actions;
    }

    public void ScheduleSelectedModelRefresh()
        => _ui.LaunchSettingsRefresh.Schedule(
            RenderSelectedAsync,
            action => _actions.RunBackground(action, "Launch settings refresh failed"));

    public void CancelRefresh()
    {
        _ui.LaunchSettingsRefresh.Cancel();
        _ui.LaunchSettingsInputRefresh.Cancel();
        _actions.CancelRuntimeOptionDiscovery();
    }

    public async Task RenderSelectedAsync(CancellationToken cancellationToken = default)
    {
        var model = _actions.SelectedModel();
        var workflow = _actions.ModelServices().ModelLaunchSettingsWorkflow;
        await _models.LaunchSettingsRenderApplication.RenderSelectedAsync(
            model,
            _actions.Settings(),
            new LaunchSettingsRenderActions(
                _actions.SelectedModel,
                _actions.SelectedProfileId,
                _ui.LaunchSettingsEditor.Clear,
                UpdateSaveAsNewName,
                (selectedModel, defaults, profileId, token) => workflow.BuildAsync(
                    selectedModel,
                    defaults,
                    token,
                    profileId),
                _ui.LaunchSettingsEditor.Load,
                runtimeId => _actions.RefreshRuntimeSelectorAsync(runtimeId),
                ApplyToControls,
                _actions.ApplyModelCapabilitiesAsync,
                UpdateSaveButtonState),
            cancellationToken);
    }

    public async Task SaveSelectedProfileAsync()
    {
        var selectedProfileId = _actions.SelectedProfileId();
        if (string.IsNullOrWhiteSpace(selectedProfileId))
        {
            _actions.SetStatus("Select a named launch profile to update it, or enter a name and save a new profile.");
            return;
        }

        await _models.ModelLaunchSettingsSaveApplication.SaveSelectedProfileAsync(
            _actions.SelectedModel(),
            new ModelLaunchProfileSaveSelectedActions(
                _actions.RunBusyAsync,
                _ui.LaunchSettingsEditor.IsLoadedFor,
                _actions.SelectedProfileId,
                () => RenderSelectedAsync(),
                ReadFromControls,
                SaveProfileAsync,
                new ModelLaunchProfileSaveActions(
                    _ui.LaunchSettingsEditor.MarkSaved,
                    UpdateSaveButtonState,
                    _actions.SetStatus)));
        await _actions.RefreshModelsAsync();
    }

    public async Task SaveAsNewModelAsync()
    {
        await _models.ModelLaunchVariantSaveApplication.SaveSelectedAsNewAsync(
            _actions.SelectedModel(),
            _panel.SaveAsNewModelName,
            _actions.Settings(),
            new ModelLaunchVariantSaveSelectedActions(
                _actions.RunBusyAsync,
                modelId => _ui.LaunchSettingsEditor.IsLoadedFor(modelId, _actions.SelectedProfileId()),
                () => RenderSelectedAsync(),
                ReadFromControls,
                _actions.SelectedRuntimeId,
                request => _actions.ModelServices().LaunchVariants.SaveAsNewAsync(request),
                new ModelLaunchVariantSaveActions(
                    _actions.RefreshModelsAsync,
                    _actions.SelectProfileAfterRefresh,
                    () => RenderSelectedAsync(),
                    _actions.RefreshOverviewModelsAsync,
                    _actions.SetStatus)));
    }

    public async Task SaveDefaultsAsync()
    {
        await _models.ModelLaunchSettingsSaveApplication.SaveDefaultsFromControlsAsync(
            new LaunchDefaultsSaveFromControlsActions(
                _actions.RunBusyAsync,
                ReadFromControls,
                launchDefaults => ModelLaunchSettingsWorkflowService.SaveLaunchDefaults(_actions.Settings(), launchDefaults),
                new LaunchDefaultsSaveActions(
                    _actions.SetSettings,
                    _actions.PersistSettingsAsync,
                    UpdateSaveButtonState,
                    _actions.SetStatus)));
    }

    private async Task<ModelLaunchSettingsSaveResult> SaveProfileAsync(
        ModelRecord model,
        AppSettings launchSettings,
        string profileId)
        => await _actions.ModelServices().ModelLaunchSettingsWorkflow.SaveProfileAsync(
            model,
            launchSettings,
            _actions.SelectedRuntimeId(),
            profileId: profileId);

    public void ResetToDefaults()
    {
        var defaults = AppSettings.CreateDefault(_workspaceRoot);
        ApplyToControls(ModelLaunchSettings.FromAppSettings(defaults).ApplyTo(_actions.Settings()));
        UpdateSaveButtonState();
        _actions.SetStatus("Launch settings reset in the form. Save a new profile, update the selected profile, or save them as the app default to persist them.");
    }

    public Task ChooseVisionProjectorAsync()
        => ChooseHeadAsync(
            (request, actions) => _models.ModelLaunchHeadSelectionApplication.ChooseVisionProjector(request, actions),
            value =>
            {
                if (_panel.FormControls.VisionProjectorPathBox is not null)
                    _panel.FormControls.VisionProjectorPathBox.Text = value;
            });

    public Task ChooseMtpHeadAsync()
        => ChooseHeadAsync(
            (request, actions) => _models.ModelLaunchHeadSelectionApplication.ChooseMtpHead(request, actions),
            value =>
            {
                if (_panel.FormControls.MtpHeadPathBox is not null)
                    _panel.FormControls.MtpHeadPathBox.Text = value;
            });

    public Task ChooseDraftModelAsync()
        => ChooseHeadAsync(
            (request, actions) => _models.ModelLaunchHeadSelectionApplication.ChooseDraftModel(request, actions),
            value =>
            {
                if (_panel.FormControls.SpecDraftModelPathBox is not null)
                    _panel.FormControls.SpecDraftModelPathBox.Text = value;
            });

    private Task ChooseHeadAsync(
        Action<LaunchHeadSelectionRequest, LaunchHeadSelectionActions> choose,
        Action<string> applySelectedPath)
    {
        choose(
            new LaunchHeadSelectionRequest(_actions.SelectedModel(), _actions.Settings().ModelsRoot),
            new LaunchHeadSelectionActions(_actions.PickOpenFile, applySelectedPath));
        return Task.CompletedTask;
    }

    public AppSettings ReadFromControls()
        => LaunchSettingsFormBinder.Read(_actions.Settings(), _panel.FormControls);

    public void ApplyToControls(AppSettings? source = null)
    {
        _ui.LaunchSettingsEditor.RunProgrammaticUpdate(() =>
            LaunchSettingsFormBinder.Apply(_panel.FormControls, source ?? _actions.Settings()));
        _actions.UpdateControlVisibility();
        UpdateSaveButtonState();
        _actions.UpdateRuntimeCommandPreview();
    }

    public void AttachChangeHandlers()
    {
        void Changed()
        {
            if (_ui.LaunchSettingsEditor.IsProgrammaticUpdate) return;
            _actions.UpdateContextSizeSuggestion();
            ScheduleInputRefresh();
        }

        LaunchSettingsFormBinder.AttachChangeHandlers(_panel.FormControls, Changed, (_, _) => _actions.NormalizeContextSize());
    }

    public void ScheduleInputRefresh()
        => _ui.LaunchSettingsInputRefresh.Schedule(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _actions.UpdateControlVisibility();
                UpdateSaveButtonState();
                _actions.UpdateRuntimeCommandPreview();
                return Task.CompletedTask;
            },
            action => _actions.RunBackground(action, "Launch settings input refresh failed"));

    public void UpdateSaveButtonState()
    {
        var state = BuildSaveState();
        var hasNamedProfile = !string.IsNullOrWhiteSpace(_actions.SelectedProfileId());
        var content = string.Equals(state.SaveForModelContent, LaunchSettingsSaveStateService.SavedText, StringComparison.Ordinal)
            ? LaunchSettingsSaveStateService.SavedText
            : "Save Profile";
        _panel.SetSaveForModelState(content, hasNamedProfile && state.CanSaveForModel, hasNamedProfile);
        _panel.SetSaveAsNewEnabled(state.CanSaveAsNewVariant);
    }

    private void UpdateSaveAsNewName(ModelRecord? model)
    {
        if (!_ui.LaunchSettingsEditor.TryChangeSaveAsNewSource(model, _actions.SelectedProfileId())) return;
        _panel.SetSaveAsNewModelName("");
        _panel.SetSaveAsNewEnabled(BuildSaveState(readCurrentProfile: false).CanSaveAsNewVariant);
    }

    private LaunchSettingsSaveState BuildSaveState(bool readCurrentProfile = true)
    {
        var model = _actions.SelectedModel();
        var currentProfileReadable = false;
        ModelLaunchSettings? currentProfile = null;
        if (readCurrentProfile && model is not null && _ui.LaunchSettingsEditor.HasSavedProfile && _ui.LaunchSettingsEditor.SavedProfile is not null)
        {
            try
            {
                currentProfile = ModelLaunchSettings.FromAppSettings(ReadFromControls(), _actions.SelectedRuntimeId());
                currentProfileReadable = true;
            }
            catch
            {
                currentProfile = null;
            }
        }

        return LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            model,
            _ui.LaunchSettingsEditor.HasSavedProfile,
            _ui.LaunchSettingsEditor.SavedProfile,
            currentProfileReadable,
            currentProfile,
            _panel.SaveAsNewModelName));
    }
}
