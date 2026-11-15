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
        => await RunStaAsync(() =>
        {
            var settings = AppSettings.CreateDefault(TestWorkspace);
            var (_, panelState) = CreatePanel(settings);
            var prompts = 0;
            var answer = LaunchSettingsUnsavedChoice.Cancel;
            var backgroundWork = new List<string>();
            var model = new ModelRecord("model-1", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
            var controller = CreateController(panelState, settings, model, () =>
            {
                prompts++;
                return answer;
            }, backgroundWork);
            controller.LoadEditor(model, new ModelLaunchSettingsViewState(
                model.Id, ModelLaunchSettings.FromAppSettings(settings, "cpu"), true, "cpu", settings, "profile-1", "Fast"));
            controller.ApplyToControls(settings);

            Assert.False(controller.HasUnsavedEdits());
            Assert.Equal(Visibility.Collapsed, panelState.UnsavedChangesText!.Visibility);
            Assert.True(controller.ConfirmLeavingEdits("model-2", "profile-2"));
            Assert.Equal(0, prompts);

            panelState.FormControls.ContextSizeBox!.Text = (settings.ContextSize + 4096).ToString(System.Globalization.CultureInfo.InvariantCulture);
            controller.UpdateSaveButtonState();

            Assert.True(controller.HasUnsavedEdits());
            Assert.True(controller.HasUnsavedEditsFor(model.Id, "profile-1"));
            Assert.False(controller.HasUnsavedEditsFor(model.Id, "profile-2"));
            Assert.Equal(Visibility.Visible, panelState.UnsavedChangesText.Visibility);
            Assert.Equal(Loc.T("Launch.UnsavedChanges"), panelState.UnsavedChangesText.Text);

            Assert.True(controller.ConfirmLeavingEdits(model.Id, "profile-1"));
            using (controller.SuppressLeavePrompt())
                Assert.True(controller.ConfirmLeavingEdits("model-2", "profile-2"));
            Assert.Equal(0, prompts);

            Assert.False(controller.ConfirmLeavingEdits("model-2", "profile-2"));
            Assert.False(controller.ConfirmLeavingEdits());
            Assert.Equal(2, prompts);

            answer = LaunchSettingsUnsavedChoice.Discard;
            Assert.True(controller.ConfirmLeavingEdits("model-2", "profile-2"));
            Assert.Empty(backgroundWork);

            answer = LaunchSettingsUnsavedChoice.Save;
            Assert.True(controller.ConfirmLeavingEdits());
            Assert.Equal(["Launch profile save failed"], backgroundWork);

            controller.ApplyToControls(settings);
            Assert.False(controller.HasUnsavedEdits());
            Assert.Equal(Visibility.Collapsed, panelState.UnsavedChangesText.Visibility);
        });

    [Fact]
    public async Task RecreatedPanelDoesNotInheritTheOldFormsUnsavedState()
        => await RunStaAsync(() =>
        {
            var settings = AppSettings.CreateDefault(TestWorkspace);
            var (_, panelState) = CreatePanel(settings);
            var prompts = 0;
            var model = new ModelRecord("model-1", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
            var controller = CreateController(panelState, settings, model, () =>
            {
                prompts++;
                return LaunchSettingsUnsavedChoice.Cancel;
            }, []);
            controller.LoadEditor(model, new ModelLaunchSettingsViewState(
                model.Id, ModelLaunchSettings.FromAppSettings(settings, "cpu"), true, "cpu", settings, "profile-1", "Fast"));
            controller.ApplyToControls(settings with { ContextSize = settings.ContextSize + 4096 });
            Assert.True(controller.HasUnsavedEdits());

            panelState.Apply(LaunchSettingsPanelFactory.Create(PanelRequest(settings)));

            Assert.False(controller.HasUnsavedEditsFor(model.Id, "profile-1"));
            Assert.True(controller.ConfirmLeavingEdits());
            Assert.Equal(0, prompts);
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
    private static LaunchSettingsPageController CreateController(
        LaunchSettingsPanelState panelState,
        AppSettings settings,
        ModelRecord selectedModel,
        Func<LaunchSettingsUnsavedChoice> confirm,
        List<string> backgroundWork)
    {
        var ui = new MainWindowCoreUiServices(
            null!, null!, null!, null!, null!, null!, null!,
            new LaunchSettingsEditorSession(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!);
        return new LaunchSettingsPageController(
            TestWorkspace,
            panelState,
            ui,
            null!,
            null!,
            new LaunchSettingsPageControllerActions(
                Settings: () => settings,
                SetSettings: _ => { },
                SelectedModel: () => selectedModel,
                SelectedProfileId: () => "profile-1",
                SelectedRuntimeId: () => "cpu",
                SelectedRuntime: () => null,
                ModelServices: () => throw new InvalidOperationException("Model services are not used."),
                RunBusyAsync: (_, action) => action(),
                RunBackground: (_, failure) => backgroundWork.Add(failure),
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
                SetStatus: _ => { },
                ConfirmUnsavedEdits: confirm));
    }
}
