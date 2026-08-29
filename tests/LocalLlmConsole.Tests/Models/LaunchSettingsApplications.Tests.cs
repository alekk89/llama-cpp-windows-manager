using LocalLlmConsole.Models;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;

namespace LocalLlmConsole.Tests;


[Collection(LocalizationStateTestCollection.Name)]
public sealed class LaunchSettingsApplicationsTests : ManagerRegressionTestBase
{


    [Fact]
    public void ModelLaunchDefaultsUseHighContextGpuAndQ8Cache()
    {
        var root = CreateTempRoot();

        var settings = AppSettings.CreateDefault(root);
        var modelSettings = ModelLaunchSettings.FromAppSettings(settings);
        var applied = modelSettings.ApplyTo(settings with { Port = 9000 });

        Assert.Equal(131_072, settings.ContextSize);
        Assert.Equal(999, settings.GpuLayers);
        Assert.Equal(8081, modelSettings.Port);
        Assert.Equal(8081, applied.Port);
        Assert.Equal(4096, settings.BatchSize);
        Assert.Equal("q8_0", settings.CacheTypeK);
        Assert.Equal("q8_0", settings.CacheTypeV);
        Assert.Equal(0.65, settings.Temperature);
        Assert.Equal(settings.ContextSize, modelSettings.ContextSize);
        Assert.Equal(settings.GpuLayers, modelSettings.GpuLayers);
        Assert.Equal(settings.BatchSize, modelSettings.BatchSize);
        Assert.Equal(settings.CacheTypeK, modelSettings.CacheTypeK);
        Assert.Equal(settings.CacheTypeV, modelSettings.CacheTypeV);
        Assert.Equal(settings.Temperature, modelSettings.Temperature);
        Assert.Equal("none", settings.SpeculativeType);
        Assert.Equal("q8_0", settings.SpecDraftCacheTypeK);
        Assert.Equal("q8_0", settings.SpecDraftCacheTypeV);
        Assert.Equal(-1, settings.Seed);
        Assert.Equal(-1, settings.MaxTokens);
        Assert.Equal(0, settings.VisionImageMinTokens);
        Assert.Equal(0, settings.VisionImageMaxTokens);
        Assert.Equal("", settings.VisionProjectorPath);
        Assert.Equal("", settings.MtpHeadPath);
        Assert.Equal(settings.VisionProjectorPath, modelSettings.VisionProjectorPath);
        Assert.Equal(settings.MtpHeadPath, modelSettings.MtpHeadPath);
        Assert.Equal(settings.VisionImageMinTokens, modelSettings.VisionImageMinTokens);
        Assert.Equal(settings.VisionImageMaxTokens, modelSettings.VisionImageMaxTokens);
        Assert.Equal("auto", settings.RopeScaling);
    }


