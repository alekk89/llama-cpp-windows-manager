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
    private void ScheduleSelectedModelLaunchSettingsRefresh()
        => _coreServices.Ui.LaunchSettingsRefresh.Schedule(
            RenderSelectedModelLaunchSettingsAsync,
            action => RunBackground(action, "Launch settings refresh failed"));

    private void CancelLaunchSettingsRefresh()
    {
        _coreServices.Ui.LaunchSettingsRefresh.Cancel();
        _runtimeLaunchOptionDiscoveryCancellation?.Cancel();
        _runtimeLaunchOptionDiscoveryCancellation?.Dispose();
        _runtimeLaunchOptionDiscoveryCancellation = null;
    }

    private async Task RenderSelectedModelLaunchSettingsAsync(CancellationToken cancellationToken = default)
    {
        var model = SelectedModel();
        var launchSettings = ModelServices.ModelLaunchSettingsWorkflow;
        await _coreServices.Models.LaunchSettingsRenderApplication.RenderSelectedAsync(
            model,
            _settings,
            new LaunchSettingsRenderActions(
                SelectedModel,
                SelectedModelLaunchProfileId,
                _coreServices.Ui.LaunchSettingsEditor.Clear,
                UpdateSaveAsNewName,
                async (selectedModel, defaults, profileId, token) =>
                {
                    Require(launchSettings);
                    return await launchSettings!.BuildAsync(
                        selectedModel,
                        defaults,
                        token,
                        profileId);
                },
                _coreServices.Ui.LaunchSettingsEditor.Load,
                runtimeId => RefreshRuntimeSelectorAsync(runtimeId),
                ApplyLaunchSettingsToControls,
                ApplyModelCapabilitiesAsync,
                UpdateLaunchSaveButtonState),
            cancellationToken);
    }

    private async Task SaveLaunchSettingsForSelectedModelAsync()
    {
        var selectedProfileId = SelectedModelLaunchProfileId();
        if (string.IsNullOrWhiteSpace(selectedProfileId))
        {
            SetStatus("Select a named launch profile to update it, or enter a name and save a new profile.");
            return;
        }

        await _coreServices.Models.ModelLaunchSettingsSaveApplication.SaveSelectedProfileAsync(
            SelectedModel(),
            ModelLaunchProfileSaveSelectedActions());
        if (!string.IsNullOrWhiteSpace(selectedProfileId))
            await RefreshModelsAsync();
    }

    private async Task SaveLaunchSettingsAsNewModelAsync()
    {
        await _coreServices.Models.ModelLaunchVariantSaveApplication.SaveSelectedAsNewAsync(
            SelectedModel(),
            _launchSettingsPanel.SaveAsNewModelName,
            _settings,
            ModelLaunchVariantSaveSelectedActions());
    }

    private async Task SaveLaunchDefaultsFromControlsAsync()
    {
        await _coreServices.Models.ModelLaunchSettingsSaveApplication.SaveDefaultsFromControlsAsync(LaunchDefaultsSaveFromControlsActions());
    }

    private ModelLaunchProfileSaveSelectedActions ModelLaunchProfileSaveSelectedActions()
        => new(
            RunAsync,
            _coreServices.Ui.LaunchSettingsEditor.IsLoadedFor,
            SelectedModelLaunchProfileId,
            () => RenderSelectedModelLaunchSettingsAsync(),
            ReadLaunchSettingsFromControls,
            SaveModelLaunchProfileAsync,
            ModelLaunchProfileSaveActions());

    private ModelLaunchProfileSaveActions ModelLaunchProfileSaveActions()
        => new(
            _coreServices.Ui.LaunchSettingsEditor.MarkSaved,
            UpdateLaunchSaveButtonState,
            SetStatus);

    private LaunchDefaultsSaveFromControlsActions LaunchDefaultsSaveFromControlsActions()
        => new(
            RunAsync,
            ReadLaunchSettingsFromControls,
            launchDefaults => ModelLaunchSettingsWorkflowService.SaveLaunchDefaults(_settings, launchDefaults),
            LaunchDefaultsSaveActions());

    private LaunchDefaultsSaveActions LaunchDefaultsSaveActions()
        => new(
            settings => _settings = settings,
            PersistSettingsAsync,
            UpdateLaunchSaveButtonState,
            SetStatus);

    private ModelLaunchVariantSaveSelectedActions ModelLaunchVariantSaveSelectedActions()
        => new(
            RunAsync,
            modelId => _coreServices.Ui.LaunchSettingsEditor.IsLoadedFor(modelId, SelectedModelLaunchProfileId()),
            () => RenderSelectedModelLaunchSettingsAsync(),
            ReadLaunchSettingsFromControls,
            SelectedLaunchRuntimeId,
            SaveModelLaunchVariantAsync,
            ModelLaunchVariantSaveActions());

    private ModelLaunchVariantSaveActions ModelLaunchVariantSaveActions()
        => new(
            RefreshModelsAsync,
            SelectLaunchProfileAfterRefresh,
            () => RenderSelectedModelLaunchSettingsAsync(),
            RefreshOverviewModelSelectorAsync,
            SetStatus);

    private async Task<ModelLaunchSettingsSaveResult> SaveModelLaunchProfileAsync(
        ModelRecord model,
        AppSettings launchSettings,
        string profileId)
    {
        var workflow = ModelServices.ModelLaunchSettingsWorkflow;
        Require(workflow);
        return await workflow!.SaveProfileAsync(
            model,
            launchSettings,
            SelectedLaunchRuntimeId(),
            profileId: profileId);
    }

    private async Task<ModelLaunchVariantWorkflowResult> SaveModelLaunchVariantAsync(ModelLaunchVariantWorkflowRequest request)
    {
        var launchVariants = ModelServices.LaunchVariants;
        Require(launchVariants);
        return await launchVariants!.SaveAsNewAsync(request);
    }

    private void ResetLaunchSettingsToDefaults()
    {
        var defaults = AppSettings.CreateDefault(_workspaceRoot);
        ApplyLaunchSettingsToControls(ModelLaunchSettings.FromAppSettings(defaults).ApplyTo(_settings));
        UpdateLaunchSaveButtonState();
        SetStatus("Launch settings reset in the form. Save a new profile, update the selected profile, or save them as the app default to persist them.");
    }

    private Task ChooseVisionProjectorPathAsync()
    {
        _coreServices.Models.ModelLaunchHeadSelectionApplication.ChooseVisionProjector(
            new LaunchHeadSelectionRequest(SelectedModel(), _settings.ModelsRoot),
            LaunchHeadSelectionActions(value =>
            {
                if (_launchSettingsPanel.FormControls.VisionProjectorPathBox is not null)
                    _launchSettingsPanel.FormControls.VisionProjectorPathBox.Text = value;
            }));
        return Task.CompletedTask;
    }

    private Task ChooseMtpHeadPathAsync()
    {
        _coreServices.Models.ModelLaunchHeadSelectionApplication.ChooseMtpHead(
            new LaunchHeadSelectionRequest(SelectedModel(), _settings.ModelsRoot),
            LaunchHeadSelectionActions(value =>
            {
                if (_launchSettingsPanel.FormControls.MtpHeadPathBox is not null)
                    _launchSettingsPanel.FormControls.MtpHeadPathBox.Text = value;
            }));
        return Task.CompletedTask;
    }

    private Task ChooseDraftModelPathAsync()
    {
        _coreServices.Models.ModelLaunchHeadSelectionApplication.ChooseDraftModel(
            new LaunchHeadSelectionRequest(SelectedModel(), _settings.ModelsRoot),
            LaunchHeadSelectionActions(value =>
            {
                if (_launchSettingsPanel.FormControls.SpecDraftModelPathBox is not null)
                    _launchSettingsPanel.FormControls.SpecDraftModelPathBox.Text = value;
            }));
        return Task.CompletedTask;
    }

    private LaunchHeadSelectionActions LaunchHeadSelectionActions(Action<string> applySelectedPath)
        => new(
            request => _coreServices.App.FileSystemDialogs.PickOpenFile(request, this),
            applySelectedPath);

    private AppSettings ReadLaunchSettingsFromControls()
        => LaunchSettingsFormBinder.Read(_settings, _launchSettingsPanel.FormControls);

    private void ApplyLaunchSettingsToControls(AppSettings? source = null)
    {
        _coreServices.Ui.LaunchSettingsEditor.RunProgrammaticUpdate(() =>
            LaunchSettingsFormBinder.Apply(_launchSettingsPanel.FormControls, source ?? _settings));

        UpdateLaunchControlVisibility();
        UpdateLaunchSaveButtonState();
        UpdateRuntimeCommandPreview();
    }

    private void AttachLaunchSettingsChangeHandlers()
    {
        void Changed()
        {
            if (!_coreServices.Ui.LaunchSettingsEditor.IsProgrammaticUpdate)
            {
                UpdateContextSizeSuggestion();
                UpdateLaunchControlVisibility();
                UpdateLaunchSaveButtonState();
                UpdateRuntimeCommandPreview();
            }
        }

        LaunchSettingsFormBinder.AttachChangeHandlers(_launchSettingsPanel.FormControls, Changed, (_, _) => NormalizeContextSizeBox());
    }

    private void UpdateLaunchSaveButtonState()
    {
        var state = BuildLaunchSettingsSaveState();
        var hasNamedProfile = !string.IsNullOrWhiteSpace(SelectedModelLaunchProfileId());
        var content = string.Equals(state.SaveForModelContent, LaunchSettingsSaveStateService.SavedText, StringComparison.Ordinal)
            ? LaunchSettingsSaveStateService.SavedText
            : "Save Profile";
        _launchSettingsPanel.SetSaveForModelState(content, hasNamedProfile && state.CanSaveForModel, hasNamedProfile);
        ApplySaveAsNewButtonState(state);
    }

    private void UpdateSaveAsNewName(ModelRecord? model)
    {
        if (!_coreServices.Ui.LaunchSettingsEditor.TryChangeSaveAsNewSource(model, SelectedModelLaunchProfileId())) return;

        _launchSettingsPanel.SetSaveAsNewModelName("");
        UpdateSaveAsNewButtonState();
    }

    private void UpdateSaveAsNewButtonState()
    {
        ApplySaveAsNewButtonState(BuildLaunchSettingsSaveState(readCurrentProfile: false));
    }

    private LaunchSettingsSaveState BuildLaunchSettingsSaveState(bool readCurrentProfile = true)
    {
        var model = SelectedModel();
        var name = _launchSettingsPanel.SaveAsNewModelName;
        var currentProfileReadable = false;
        ModelLaunchSettings? currentProfile = null;
        if (readCurrentProfile && model is not null && _coreServices.Ui.LaunchSettingsEditor.HasSavedProfile && _coreServices.Ui.LaunchSettingsEditor.SavedProfile is not null)
            currentProfileReadable = TryReadCurrentModelLaunchSettings(out currentProfile);

        return LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            model,
            _coreServices.Ui.LaunchSettingsEditor.HasSavedProfile,
            _coreServices.Ui.LaunchSettingsEditor.SavedProfile,
            currentProfileReadable,
            currentProfile,
            name));
    }

    private void ApplySaveAsNewButtonState(LaunchSettingsSaveState state)
    {
        _launchSettingsPanel.SetSaveAsNewEnabled(state.CanSaveAsNewVariant);
    }

    private bool TryReadCurrentModelLaunchSettings(out ModelLaunchSettings? current)
    {
        current = null;
        try
        {
            current = ModelLaunchSettings.FromAppSettings(ReadLaunchSettingsFromControls(), SelectedLaunchRuntimeId());
            return true;
        }
        catch
        {
            return false;
        }
    }

}
