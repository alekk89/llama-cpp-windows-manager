namespace LocalLlmConsole;

public sealed record LaunchSettingsPageControllerActions(
    Func<AppSettings> Settings,
    Action<AppSettings> SetSettings,
    Func<ModelRecord?> SelectedModel,
    Func<string> SelectedProfileId,
    Func<string> SelectedRuntimeId,
    Func<RuntimeChoice?> SelectedRuntime,
    Func<MainWindowLoadedModelServices> ModelServices,
    Func<string, Func<Task>, Task> RunBusyAsync,
    Action<Func<Task>, string> RunBackground,
    Func<string?, Task> RefreshRuntimeSelectorAsync,
    Func<ModelRecord?, CancellationToken, Task> ApplyModelCapabilitiesAsync,
    Func<Task> RefreshModelsAsync,
    Action<string> SelectProfileAfterRefresh,
    Action<string, string?> SelectModelAfterRefresh,
    Func<Task> RefreshOverviewModelsAsync,
    Func<Task> PersistSettingsAsync,
    Action UpdateControlVisibility,
    Action UpdateRuntimeCommandPreview,
    Action UpdateContextSizeSuggestion,
    Action NormalizeContextSize,
    Action CancelRuntimeOptionDiscovery,
    Func<OpenFilePickerRequest, string?> PickOpenFile,
    Action<BenchmarkPlan> OpenBenchmarkPlan,
    Action ShowModels,
    Action<string> OpenLog,
    Action<string> SetStatus,
    Func<LaunchSettingsUnsavedChoice>? ConfirmUnsavedEdits = null,
    Func<ModelRecord, AppSettings, string, string, Task<ModelLaunchSettingsSaveResult>>? SaveProfileAsync = null);

public enum LaunchSettingsUnsavedChoice
{
    Save,
    Discard,
    Cancel
}

public sealed class LaunchSettingsPageController
{
    private readonly string _workspaceRoot;
    private readonly LaunchSettingsPanelState _panel;
    private readonly MainWindowCoreUiServices _ui;
    private readonly MainWindowCoreModelServices _models;
    private readonly MainWindowCoreRuntimeServices _runtimes;
    private readonly LaunchSettingsPageControllerActions _actions;
    private ModelRecord? _editorModel;
    private LaunchSettingsFormControls? _editorControls;
    private int _leavePromptSuppression;
    private bool _savingBeforeLeave;

    public LaunchSettingsPageController(
        string workspaceRoot,
        LaunchSettingsPanelState panel,
        MainWindowCoreUiServices ui,
        MainWindowCoreModelServices models,
        MainWindowCoreRuntimeServices runtimes,
        LaunchSettingsPageControllerActions actions)
    {
        _workspaceRoot = workspaceRoot;
        _panel = panel;
        _ui = ui;
        _models = models;
        _runtimes = runtimes;
        _actions = actions;
    }

