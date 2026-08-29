using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class LaunchSaveApplicationTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task LaunchProfileSaveApplicationHandlesSelectionAndRefreshesEditorState()
    {
        var service = new ModelLaunchSettingsSaveApplicationService();
        var status = "";
        var markSaved = "";
        var rendered = 0;
        var buttonUpdates = 0;
        var settings = AppSettings.CreateDefault("workspace") with { Port = 8181 };
        var savedSettings = ModelLaunchSettings.FromAppSettings(settings, "runtime-a");
        var resultActions = new ModelLaunchProfileSaveActions(
            (modelId, profileId, saved) => markSaved = $"{modelId}|{profileId}|{saved.Port}",
            () => buttonUpdates++,
            value => status = value);
        var actions = new ModelLaunchProfileSaveSelectedActions(
            async (message, operation) =>
            {
                Assert.Equal("Saving model launch profile...", message);
                await operation();
            },
            (_, _) => false,
            () => "profile-a",
            () =>
            {
                rendered++;
                return Task.CompletedTask;
            },
            () => settings,
            (model, launchSettings, profileId) =>
            {
                Assert.Equal("model-a", model.Id);
                Assert.Equal(8181, launchSettings.Port);
                Assert.Equal("profile-a", profileId);
                return Task.FromResult(new ModelLaunchSettingsSaveResult(savedSettings, "Profile saved."));
            },
            resultActions);

        var missing = await service.SaveSelectedProfileAsync(null, actions);
        var model = new ModelRecord("model-a", "Model A", "a.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var saved = await service.SaveSelectedProfileAsync(model, actions);

        Assert.Equal(ModelLaunchProfileSaveApplicationOutcome.NoModelSelected, missing);
        Assert.Equal(ModelLaunchProfileSaveApplicationOutcome.Saved, saved);
        Assert.Equal(1, rendered);
        Assert.Equal(1, buttonUpdates);
        Assert.Equal("model-a|profile-a|8181", markSaved);
        Assert.Equal("Profile saved.", status);
    }

    [Fact]
    public async Task LaunchDefaultsSaveApplicationPersistsAndReportsResult()
    {
        var service = new ModelLaunchSettingsSaveApplicationService();
        var input = AppSettings.CreateDefault("workspace") with { ContextSize = 32768 };
        var stored = AppSettings.CreateDefault("workspace");
        var persisted = 0;
        var buttonUpdates = 0;
        var status = "";
        var resultActions = new LaunchDefaultsSaveActions(
            value => stored = value,
            () =>
            {
                persisted++;
                return Task.CompletedTask;
            },
            () => buttonUpdates++,
            value => status = value);
        var actions = new LaunchDefaultsSaveFromControlsActions(
            async (message, operation) =>
            {
                Assert.Equal("Saving launch defaults...", message);
                await operation();
            },
            () => input,
            value => new LaunchDefaultsSaveResult(value with { ContextSize = 65536 }, "Defaults saved."),
            resultActions);

        var outcome = await service.SaveDefaultsFromControlsAsync(actions);

        Assert.Equal(LaunchDefaultsSaveApplicationOutcome.Saved, outcome);
        Assert.Equal(65536, stored.ContextSize);
        Assert.Equal(1, persisted);
        Assert.Equal(1, buttonUpdates);
        Assert.Equal("Defaults saved.", status);
    }

    [Fact]
    public async Task LaunchVariantSaveApplicationHandlesMissingFailedAndSuccessfulResults()
    {
        var service = new ModelLaunchVariantSaveApplicationService();
        var settings = AppSettings.CreateDefault("workspace") with { Port = 8282 };
        var model = new ModelRecord("model-a", "Model A", "a.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile(
            "profile-new",
            model.Id,
            "Fast",
            ModelLaunchSettings.FromAppSettings(settings, "runtime-a"),
            DateTimeOffset.UtcNow);
        var status = "";
        var rendered = 0;
        var refreshedModels = 0;
        var refreshedOverview = 0;
        var selectedProfile = "";
        var resultActions = new ModelLaunchVariantSaveActions(
            () =>
            {
                refreshedModels++;
                return Task.CompletedTask;
            },
            value => selectedProfile = value,
            () =>
            {
                rendered++;
                return Task.CompletedTask;
            },
            () =>
            {
                refreshedOverview++;
                return Task.CompletedTask;
            },
            value => status = value);
        var nextResult = new ModelLaunchVariantWorkflowResult(false, "Name already exists.");
        var actions = new ModelLaunchVariantSaveSelectedActions(
            async (message, operation) =>
            {
                Assert.Equal("Saving named launch profile...", message);
                await operation();
            },
            _ => false,
            () =>
            {
                rendered++;
                return Task.CompletedTask;
            },
            () => settings,
            () => "runtime-a",
            request =>
            {
                Assert.Equal("Fast", request.RequestedName);
                Assert.Equal("runtime-a", request.RuntimeId);
                return Task.FromResult(nextResult);
            },
            resultActions);

        Assert.Equal(ModelLaunchVariantSaveApplicationOutcome.NoModelSelected, await service.SaveSelectedAsNewAsync(null, "Fast", settings, actions));
        Assert.Equal(ModelLaunchVariantSaveApplicationOutcome.Failed, await service.SaveSelectedAsNewAsync(model, "Fast", settings, actions));

        nextResult = new ModelLaunchVariantWorkflowResult(true, "Profile saved.", profile, profile.Settings);
        Assert.Equal(ModelLaunchVariantSaveApplicationOutcome.Saved, await service.SaveSelectedAsNewAsync(model, "Fast", settings, actions));

        Assert.Equal(3, rendered);
        Assert.Equal(1, refreshedModels);
        Assert.Equal(1, refreshedOverview);
        Assert.Equal("profile-new", selectedProfile);
        Assert.Equal("Profile saved.", status);
    }
}
