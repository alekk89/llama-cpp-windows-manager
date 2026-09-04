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
    private async Task LoadSelectedModelAsync(bool restart)
    {
        var model = SelectedModel();
        await _coreServices.Models.ModelRuntimeLoadApplication.LoadSelectedAsync(
            new SelectedModelRuntimeLoadApplicationRequest(
                model,
                restart,
                IsModelLoaded(model),
                IsModelActive(model),
                model is not null && _coreServices.Ui.LaunchSettingsEditor.IsLoadedFor(model.Id, SelectedModelLaunchProfileId()),
                SelectedLaunchRuntimeId(),
                SelectedRuntime()),
            ModelRuntimeLoadActions(
                ReadLaunchSettingsFromControls,
                SelectedModelLaunchProfileId(),
                _modelsPage.SelectedLaunchProfile?.Name ?? ""));
    }

    private async Task LoadOverviewSelectedModelAsync()
    {
        if (SelectedOverviewGroup() is { } group)
        {
            await LoadOverviewGroupAsync(group);
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedOverviewLaunchProfileId()))
        {
            SetStatus(Loc.T("Models.Profile.SelectBeforeLoad"));
            return;
        }
        await LoadOverviewModelAsync(SelectedOverviewModel());
    }

    private async Task LoadOverviewGroupAsync(ModelGroupRecord group)
    {
        await RunResponsiveAsync(Loc.T("ModelGroupStatus.Preparing", group.Name), async () =>
        {
            var stateStore = AppServices.StateStore;
            var groupSnapshot = await ModelServices.ModelGroups.SnapshotAsync();
            var profiles = await stateStore.ListNamedModelLaunchProfilesAsync();
            var models = await stateStore.ListModelsAsync();
            var runtimes = await stateStore.ListRuntimesAsync();
            var sessions = _sessions.Snapshots();
            var needsGpuProbe = groupSnapshot.Assignments.Values
                .Where(assignment => assignment.GroupId.Equals(group.Id, StringComparison.OrdinalIgnoreCase))
                .Select(assignment => profiles.FirstOrDefault(profile => profile.Id.Equals(assignment.LaunchProfileId, StringComparison.OrdinalIgnoreCase)))
                .Where(profile => profile is not null)
                .Any(profile => profile!.Settings.GpuLayers != 0);
            var memory = needsGpuProbe
                ? await _coreServices.App.GpuStatus.MemoryAsync()
                : null;
            var plan = _coreServices.Models.OverviewModelGroupLoadPlanning.Plan(
                group,
                groupSnapshot,
                profiles,
                models,
                runtimes,
                sessions,
                _settings,
                memory);
            if (!plan.CanLoad)
            {
                var message = string.Join(Environment.NewLine, plan.Errors.Select(error => $"• {error}"));
                SetStatus(plan.Errors[0]);
                _coreServices.App.Dialogs.Notify(this, message, Loc.T("ModelGroupStatus.CannotLoad", group.Name), MessageBoxImage.Error);
                return;
            }

            var pending = plan.Targets.Where(target => !target.AlreadyLoaded).ToArray();
            await _coreServices.Models.OverviewModelGroupLoadApplication.ExecuteAsync(
                plan,
                sessions,
                models,
                runtimes,
                new OverviewModelGroupLoadApplicationActions(
                    async (sessionId, _) => await _overviewSelection.UnloadSessionAsync(sessionId),
                    async (runtime, model, settings, profileId, profileName, _) => await StartModelRuntimeAsync(
                        runtime,
                        model,
                        settings,
                        interactivePrompts: false,
                        launchProfileId: profileId,
                        launchProfileName: profileName,
                        selectLoadedOverviewModel: false)));

            await RefreshOverviewModelSelectorAsync();
            _overviewPage.SelectModelId(group.Id);
            UpdateOverviewModelActions();
            SetStatus(pending.Length == 0
                ? Loc.T("ModelGroupStatus.AlreadyLoaded", group.Name)
                : Loc.T(pending.Length == 1 ? "ModelGroupStatus.StartedOne" : "ModelGroupStatus.StartedMany", pending.Length, group.Name));
        });
    }

    private async Task LoadOverviewModelAsync(ModelRecord? model)
    {
        await _coreServices.Models.ModelRuntimeLoadApplication.LoadOverviewAsync(
            new OverviewModelRuntimeLoadApplicationRequest(
                model,
                IsModelLoaded(model),
                IsModelActive(model),
                AppReady: true,
                SelectedProfileLoaded: IsOverviewSelectedProfileLoaded(model)),
            ModelRuntimeLoadActions(
                () => _settings,
                SelectedOverviewLaunchProfileId(),
                _overviewPage.SelectedLaunchProfileName));
    }

    private async Task LoadLaunchProfileAsync(ModelRecord model, NamedModelLaunchProfile profile)
    {
        var selectedProfileLoaded = _sessions.SessionForProfile(model.Id, profile.Id) is { IsRunning: true };
        await _coreServices.Models.ModelRuntimeLoadApplication.LoadOverviewAsync(
            new OverviewModelRuntimeLoadApplicationRequest(
                model,
                IsModelLoaded(model),
                IsModelActive(model),
                AppReady: true,
                SelectedProfileLoaded: selectedProfileLoaded),
            ModelRuntimeLoadActions(() => _settings, profile.Id, profile.Name));
    }

    private async void BeginNewLaunchProfile()
        => await RunEventAsync(_launchSettingsController.BeginNewProfileAsync);

    private ModelRuntimeLoadApplicationActions ModelRuntimeLoadActions(
        Func<AppSettings> readLaunchSettings,
        string launchProfileId,
        string launchProfileName,
        Func<string, Func<Task>, Task>? runBusyAsync = null,
        bool restoreForInteractivePrompts = false)
        => new(
            runBusyAsync ?? RunResponsiveAsync,
            model => SwitchToLoadedModelAsync(model, launchProfileId),
            () => RenderSelectedModelLaunchSettingsAsync(),
            readLaunchSettings,
            ListRuntimesAsync,
            model => DraftModelLaunchProfileAsync(model, launchProfileId),
            model => _overviewSelection.UnloadSessionAsync(LoadedModelSessionManager.SessionIdFor(model.Id, launchProfileId)),
            (runtime, model, launchSettings) => StartModelRuntimeAsync(
                runtime,
                model,
                launchSettings,
                launchProfileId: launchProfileId,
                launchProfileName: launchProfileName,
                restoreForInteractivePrompts: restoreForInteractivePrompts),
            SetStatus);

    private async Task<IReadOnlyList<RuntimeRecord>> ListRuntimesAsync()
        => await AppServices.StateStore.ListRuntimesAsync();

    private ModelRuntimeUnloadApplicationActions ModelRuntimeUnloadActions()
        => new(StopModelRuntimeAsync, SetStatus);

}