    public void ScheduleSelectedModelRefresh()
        => _ui.LaunchSettingsRefresh.Schedule(
            RenderSelectedAsync,
            action => _actions.RunBackground(action, Loc.T("Status.Failure.LaunchSettingsRefresh")));

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
                viewState => LoadEditor(model, viewState),
                runtimeId => _actions.RefreshRuntimeSelectorAsync(runtimeId),
                ApplyToControls,
                _actions.ApplyModelCapabilitiesAsync,
                UpdateSaveButtonState,
                HasUnsavedEditsFor),
            cancellationToken);
    }

    /// <summary>Records which model, profile and form the editor now shows.</summary>
    internal void LoadEditor(ModelRecord? model, ModelLaunchSettingsViewState viewState)
    {
        _ui.LaunchSettingsEditor.Load(viewState);
        _editorModel = model;
        _editorControls = _panel.FormControls;
    }

    public async Task SaveSelectedProfileAsync()
    {
        var selectedProfileId = _actions.SelectedProfileId();
        if (string.IsNullOrWhiteSpace(selectedProfileId))
        {
            _actions.SetStatus(Loc.T("Models.Profile.SelectOrCreate"));
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

    public async Task BeginNewProfileAsync()
    {
        var model = _actions.SelectedModel();
        if (model is null) return;
        var profileId = _actions.SelectedProfileId();
        CancelRefresh();
        if (!_ui.LaunchSettingsEditor.IsLoadedFor(model.Id, profileId)) await RenderSelectedAsync();
        var runtimeId = await _actions.ModelServices().LaunchProfiles.DefaultRuntimeIdAsync();
        if (_actions.SelectedModel()?.Id != model.Id || _actions.SelectedProfileId() != profileId) return;
        if (!string.IsNullOrEmpty(runtimeId)) await _actions.RefreshRuntimeSelectorAsync(runtimeId);
        _panel.SetSaveAsNewModelName("");
        _panel.FocusSaveAsNewModelName();
        UpdateSaveButtonState();
        _actions.SetStatus(Loc.T("Models.Profile.NewInstructions"));
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
        => _actions.SaveProfileAsync is { } save
            ? await save(model, launchSettings, _actions.SelectedRuntimeId(), profileId)
            : await _actions.ModelServices().ModelLaunchSettingsWorkflow.SaveProfileAsync(
                model,
                launchSettings,
                _actions.SelectedRuntimeId(),
                profileId: profileId);

    public void ResetToDefaults()
    {
        var defaults = AppSettings.CreateDefault(_workspaceRoot);
        ApplyToControls(ModelLaunchSettings.FromAppSettings(defaults).ApplyTo(_actions.Settings()));
        UpdateSaveButtonState();
        _actions.SetStatus(Loc.T("Launch.ResetInstructions"));
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
            action => _actions.RunBackground(action, Loc.T("Status.Failure.LaunchSettingsInputRefresh")));

    public void UpdateSaveButtonState()
    {
        var state = BuildSaveState();
        var hasNamedProfile = !string.IsNullOrWhiteSpace(_actions.SelectedProfileId());
        var content = string.Equals(state.SaveForModelContent, LaunchSettingsSaveStateService.SavedText, StringComparison.Ordinal)
            ? Loc.T("Launch.SavedButton")
            : Loc.T("Launch.SaveForModelButton");
        _panel.SetSaveForModelState(content, hasNamedProfile && state.CanSaveForModel, hasNamedProfile);
        _panel.SetSaveAsNewEnabled(state.CanSaveAsNewVariant);
        _panel.SetUnsavedChangesVisible(hasNamedProfile && state.HasUnsavedChanges && ReferenceEquals(_editorControls, _panel.FormControls));
    }

    /// <summary>
    /// True when the form still shows the profile it was loaded for and differs from its saved
    /// state. A recreated panel no longer holds the edits, so it never counts as changed.
    /// </summary>
    public bool HasUnsavedEdits()
    {
        var editor = _ui.LaunchSettingsEditor;
        if (string.IsNullOrEmpty(editor.ModelId) || string.IsNullOrWhiteSpace(editor.ProfileId)
            || !editor.HasSavedProfile || editor.SavedProfile is null) return false;
        if (!ReferenceEquals(_editorControls, _panel.FormControls)) return false;
        return BuildSaveState(model: _editorModel ?? _actions.SelectedModel()).HasUnsavedChanges;
    }

    public bool HasUnsavedEditsFor(string modelId, string profileId)
        => (_savingBeforeLeave || _ui.LaunchSettingsEditor.IsLoadedFor(modelId, profileId)) && HasUnsavedEdits();

    /// <summary>Suppresses the unsaved-changes prompt while the app itself restores a selection.</summary>
    public IDisposable SuppressLeavePrompt()
    {
        _leavePromptSuppression++;
        return new LeavePromptSuppression(this);
    }

    /// <summary>
    /// True when moving to the next model and profile (or, with nulls, away from the page) would
    /// drop unsaved edits, so <see cref="ConfirmLeavingEditsAsync"/> must ask first.
    /// </summary>
    public bool NeedsLeaveConfirmation(string? nextModelId = null, string? nextProfileId = null)
    {
        if (_leavePromptSuppression > 0) return false;
        if (nextModelId is not null && _ui.LaunchSettingsEditor.IsLoadedFor(nextModelId, nextProfileId ?? "")) return false;
        return HasUnsavedEdits();
    }

    /// <summary>
    /// Asks Save, Discard or Cancel before the editor leaves its profile with unsaved edits. Save
    /// completes before this returns; a failed save is reported like the Save button's and
    /// returns false with the edits kept, as does Cancel, so the caller stays where it is.
    /// </summary>
    public async Task<bool> ConfirmLeavingEditsAsync(string? nextModelId = null, string? nextProfileId = null)
    {
        if (!NeedsLeaveConfirmation(nextModelId, nextProfileId)) return true;
        var choice = (_actions.ConfirmUnsavedEdits ?? LaunchSettingsUnsavedChangesDialog.Show)();
        if (choice == LaunchSettingsUnsavedChoice.Cancel) return false;
        if (choice == LaunchSettingsUnsavedChoice.Discard)
        {
            _editorControls = null;
            _panel.SetUnsavedChangesVisible(false);
            _actions.SetStatus(Loc.T("Launch.UnsavedChanges.Discarded"));
            return true;
        }
        return await SaveEditsBeforeLeavingAsync();
    }

    private async Task<bool> SaveEditsBeforeLeavingAsync()
    {
        var model = _editorModel;
        var editor = _ui.LaunchSettingsEditor;
        if (model is null || !string.Equals(model.Id, editor.ModelId, StringComparison.OrdinalIgnoreCase)) return false;
        var profileId = editor.ProfileId;
        var saved = false;
        // While the write runs, a background refresh keeps the edited form even though another row is selected.
        _savingBeforeLeave = true;
        try
        {
            await _actions.RunBusyAsync(Loc.T("Launch.Busy.SavingProfile"), async () =>
            {
                var result = await SaveProfileAsync(model, ReadFromControls(), profileId);
                editor.MarkSaved(model.Id, profileId, result.SavedSettings);
                UpdateSaveButtonState();
                _actions.SetStatus(result.StatusMessage);
                saved = true;
            });
        }
        finally
        {
            _savingBeforeLeave = false;
        }
        if (saved) _actions.RunBackground(_actions.RefreshModelsAsync, Loc.T("Status.Failure.ModelsRefresh"));
        return saved;
    }

    private sealed class LeavePromptSuppression(LaunchSettingsPageController owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._leavePromptSuppression--;
        }
    }

    public Task FitSelectedProfileToAvailableVramAsync()
        => _actions.RunBusyAsync(Loc.T("Launch.Busy.FittingProfile"), FitSelectedProfileCoreAsync);

    public void ScheduleProfileFitCapabilityProbe()
    {
        var runtime = _actions.SelectedRuntime();
        if (runtime is null) { _panel.SetProfileFitCapability(false, Loc.T("Launch.SelectRuntimeFirst")); return; }
        if (runtime.Backend == RuntimeBackend.Cpu) { _panel.SetProfileFitCapability(false, Loc.T("ProfileFit.Capability.NoGpu")); return; }
        var record = CreateRuntimeRecord(runtime);
        var executable = ProfileFitCapabilityService.ResolveExecutable(record);
        if (runtime.Mode == RuntimeMode.Native && !File.Exists(executable))
        {
            _panel.SetProfileFitCapability(false, Loc.T("ProfileFit.Capability.Missing"));
            return;
        }
        _panel.SetProfileFitCapability(false, Loc.T("ProfileFit.Capability.Checking"));
        _actions.RunBackground(() => ProbeProfileFitCapabilityAsync(record), Loc.T("Status.Failure.ProfileFitProbe"));
    }

    public async Task OfferOutOfMemoryRecoveryAsync(LoadedModelSessionSnapshot session)
    {
        var capture = await _runtimes.RuntimeLogTail.CaptureAsync(session.LogPath, 80_000);
        if (!RuntimeOutOfMemoryClassifier.IsOutOfMemory(session.StatusReason, capture.RawTail)) return;
        var owner = System.Windows.Application.Current.MainWindow;
        var action = OutOfMemoryRecoveryDialog.Show(owner, session.ModelName);
        if (action == OutOfMemoryRecoveryAction.ViewLog) { _actions.OpenLog(session.LogPath); return; }
        if (action is not (OutOfMemoryRecoveryAction.CreateFittedProfile or OutOfMemoryRecoveryAction.EditMemorySettings)) return;
        _actions.ShowModels();
        await _actions.RefreshModelsAsync();
        _actions.SelectModelAfterRefresh(session.ModelId, session.LaunchProfileId);
        await RenderSelectedAsync();
        if (action == OutOfMemoryRecoveryAction.CreateFittedProfile) await FitSelectedProfileCoreAsync();
        else _actions.SetStatus(Loc.T("ProfileFit.ReviewBeforeRetry"));
    }

    private async Task FitSelectedProfileCoreAsync()
    {
        var model = _actions.SelectedModel() ?? throw new InvalidOperationException(Loc.T("Launch.SelectModelFirst"));
        var runtime = _actions.SelectedRuntime() ?? throw new InvalidOperationException(Loc.T("Launch.SelectRuntimeFirst"));
        if (runtime.Backend == RuntimeBackend.Cpu) throw new InvalidOperationException(Loc.T("ProfileFit.RequiresGpu"));
        var settings = ReadFromControls();
        var input = ProfileFitDialog.ShowInput(System.Windows.Application.Current.MainWindow, settings.ContextSize);
        if (input is null) return;
        var current = ModelLaunchSettings.FromAppSettings(settings, runtime.Id);
        var gpuCount = Math.Max(1, Math.Max(CsvCount(current.GpuDevices), CsvCount(current.GpuSplit)));
        var result = await _runtimes.ProfileFit.FitAsync(new ProfileFitRequest(
            model.ModelPath, CreateRuntimeRecord(runtime), current, input.DesiredMaximumContext, input.MinimumContext,
            Enumerable.Repeat(input.ReservedVramMiB, gpuCount).ToArray(), _actions.Settings().WslDistro));
        if (!result.Success || result.Proposal is null) throw new InvalidOperationException(result.Error);
        var action = ProfileFitDialog.ShowPreview(System.Windows.Application.Current.MainWindow, current, result);
        if (action == ProfileFitPreviewAction.Cancel) return;
        var fitted = current with
        {
            ContextSize = result.Proposal.ContextSize,
            GpuLayers = result.Proposal.GpuLayers,
            GpuSplit = result.Proposal.GpuSplit,
            TensorBufferOverrides = result.Proposal.TensorBufferOverrides
        };
        var fittedSettings = fitted.ApplyTo(settings);
        if (action == ProfileFitPreviewAction.ApplyTemporarily)
        {
            ApplyToControls(fittedSettings);
            _actions.SetStatus(Loc.T("ProfileFit.AppliedTemporarily"));
            return;
        }
        var originalProfileId = _actions.SelectedProfileId();
        var saved = await _actions.ModelServices().LaunchVariants.SaveAsNewAsync(new ModelLaunchVariantWorkflowRequest(
            model, await UniqueFittedProfileNameAsync(model, gpuCount), fittedSettings, runtime.Id, _actions.Settings()));
        if (!saved.Success || saved.Profile is null) throw new InvalidOperationException(saved.StatusMessage);
        _actions.SelectProfileAfterRefresh(saved.Profile.Id);
        await _actions.RefreshModelsAsync();
        await _actions.RefreshOverviewModelsAsync();
        _actions.SetStatus(saved.StatusMessage);
        if (action == ProfileFitPreviewAction.SaveAndBenchmark)
            _actions.OpenBenchmarkPlan(ProfileFitBenchmarkPlanService.Create(model, originalProfileId, saved.Profile,
                _actions.Settings().BenchmarkStopActiveSessions, _actions.Settings().BenchmarkPreventSystemSleep));
    }

    private async Task<string> UniqueFittedProfileNameAsync(ModelRecord model, int gpuCount)
    {
        var stem = Loc.T("ProfileFit.FittedProfileName", model.Name, gpuCount);
        var names = (await _actions.ModelServices().LaunchProfiles.ListNamedAsync(model)).Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(stem)) return stem;
        for (var suffix = 2; suffix < 1000; suffix++) if (!names.Contains($"{stem} ({suffix})")) return $"{stem} ({suffix})";
        return $"{stem} {DateTimeOffset.Now:yyyyMMdd-HHmmss}";
    }

    private async Task ProbeProfileFitCapabilityAsync(RuntimeRecord runtime)
    {
        var capability = await _runtimes.ProfileFitCapabilities.ProbeAsync(runtime, _actions.Settings().WslDistro);
        if (string.Equals(_actions.SelectedRuntimeId(), runtime.Id, StringComparison.OrdinalIgnoreCase))
            _panel.SetProfileFitCapability(capability.SupportsFitParams, capability.SupportsFitParams ? null : capability.Error);
    }

    private static RuntimeRecord CreateRuntimeRecord(RuntimeChoice runtime)
        => new(runtime.Id, runtime.DisplayName, runtime.Mode, runtime.Backend, runtime.ExecutablePath, "{}", DateTimeOffset.UtcNow);

    private static int CsvCount(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    private void UpdateSaveAsNewName(ModelRecord? model)
    {
        if (!_ui.LaunchSettingsEditor.TryChangeSaveAsNewSource(model, _actions.SelectedProfileId())) return;
        _panel.SetSaveAsNewModelName("");
        _panel.SetSaveAsNewEnabled(BuildSaveState(readCurrentProfile: false).CanSaveAsNewVariant);
    }

    private LaunchSettingsSaveState BuildSaveState(bool readCurrentProfile = true, ModelRecord? model = null)
    {
        model ??= _actions.SelectedModel();
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
