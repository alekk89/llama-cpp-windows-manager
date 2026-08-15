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

    private async Task UnloadSelectedModelAsync()
    {
        var model = SelectedModel();
        await _coreServices.Models.ModelRuntimeUnloadApplication.UnloadSelectedAsync(
            new ModelRuntimeUnloadApplicationRequest(model, IsModelLoaded(model)),
            ModelRuntimeUnloadActions());
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
            SetStatus("Select a launch profile before loading the model.");
            return;
        }
        await LoadOverviewModelAsync(SelectedOverviewModel());
    }

    private async Task LoadOverviewGroupAsync(ModelGroupRecord group)
    {
        await RunResponsiveAsync($"Preparing {group.Name}...", async () =>
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
                _coreServices.App.Dialogs.Notify(this, message, $"Cannot load {group.Name}", MessageBoxImage.Error);
                return;
            }

            var pending = plan.Targets.Where(target => !target.AlreadyLoaded).ToArray();
            await _coreServices.Models.OverviewModelGroupLoadApplication.ExecuteAsync(
                plan,
                sessions,
                models,
                runtimes,
                new OverviewModelGroupLoadApplicationActions(
                    async (model, _) => await StopModelRuntimeAsync(model),
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
                ? $"All models in {group.Name} are already loaded."
                : $"Started {pending.Length} model{(pending.Length == 1 ? "" : "s")} from {group.Name}.");
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

    private ModelRuntimeLoadApplicationActions ModelRuntimeLoadActions(
        Func<AppSettings> readLaunchSettings,
        string launchProfileId,
        string launchProfileName)
        => new(
            RunResponsiveAsync,
            SwitchToLoadedModelAsync,
            () => RenderSelectedModelLaunchSettingsAsync(),
            readLaunchSettings,
            ListRuntimesAsync,
            model => DraftModelLaunchProfileAsync(model, SelectedOverviewLaunchProfileId()),
            StopModelRuntimeAsync,
            (runtime, model, launchSettings) => StartModelRuntimeAsync(
                runtime,
                model,
                launchSettings,
                launchProfileId: launchProfileId,
                launchProfileName: launchProfileName),
            SetStatus);

    private async Task<IReadOnlyList<RuntimeRecord>> ListRuntimesAsync()
        => await AppServices.StateStore.ListRuntimesAsync();

    private ModelRuntimeUnloadApplicationActions ModelRuntimeUnloadActions()
        => new(StopModelRuntimeAsync, SetStatus);

}