    [Fact]
    public void ModelSettingsDefaultToSimpleWithRequestedAdvancedGroups()
    {
        var source = ReadMainWindowSources()
            + File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "LaunchSettingsPageController.cs"));
        var launchProfileService = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Models", "ModelLaunchProfileService.cs"));
        var advancedSections = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "AdvancedSectionStateController.cs"));
        var selectedCapabilities = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Models", "SelectedModelCapabilityController.cs"));
        var controlStateService = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Launch", "LaunchSettingsControlStateService.cs"));
        var launchFormBinder = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "LaunchSettingsFormBinder.cs"));
        var launchPanelFactory = ReadLaunchSettingsPanelFactorySources();
        var launchUiSchema = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "LaunchSettingUiSchema.cs"));
        var launchPanelState = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "LaunchSettingsPanelState.cs"));
        var advancedState = new AdvancedSectionStateController();

        Assert.False(advancedState.ShowLaunchSettings);
        advancedState.SetLaunchSettings(true);
        Assert.True(advancedState.ShowLaunchSettings);
        Assert.Contains("public bool ShowLaunchSettings { get; private set; }", advancedSections, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.AdvancedSections.ShowLaunchSettings", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.AdvancedSections.SetLaunchSettings(showAdvanced)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_showAdvancedLaunchSettings", source, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsPanelFactory.Create", source, StringComparison.Ordinal);
        Assert.Contains("private readonly LaunchSettingsPanelState _launchSettingsPanel;", source, StringComparison.Ordinal);
        Assert.Contains("_launchSettingsPanel = uiState.LaunchSettingsPanel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly LaunchSettingsPanelState _launchSettingsPanel = new();", source, StringComparison.Ordinal);
        Assert.Contains("_launchSettingsPanel.Apply(panel);", source, StringComparison.Ordinal);
        Assert.Contains("public LaunchSettingsFormControls FormControls { get; private set; } = new();", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("public string SaveAsNewModelName", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("SetSaveForModelState", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("SaveModelLaunchSettingsButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("SetSaveForModelState(content, hasNamedProfile && state.CanSaveForModel, hasNamedProfile)", source, StringComparison.Ordinal);
        Assert.Contains("Models.Profile.SelectOrCreate", source, StringComparison.Ordinal);
        Assert.Contains("public void ApplyControlState(LaunchSettingsControlStatePlan plan)", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsSearch.From(LaunchSettingsSearchBox?.Text)", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("ApplyLaunchSectionVisibility(plan, search)", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("AdvancedLaunchSettingLabels.Contains(label)", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("Terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("_launchSettingsPanel.ApplyControlState(plan)", source, StringComparison.Ordinal);
        Assert.Contains("LaunchTextBox(request.Settings.Port)", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("Tooltip.LaunchPortBox", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsFormBinder.Read(_settings, _launchSettingsPanel.FormControls)", source, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsFormBinder.Apply(_panel.FormControls", source, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsFormBinder.AttachChangeHandlers(_panel.FormControls", source, StringComparison.Ordinal);
        Assert.Contains("public sealed record LaunchSettingsPanelRequest", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("public sealed class LaunchSettingsPanelControls", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsSearchBox = launchSettingsSearchBox", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("AdvancedLaunchSettingsButton = advancedLaunchSettingsButton", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingElements = launchSettingElements", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingSections = launchSettingSections", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("AdvancedLaunchSettingLabels = advancedLaunchSettingLabels", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("public sealed class LaunchSettingsFormControls", launchFormBinder, StringComparison.Ordinal);
        Assert.Contains("public static void ValidateCrossFieldRules", launchFormBinder, StringComparison.Ordinal);
        Assert.DoesNotContain("Port = ReadInt(_launchPortBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchPortBox = _launchPortBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private WpfTextBox? _launchPortBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_launchSettingsFormControls", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeCombo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveModelLaunchSettingsButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveAsNewModelNameBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveAsNewModelButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_launchSettingElements", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_advancedLaunchSections", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_advancedLaunchSettingsToggle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelCapabilityText", source, StringComparison.Ordinal);
        Assert.Contains("var launchSettings = ModelServices.ModelLaunchSettingsWorkflow;", source, StringComparison.Ordinal);
        Assert.Contains("_models.LaunchSettingsRenderApplication.RenderSelectedAsync(", source, StringComparison.Ordinal);
        Assert.Contains("_actions.SelectedProfileId", source, StringComparison.Ordinal);
        Assert.Contains("private async Task<ModelLaunchSettings?> EnsureModelLaunchProfileAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelLaunchPortAvailableAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetModelLaunchSettingsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveModelLaunchSettingsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelPortAllocator.NextAvailable", source, StringComparison.Ordinal);
        Assert.Contains("ReadAsync(ModelRecord model, string profileId", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("DraftAsync(ModelRecord model, AppSettings defaults, string profileId", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("ListNamedAsync(model)", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("SaveNamedAsync(saved)", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("EnsureAsync(ModelRecord model, AppSettings defaults)", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("IsPortAvailableAsync(string modelId, int port, AppSettings settings, string currentProfileId", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("ModelPortAllocator.NextAvailable(settings.Port, used)", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("foreach (var sectionGroup in LaunchSettingUiSchema.Definitions.GroupBy", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("\"Basic\", \"ContextSize\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"PerformanceMemory\", \"FlashAttention\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"SpeculativeMtp\", \"SpecType\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"ChatCapabilities\", \"Vision\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"GenerationDefaults\", \"Temperature\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"ContextExtension\", \"RopeScaling\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"Server\", \"ParallelSlots\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsSearchHost(request.LaunchSettingsSearchChanged, out searchBox)", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("FormControls.RuntimeOptions?.ApplyVisibility(plan.ShowAdvancedSections, LaunchSettingsSearchBox?.Text)", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("AdvancedButtonText(showAdvanced)", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingEditorKind.VisionProjector", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("Picker.Vision.Embedded", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingEditorKind.MtpHead", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"ChatCapabilities\", \"ImageMin\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"ChatCapabilities\", \"ImageMax\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"Basic\", \"GpuMode\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"Basic\", \"GpuDevices\", advanced: true", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"Basic\", \"GpuSplit\", advanced: true", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("public bool VisionLaunchSettingsAvailable => Capabilities.LikelyVision;", selectedCapabilities, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.SelectedCapabilities.Apply(model, capabilities)", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.LaunchSettingsControlStates.Build(new LaunchSettingsControlStateRequest(", source, StringComparison.Ordinal);
        Assert.Contains("\"Image min\"] = visionAvailable", controlStateService, StringComparison.Ordinal);
        Assert.Contains("\"Image max\"] = visionAvailable", controlStateService, StringComparison.Ordinal);
        Assert.Contains("\"Vision head\"] = visionAvailable", controlStateService, StringComparison.Ordinal);
        Assert.Contains("\"Draft model\"", controlStateService, StringComparison.Ordinal);
        Assert.Contains("\"MTP head\"", controlStateService, StringComparison.Ordinal);
        Assert.DoesNotContain("var visionLaunchSettingsAvailable = _coreServices.Ui.SelectedCapabilities.VisionLaunchSettingsAvailable;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetLaunchSettingVisible(\"Image min\", visionLaunchSettingsAvailable);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private void SetLaunchSettingVisible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectedModelCapabilities", source, StringComparison.Ordinal);

        Assert.Contains("builder.AddSection(title, section, grid, isAdvancedSection);", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("if (isAdvancedSection)", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("builder.AddAdvancedSection(section);", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("if (definition.Advanced)", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("\"PerformanceMemory\", \"KvOffload\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"PerformanceMemory\", \"PromptCache\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"PerformanceMemory\", \"MemoryLock\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"SpeculativeMtp\", \"DraftGpu\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"GenerationDefaults\", \"MaxTokens\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"GenerationDefaults\", \"Frequency\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("nameof(AppSettings.CustomParameters)", launchUiSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("Performance & Memory - Advanced", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Speculative / MTP - Advanced", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Generation Defaults - Advanced", source, StringComparison.Ordinal);
    }


    [Fact]
    public void LaunchSettingsControlStateServiceOwnsGpuVisionAndSpeculativeRules()
    {
        var service = new LaunchSettingsControlStateService();

        var cudaVisionDraft = service.Build(new LaunchSettingsControlStateRequest(
            ShowAdvancedSections: true,
            RuntimeBackend: RuntimeBackend.Cuda,
            VisionLaunchSettingsAvailable: true,
            SpeculativeType: "draft-model"));
        var cpuNoVision = service.Build(new LaunchSettingsControlStateRequest(
            ShowAdvancedSections: false,
            RuntimeBackend: RuntimeBackend.Cpu,
            VisionLaunchSettingsAvailable: false,
            SpeculativeType: "none"));
        var mtpHead = service.Build(new LaunchSettingsControlStateRequest(
            ShowAdvancedSections: true,
            RuntimeBackend: RuntimeBackend.Cuda,
            VisionLaunchSettingsAvailable: false,
            SpeculativeType: "atomic-mtp"));

        Assert.True(cudaVisionDraft.ShowAdvancedSections);
        Assert.True(cudaVisionDraft.GpuLayersAvailable);
        Assert.True(cudaVisionDraft.VisionLaunchSettingsAvailable);
        Assert.True(cudaVisionDraft.DraftSpeculativeSettingsAvailable);
        Assert.False(cudaVisionDraft.MtpHeadSettingsAvailable);
        Assert.True(cudaVisionDraft.VisibleSettings["GPU layers"]);
        Assert.True(cudaVisionDraft.VisibleSettings["GPU mode"]);
        Assert.True(cudaVisionDraft.EnabledSettings["GPU devices"]);
        Assert.True(cudaVisionDraft.VisibleSettings["Vision head"]);
        Assert.True(cudaVisionDraft.VisibleSettings["Image min"]);
        Assert.True(cudaVisionDraft.EnabledSettings["Draft model"]);
        Assert.True(cudaVisionDraft.EnabledSettings["Split prob"]);

        Assert.False(cpuNoVision.ShowAdvancedSections);
        Assert.False(cpuNoVision.GpuLayersAvailable);
        Assert.True(cpuNoVision.VisionLaunchSettingsAvailable);
        Assert.False(cpuNoVision.DraftSpeculativeSettingsAvailable);
        Assert.False(cpuNoVision.MtpHeadSettingsAvailable);
        Assert.False(cpuNoVision.VisibleSettings["GPU layers"]);
        Assert.False(cpuNoVision.VisibleSettings["GPU mode"]);
        Assert.False(cpuNoVision.EnabledSettings["GPU split"]);
        Assert.True(cpuNoVision.VisibleSettings["Vision head"]);
        Assert.True(cpuNoVision.VisibleSettings["Image max"]);
        Assert.False(cpuNoVision.EnabledSettings["Draft GPU"]);
        Assert.True(cpuNoVision.VisibleSettings["Reasoning"]);
        Assert.True(cpuNoVision.VisibleSettings["Jinja chat"]);

        Assert.True(mtpHead.MtpHeadSettingsAvailable);
        Assert.False(mtpHead.DraftSpeculativeSettingsAvailable);
        Assert.True(mtpHead.EnabledSettings["MTP head"]);
        Assert.False(mtpHead.EnabledSettings["Draft model"]);
    }


    [Theory]
    [InlineData("196", 200704)]
    [InlineData("196k", 200704)]
    [InlineData("196K", 200704)]
    [InlineData("196.5k", 201728)]
    [InlineData("196000", 195584)]
    [InlineData("128,000", 128000)]
    [InlineData("1m", 1048576)]
    [InlineData("0", 0)]
    public void ContextSizeParserNormalizesShorthandToLlamaFriendlySteps(string text, int expected)
    {
        var ok = LaunchSettingParser.TryNormalizeContextSize(text, out var value);

        Assert.True(ok);
        Assert.Equal(expected, value);
    }


    [Fact]
    public void LaunchSettingParserValidatesNumericInputs()
    {
        Assert.Equal(200704, LaunchSettingParser.ReadContextSize("196k"));
        Assert.Equal(4, LaunchSettingParser.ReadInt("4", "Threads", 0, 8));
        Assert.Equal(0.75, LaunchSettingParser.ReadDouble("0.75", "Top P", 0, 1), precision: 3);
        Assert.Contains("whole number", Assert.Throws<InvalidOperationException>(() => LaunchSettingParser.ReadInt("1.5", "Threads", 0)).Message, StringComparison.Ordinal);
        Assert.Contains("at least 0", Assert.Throws<InvalidOperationException>(() => LaunchSettingParser.ReadDouble("-0.1", "Top P", 0, 1)).Message, StringComparison.Ordinal);
        Assert.Contains("no more than 1", Assert.Throws<InvalidOperationException>(() => LaunchSettingParser.ReadDouble("1.2", "Top P", 0, 1)).Message, StringComparison.Ordinal);
    }


    [Fact]
    public void LaunchSettingsFormBinderOwnsCrossFieldValidation()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot());

        LaunchSettingsFormBinder.ValidateCrossFieldRules(settings);
        Assert.Contains("Draft min tokens", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { SpecDraftMinTokens = 32, SpecDraftMaxTokens = 16 })).Message, StringComparison.Ordinal);
        Assert.Contains("Image min tokens", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { VisionImageMinTokens = 640, VisionImageMaxTokens = 320 })).Message, StringComparison.Ordinal);
        Assert.Contains("Draft split probability", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { SpecDraftPSplit = -0.5 })).Message, StringComparison.Ordinal);
        Assert.Contains("Draft min probability", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { SpecDraftPMin = -0.5 })).Message, StringComparison.Ordinal);
        Assert.Contains("Prompt cache MB", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { PromptCacheMode = "on", PromptCacheRamMb = 0 })).Message, StringComparison.Ordinal);
        Assert.Contains("Checkpoint count", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { ContextCheckpointsMode = "on", ContextCheckpointCount = 0 })).Message, StringComparison.Ordinal);
        Assert.Contains("Checkpoint spacing", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { ContextCheckpointsMode = "on", ContextCheckpointEveryNTokens = -1 })).Message, StringComparison.Ordinal);
        Assert.Contains("Single GPU mode", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { GpuMode = "single", GpuDevices = "CUDA0,CUDA1" })).Message, StringComparison.Ordinal);
        Assert.Contains("same number", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { GpuMode = "layer", GpuDevices = "CUDA0,CUDA1", GpuSplit = "1" })).Message, StringComparison.Ordinal);
        Assert.Contains("unterminated quote", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { CustomParameters = "\"oops" })).Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void LaunchSettingsSaveStateServiceOwnsButtonRules()
    {
        var source = ReadMainWindowSources()
            + File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "LaunchSettingsPageController.cs"));
        var settings = AppSettings.CreateDefault(CreateTempRoot());
        var model = new ModelRecord("model-1", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var saved = ModelLaunchSettings.FromAppSettings(settings);
        var changed = saved with { ContextSize = saved.ContextSize + 1024 };

        var noSelection = LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            null,
            HasSavedProfile: false,
            SavedProfile: null,
            CurrentProfileReadable: false,
            CurrentProfile: null,
            RequestedVariantName: "Qwen 32K"));
        var newProfile = LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            model,
            HasSavedProfile: false,
            SavedProfile: null,
            CurrentProfileReadable: false,
            CurrentProfile: null,
            RequestedVariantName: model.Name));
        var unreadableCurrentProfile = LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            model,
            HasSavedProfile: true,
            SavedProfile: saved,
            CurrentProfileReadable: false,
            CurrentProfile: null,
            RequestedVariantName: "Qwen 32K"));
        var cleanProfile = LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            model,
            HasSavedProfile: true,
            SavedProfile: saved,
            CurrentProfileReadable: true,
            CurrentProfile: saved,
            RequestedVariantName: "Qwen 32K"));
        var dirtyProfile = LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            model,
            HasSavedProfile: true,
            SavedProfile: saved,
            CurrentProfileReadable: true,
            CurrentProfile: changed,
            RequestedVariantName: "  qwen  "));

        Assert.Equal(LaunchSettingsSaveStateService.SaveForModelText, noSelection.SaveForModelContent);
        Assert.False(noSelection.CanSaveForModel);
        Assert.False(noSelection.CanSaveAsNewVariant);
        Assert.Equal(LaunchSettingsSaveStateService.SaveForModelText, newProfile.SaveForModelContent);
        Assert.True(newProfile.CanSaveForModel);
        Assert.True(newProfile.CanSaveAsNewVariant);
        Assert.True(unreadableCurrentProfile.CanSaveForModel);
        Assert.True(unreadableCurrentProfile.CanSaveAsNewVariant);
        Assert.Equal(LaunchSettingsSaveStateService.SavedText, cleanProfile.SaveForModelContent);
        Assert.False(cleanProfile.CanSaveForModel);
        Assert.True(cleanProfile.CanSaveAsNewVariant);
        Assert.Equal(LaunchSettingsSaveStateService.SaveForModelText, dirtyProfile.SaveForModelContent);
        Assert.True(dirtyProfile.CanSaveForModel);
        Assert.True(dirtyProfile.CanSaveAsNewVariant);
        Assert.Contains("LaunchSettingsSaveStateService.Evaluate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveModelLaunchSettingsButton.Content = \"Saved\"", source, StringComparison.Ordinal);
    }


    [Fact]
    public void LaunchSettingsEditorSessionOwnsSelectedProfileSnapshot()
    {
        var source = ReadMainWindowSources()
            + File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "LaunchSettingsPageController.cs"));
        var settings = AppSettings.CreateDefault(CreateTempRoot());
        var model = new ModelRecord("model-1", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var saved = ModelLaunchSettings.FromAppSettings(settings) with { ContextSize = 32768 };
        var session = new LaunchSettingsEditorSession();
        var viewState = new ModelLaunchSettingsViewState(
            model.Id,
            HasSavedProfile: true,
            SavedProfile: saved,
            RuntimeId: "runtime-1",
            LaunchSettings: saved.ApplyTo(settings));

        Assert.False(session.IsLoadedFor(model.Id));
        Assert.True(session.TryChangeSaveAsNewSource(model));
        Assert.False(session.TryChangeSaveAsNewSource(model));

        session.Load(viewState);

        Assert.True(session.IsLoadedFor("MODEL-1"));
        Assert.True(session.HasSavedProfile);
        Assert.Same(saved, session.SavedProfile);
        Assert.False(session.IsProgrammaticUpdate);
        var observedProgrammaticUpdate = false;
        session.RunProgrammaticUpdate(() => observedProgrammaticUpdate = session.IsProgrammaticUpdate);
        Assert.True(observedProgrammaticUpdate);
        Assert.False(session.IsProgrammaticUpdate);

        var nextSaved = saved with { ContextSize = 65536 };
        session.MarkSaved(model.Id, "", nextSaved);

        Assert.Same(nextSaved, session.SavedProfile);

        session.Clear();

        Assert.False(session.IsLoadedFor(model.Id));
        Assert.False(session.HasSavedProfile);
        Assert.Null(session.SavedProfile);
        Assert.Contains("_ui.LaunchSettingsEditor.Load,", source, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsRenderActions(", source, StringComparison.Ordinal);
        Assert.Contains("_models.ModelLaunchSettingsSaveApplication.SaveSelectedProfileAsync(", source, StringComparison.Ordinal);
        Assert.Contains("_ui.LaunchSettingsEditor.MarkSaved,", source, StringComparison.Ordinal);
        Assert.Contains("_ui.LaunchSettingsEditor.IsLoadedFor,", source, StringComparison.Ordinal);
        Assert.Contains("_ui.LaunchSettingsEditor.TryChangeSaveAsNewSource(model, _actions.SelectedProfileId())", source, StringComparison.Ordinal);
        Assert.Contains("_ui.LaunchSettingsEditor.RunProgrammaticUpdate", source, StringComparison.Ordinal);
        Assert.Contains("_ui.LaunchSettingsEditor.IsProgrammaticUpdate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_launchSettingsModelId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_savedLaunchSettingsSnapshot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_hasSavedLaunchSettingsSnapshot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveAsNewSourceModelId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_updatingLaunchSettingsControls", source, StringComparison.Ordinal);
    }


    [Fact]
    public void LaunchSettingMetadataOwnsTooltipsAndContextSuggestions()
    {
        Loc.LoadLanguage("en");
        Assert.Equal(Loc.T("Tooltip.Field.ContextSize"), LaunchSettingMetadataService.Tooltip("Context size"));
        Assert.Equal(Loc.T("Tooltip.Field.Default"), LaunchSettingMetadataService.Tooltip("Unknown setting"));
        Assert.Contains("Suggestion:", LaunchSettingMetadataService.ContextSizeTooltip("196k"), StringComparison.Ordinal);
        Assert.DoesNotContain("Suggestion:", LaunchSettingMetadataService.ContextSizeTooltip("200704"), StringComparison.Ordinal);
        Assert.DoesNotContain("Suggestion:", LaunchSettingMetadataService.ContextSizeTooltip("200_704"), StringComparison.Ordinal);
        Assert.Contains("q4_0", LaunchSettingMetadataService.CacheTypeOptions);
        Assert.Contains("atomic-mtp", LaunchSettingMetadataService.SpeculativeTypeOptions);
        Assert.DoesNotContain("mtp", LaunchSettingMetadataService.SpeculativeTypeOptions);
        Assert.Contains("draft-mtp", LaunchSettingMetadataService.SpeculativeTypeOptions);
        Assert.Contains("draft-dspark", LaunchSettingMetadataService.SpeculativeTypeOptions);
        Assert.True(LaunchSettingMetadataService.IsAtomicMtpSpeculativeType("mtp"));
        Assert.Equal("mtp", LaunchSettingMetadataService.LlamaSpeculativeTypeArgument("atomic-mtp"));
        Assert.Equal(Loc.T("Tooltip.Current.MtpHead"), LaunchSettingMetadataService.Tooltip("MTP head"));
        Assert.Equal(Loc.T("Tooltip.Field.CustomParams"), LaunchSettingMetadataService.Tooltip("Custom params"));
        Assert.Equal(Loc.T("Tooltip.Field.ImageMin"), LaunchSettingMetadataService.Tooltip("Image min"));
        Assert.Equal(Loc.T("Tooltip.Field.ImageMax"), LaunchSettingMetadataService.Tooltip("Image max"));
        Assert.Equal("auto", LaunchSettingMetadataService.AutoOnOffOptions[0]);
        Assert.Equal("on", LaunchSettingMetadataService.OnOffOptions[0]);
    }


    [Fact]
    public void AppOwnedDeletionRejectsRootAndOutsidePaths()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord(
            "model",
            "Model",
            Path.Combine(root, "models", "model.gguf"),
            OwnershipKind.AppOwned,
            "{}",
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => FileOwnershipService.EnsureDeletionAllowed(model, root, root));
        Assert.Throws<InvalidOperationException>(() => FileOwnershipService.EnsureDeletionAllowed(model, Path.GetTempPath(), root));

        var appOwnedChild = Path.Combine(root, "models", "model");
        FileOwnershipService.EnsureDeletionAllowed(model, appOwnedChild, root);
    }


    [Fact]
    public void AppOwnedDeletionRejectsExistingFolderThatDoesNotContainModel()
    {
        var root = CreateTempRoot();
        var target = Path.Combine(root, "models", "different-model");
        Directory.CreateDirectory(target);
        var model = new ModelRecord(
            "model",
            "Model",
            Path.Combine(root, "models", "model.gguf"),
            OwnershipKind.AppOwned,
            "{}",
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => FileOwnershipService.EnsureDeletionAllowed(model, target, root));
    }



    [Fact]
    public void ModelFolderApplicationServiceOwnsFolderResolutionAndBlockedStatus()
    {
        var root = CreateTempRoot();
        var service = new ModelFolderApplicationService();
        var calls = new List<string>();
        var model = new ModelRecord(
            "model",
            "Model",
            Path.Combine(root, "models", "model.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var invalid = model with { ModelPath = "model.gguf" };

        ModelFolderApplicationActions Actions()
            => new(
                folder => calls.Add($"open:{folder}"),
                status => calls.Add($"status:{status}"));

        var ignored = service.Open(null, Actions());
        var blocked = service.Open(invalid, Actions());
        var opened = service.Open(model, Actions());

        Assert.Equal(ModelFolderApplicationOutcome.Ignored, ignored);
        Assert.Equal(ModelFolderApplicationOutcome.Blocked, blocked);
        Assert.Equal(ModelFolderApplicationOutcome.Opened, opened);
        Assert.Equal([
            "status:Model folder is unavailable.",
            $"open:{Path.Combine(root, "models")}"
        ], calls);
    }
}
