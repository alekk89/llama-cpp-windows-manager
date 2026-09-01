using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class StartupLaunchProfilesTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task StartupProfileSelectionsAreOrderedAndCascadeWhenAProfileIsDeleted()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var defaults = AppSettings.CreateDefault(root);
        var model = new ModelRecord("model-1", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", now);
        var first = new NamedModelLaunchProfile(
            "profile-1", model.Id, "Interactive", ModelLaunchSettings.FromAppSettings(defaults with { Port = 8091 }), now, true);
        var second = new NamedModelLaunchProfile(
            "profile-2", model.Id, "Long context", ModelLaunchSettings.FromAppSettings(defaults with { Port = 8092 }), now);
        await store.UpsertModelAsync(model);
        await store.SaveNamedModelLaunchProfileAsync(first);
        await store.SaveNamedModelLaunchProfileAsync(second);

        await store.SetStartupLaunchProfileAsync(second.Id, true);
        await store.SetStartupLaunchProfileAsync(first.Id, true);
        await store.SetStartupLaunchProfileAsync(second.Id, true);
        Assert.Equal([second.Id, first.Id], await store.ListStartupLaunchProfileIdsAsync());

        await store.SetStartupLaunchProfileAsync(second.Id, false);
        Assert.Equal([first.Id], await store.ListStartupLaunchProfileIdsAsync());

        var application = new StartupLaunchProfileApplicationService(store);
        Assert.True(await application.ToggleLoadOnStartupAsync(second.Id));
        Assert.Contains(second.Id, await application.ConfiguredProfileIdsAsync());
        Assert.False(await application.ToggleLoadOnStartupAsync(second.Id));

        await store.DeleteNamedModelLaunchProfileAsync(first.Id);
        Assert.Empty(await store.ListStartupLaunchProfileIdsAsync());
    }

    [Fact]
    public async Task StartupProfileApplicationLoadsEverySelectionAndContinuesAfterAFailure()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var defaults = AppSettings.CreateDefault(root);
        var firstModel = new ModelRecord("model-a", "Alpha", Path.Combine(root, "alpha.gguf"), OwnershipKind.External, "{}", now);
        var secondModel = new ModelRecord("model-b", "Beta", Path.Combine(root, "beta.gguf"), OwnershipKind.External, "{}", now);
        var first = new NamedModelLaunchProfile(
            "profile-a", firstModel.Id, "Default", ModelLaunchSettings.FromAppSettings(defaults with { Port = 8091 }), now, true);
        var second = new NamedModelLaunchProfile(
            "profile-b", secondModel.Id, "Default", ModelLaunchSettings.FromAppSettings(defaults with { Port = 8092 }), now, true);
        await store.UpsertModelAsync(firstModel);
        await store.UpsertModelAsync(secondModel);
        await store.SaveNamedModelLaunchProfileAsync(first);
        await store.SaveNamedModelLaunchProfileAsync(second);
        var service = new StartupLaunchProfileApplicationService(store);
        await service.SetLoadOnStartupAsync(first.Id, true);
        await service.SetLoadOnStartupAsync(second.Id, true);
        var attempted = new List<string>();
        var statuses = new List<string>();

        var result = await service.LoadConfiguredAsync(new StartupLaunchProfileLoadActions(
            (model, profile, _) =>
            {
                attempted.Add(profile.Id);
                return profile.Id == first.Id
                    ? Task.FromException<LoadedModelSessionSnapshot>(new InvalidOperationException("synthetic failure"))
                    : Task.FromResult<LoadedModelSessionSnapshot>(null!);
            },
            (_, _) => false,
            statuses.Add), TestContext.Current.CancellationToken);

        Assert.Equal([first.Id, second.Id], attempted);
        Assert.Equal(2, result.ConfiguredCount);
        Assert.Equal(1, result.LoadedCount);
        Assert.Equal(0, result.AlreadyRunningCount);
        Assert.Equal(first.Id, Assert.Single(result.Failures).Choice.ProfileId);
        Assert.Equal("Loaded 1 of 2 startup profiles; 1 failed.", statuses[^1]);

        var snapshot = await service.GetSettingsSnapshotAsync();
        Assert.Empty(snapshot.Available);
        Assert.Equal([first.Id, second.Id], snapshot.Selected.Select(choice => choice.ProfileId));
    }
}
