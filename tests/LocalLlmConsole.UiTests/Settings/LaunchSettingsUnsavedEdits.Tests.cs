using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class WpfLaunchSettingsUnsavedEditsTests : WpfUiTestBase
{
    [Fact]
    public async Task ChoiceCombosKeepUnknownSavedValuesAndDropThemForKnownOnes()
        => await RunStaAsync(() =>
        {
            var settings = AppSettings.CreateDefault(TestWorkspace);
            var (_, panelState) = CreatePanel(settings);
            var controls = panelState.FormControls;

            LaunchSettingsFormBinder.Apply(controls, settings with
            {
                SpeculativeType = "draft-future",
                ReasoningMode = "experimental",
                FlashAttention = "turbo"
            });

            Assert.Equal("draft-future", controls.SpeculativeTypeCombo!.SelectedItem);
            Assert.Equal("experimental", controls.ReasoningCombo!.SelectedItem);
            Assert.Equal("turbo", controls.FlashAttentionCombo!.SelectedItem);
            var read = LaunchSettingsFormBinder.Read(settings, controls);
            Assert.Equal("draft-future", read.SpeculativeType);
            Assert.Equal("experimental", read.ReasoningMode);
            Assert.Equal("turbo", read.FlashAttention);

            LaunchSettingsFormBinder.Apply(controls, settings with { FlashAttention = "on" });
            Assert.Equal("on", controls.FlashAttentionCombo.SelectedItem);
            Assert.DoesNotContain("turbo", controls.FlashAttentionCombo.Items.Cast<object>().Select(item => item.ToString()));
            Assert.DoesNotContain("draft-future", controls.SpeculativeTypeCombo.Items.Cast<object>().Select(item => item.ToString()));
        });

    [Fact]
    public async Task EmptyChoiceComboListsTheSavedValueInsteadOfThrowing()
        => await RunStaAsync(() =>
        {
            var reasoning = new ComboBox { ItemsSource = Array.Empty<string>() };
            var controls = new LaunchSettingsFormControls(new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
            {
                [nameof(AppSettings.ReasoningMode)] = reasoning
            });
            var settings = AppSettings.CreateDefault(TestWorkspace);

            LaunchSettingsFormBinder.Apply(controls, settings with { ReasoningMode = "" });
            Assert.Null(reasoning.SelectedItem);

            LaunchSettingsFormBinder.Apply(controls, settings with { ReasoningMode = "custom" });
            Assert.Equal("custom", reasoning.SelectedItem);
        });

    [Fact]
    public async Task UnsavedEditsShowAMarkerAndAskBeforeLeavingTheProfile()
        => await RunStaAsync(async () =>
        {
            var harness = new Harness();
            var controller = harness.LoadEdited(editContext: false);

            Assert.False(controller.HasUnsavedEdits());
            Assert.Equal(Visibility.Collapsed, harness.Panel.UnsavedChangesText!.Visibility);
            Assert.True(await controller.ConfirmLeavingEditsAsync("model-2", "profile-2"));
            Assert.Equal(0, harness.Prompts);

            harness.EditContext();

            Assert.True(controller.HasUnsavedEdits());
            Assert.True(controller.HasUnsavedEditsFor(harness.Model.Id, "profile-1"));
            Assert.False(controller.HasUnsavedEditsFor(harness.Model.Id, "profile-2"));
            Assert.Equal(Visibility.Visible, harness.Panel.UnsavedChangesText.Visibility);
            Assert.Equal(Loc.T("Launch.UnsavedChanges"), harness.Panel.UnsavedChangesText.Text);

            Assert.False(controller.NeedsLeaveConfirmation(harness.Model.Id, "profile-1"));
            Assert.True(await controller.ConfirmLeavingEditsAsync(harness.Model.Id, "profile-1"));
            using (controller.SuppressLeavePrompt())
                Assert.True(await controller.ConfirmLeavingEditsAsync("model-2", "profile-2"));
            Assert.Equal(0, harness.Prompts);

            Assert.True(controller.NeedsLeaveConfirmation("model-2", "profile-2"));
            Assert.False(await controller.ConfirmLeavingEditsAsync("model-2", "profile-2"));
            Assert.False(await controller.ConfirmLeavingEditsAsync());
            Assert.Equal(2, harness.Prompts);
            Assert.True(controller.HasUnsavedEdits());

            harness.Answer = LaunchSettingsUnsavedChoice.Discard;
            Assert.True(await controller.ConfirmLeavingEditsAsync());
            Assert.False(controller.HasUnsavedEdits());
            Assert.Equal(Visibility.Collapsed, harness.Panel.UnsavedChangesText.Visibility);
            Assert.Empty(harness.SavedProfiles);
        });

    [Fact]
    public async Task SaveCompletesBeforeLeavingTheProfile()
        => await RunStaAsync(async () =>
        {
            var harness = new Harness { Answer = LaunchSettingsUnsavedChoice.Save };
            var controller = harness.LoadEdited();
            var write = new TaskCompletionSource();
            harness.SaveGate = write.Task;

            var leaving = controller.ConfirmLeavingEditsAsync("model-2", "profile-2");

            Assert.False(leaving.IsCompleted);
            Assert.True(controller.HasUnsavedEditsFor("model-2", "profile-2"));
            write.SetResult();
            Assert.True(await leaving);
            var saved = Assert.Single(harness.SavedProfiles);
            Assert.Equal(("model-1", "profile-1", harness.EditedContext), (saved.ModelId, saved.ProfileId, saved.Settings.ContextSize));
            Assert.False(controller.HasUnsavedEdits());
            Assert.False(controller.HasUnsavedEditsFor("model-2", "profile-2"));
            Assert.Equal(["Models refresh failed"], harness.BackgroundWork);
            Assert.Contains("saved", harness.Statuses);
        });

    [Fact]
    public async Task FailedSaveKeepsTheEditsAndTheCurrentProfile()
        => await RunStaAsync(async () =>
        {
            var harness = new Harness
            {
                Answer = LaunchSettingsUnsavedChoice.Save,
                SaveFailure = new InvalidOperationException("Database is locked.")
            };
            var controller = harness.LoadEdited();

            Assert.False(await controller.ConfirmLeavingEditsAsync("model-2", "profile-2"));
            Assert.False(await controller.ConfirmLeavingEditsAsync());

            Assert.Equal(["Database is locked.", "Database is locked."], harness.BusyErrors);
            Assert.Empty(harness.SavedProfiles);
            Assert.Empty(harness.BackgroundWork);
            Assert.True(controller.HasUnsavedEdits());
            Assert.Equal(harness.EditedContext.ToString(System.Globalization.CultureInfo.InvariantCulture),
                harness.Panel.FormControls.ContextSizeBox!.Text);
            Assert.Equal(Visibility.Visible, harness.Panel.UnsavedChangesText!.Visibility);
        });

    [Fact]
    public async Task InvalidFormBlocksSavingBeforeLeaving()
        => await RunStaAsync(async () =>
        {
            var harness = new Harness { Answer = LaunchSettingsUnsavedChoice.Save };
            var controller = harness.LoadEdited();
            harness.Panel.FormControls.ContextSizeBox!.Text = "not a number";

            Assert.False(await controller.ConfirmLeavingEditsAsync());
            Assert.Single(harness.BusyErrors);
            Assert.Empty(harness.SavedProfiles);
            Assert.Equal("not a number", harness.Panel.FormControls.ContextSizeBox.Text);
        });

    [Fact]
    public async Task RecreatedPanelDoesNotInheritTheOldFormsUnsavedState()
        => await RunStaAsync(async () =>
        {
            var harness = new Harness();
            var controller = harness.LoadEdited();
            Assert.True(controller.HasUnsavedEdits());

            harness.Panel.Apply(LaunchSettingsPanelFactory.Create(PanelRequest(harness.Settings)));

            Assert.False(controller.HasUnsavedEditsFor(harness.Model.Id, "profile-1"));
            Assert.True(await controller.ConfirmLeavingEditsAsync());
            Assert.Equal(0, harness.Prompts);
        });

    private static (LaunchSettingsPanelControls Panel, LaunchSettingsPanelState State) CreatePanel(AppSettings settings)
    {
        var panelState = new LaunchSettingsPanelState();
        var panel = LaunchSettingsPanelFactory.Create(PanelRequest(settings));
        panelState.Apply(panel);
        return (panel, panelState);
    }

    private static LaunchSettingsPanelRequest PanelRequest(AppSettings settings)
        => new(
            settings,
            [new RuntimeChoice("cpu", "Official CPU", RuntimeBackend.Cpu, RuntimeMode.Native, "llama-server.exe")],
            ShowAdvancedLaunchSettings: true,
            RuntimeSelectionChanged: () => { },
            AdvancedSettingsChanged: _ => { },
            LaunchSettingsSearchChanged: () => { },
            SaveForModelAsync: () => Task.CompletedTask,
            FitToAvailableVramAsync: () => Task.CompletedTask,
            SaveDefaultsAsync: () => Task.CompletedTask,
            ResetDefaults: () => { },
            SaveAsNewAsync: () => Task.CompletedTask,
            ChooseVisionProjectorAsync: () => Task.CompletedTask,
            ChooseDraftModelAsync: () => Task.CompletedTask,
            ChooseMtpHeadAsync: () => Task.CompletedTask,
            SaveAsNewNameChanged: () => { },
            ChooseAdditionalFile: _ => null,
            ChooseAdditionalDirectory: _ => null);

    // Only the editor session is used by the unsaved-edit paths; the other services stay unset.
    private sealed class Harness
    {
        public Harness()
        {
            Settings = AppSettings.CreateDefault(TestWorkspace);
            (_, Panel) = CreatePanel(Settings);
            EditedContext = Settings.ContextSize + 4096;
            var ui = new MainWindowCoreUiServices(
                null!, null!, null!, null!, null!, null!, null!,
                new LaunchSettingsEditorSession(),
                null!, null!, null!, null!, null!, null!, null!, null!, null!);
            Controller = new LaunchSettingsPageController(TestWorkspace, Panel, ui, null!, null!, new LaunchSettingsPageControllerActions(
                Settings: () => Settings,
                SetSettings: _ => { },
                SelectedModel: () => Model,
                SelectedProfileId: () => "profile-1",
                SelectedRuntimeId: () => "cpu",
                SelectedRuntime: () => null,
                ModelServices: () => throw new InvalidOperationException("Model services are not used."),
                RunBusyAsync: RunBusyAsync,
                RunBackground: (_, failure) => BackgroundWork.Add(failure),
                RefreshRuntimeSelectorAsync: _ => Task.CompletedTask,
                ApplyModelCapabilitiesAsync: (_, _) => Task.CompletedTask,
                RefreshModelsAsync: () => Task.CompletedTask,
                SelectProfileAfterRefresh: _ => { },
                SelectModelAfterRefresh: (_, _) => { },
                RefreshOverviewModelsAsync: () => Task.CompletedTask,
                PersistSettingsAsync: () => Task.CompletedTask,
                UpdateControlVisibility: () => { },
                UpdateRuntimeCommandPreview: () => { },
                UpdateContextSizeSuggestion: () => { },
                NormalizeContextSize: () => { },
                CancelRuntimeOptionDiscovery: () => { },
                PickOpenFile: _ => null,
                OpenBenchmarkPlan: _ => { },
                ShowModels: () => { },
                OpenLog: _ => { },
                SetStatus: Statuses.Add,
                ConfirmUnsavedEdits: () =>
                {
                    Prompts++;
                    return Answer;
                },
                SaveProfileAsync: SaveAsync));
        }

        public AppSettings Settings { get; }
        public LaunchSettingsPanelState Panel { get; }
        public LaunchSettingsPageController Controller { get; }
        public ModelRecord Model { get; } = new("model-1", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        public int EditedContext { get; }
        public LaunchSettingsUnsavedChoice Answer { get; set; } = LaunchSettingsUnsavedChoice.Cancel;
        public int Prompts { get; private set; }
        public Exception? SaveFailure { get; init; }
        public Task SaveGate { get; set; } = Task.CompletedTask;
        public List<(string ModelId, string ProfileId, AppSettings Settings)> SavedProfiles { get; } = [];
        public List<string> BackgroundWork { get; } = [];
        public List<string> BusyErrors { get; } = [];
        public List<string> Statuses { get; } = [];

        public LaunchSettingsPageController LoadEdited(bool editContext = true)
        {
            Controller.LoadEditor(Model, new ModelLaunchSettingsViewState(
                Model.Id, ModelLaunchSettings.FromAppSettings(Settings, "cpu"), true, "cpu", Settings, "profile-1", "Fast"));
            Controller.ApplyToControls(Settings);
            if (editContext) EditContext();
            return Controller;
        }

        public void EditContext()
        {
            Panel.FormControls.ContextSizeBox!.Text = EditedContext.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Controller.UpdateSaveButtonState();
        }

        // Mirrors the app's busy runner: a failure is reported and not rethrown.
        private async Task RunBusyAsync(string _, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                BusyErrors.Add(ex.Message);
            }
        }

        private async Task<ModelLaunchSettingsSaveResult> SaveAsync(ModelRecord model, AppSettings settings, string runtimeId, string profileId)
        {
            await SaveGate;
            if (SaveFailure is not null) throw SaveFailure;
            SavedProfiles.Add((model.Id, profileId, settings));
            return new ModelLaunchSettingsSaveResult(ModelLaunchSettings.FromAppSettings(settings, runtimeId), "saved", profileId);
        }
    }
}
