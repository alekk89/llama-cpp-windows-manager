using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class TrayProfilesTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task FavoriteProfilesPersistAndCascadeWithProfileDeletion()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var model = Model(root, "model-a", "Alpha");
        var profile = Profile(root, model, "profile-a", "Default", isDefault: true);
        await store.UpsertModelAsync(model);
        await store.SaveNamedModelLaunchProfileAsync(profile);

        await store.SetLaunchProfileFavoriteAsync(profile.Id, true);

        Assert.True(await store.IsLaunchProfileFavoriteAsync(profile.Id));
        Assert.Contains(profile.Id, await store.ListFavoriteLaunchProfileIdsAsync());
        Assert.Contains(model.Id, await store.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.Model));

        await store.DeleteNamedModelLaunchProfileAsync(profile.Id);

        Assert.False(await store.IsLaunchProfileFavoriteAsync(profile.Id));
        Assert.Empty(await store.ListFavoriteLaunchProfileIdsAsync());
    }

    [Fact]
    public async Task SelectorFavoritesPersistForModelsProfilesAndRuntimesAndCascadeOnDeletion()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var model = Model(root, "model-favorite", "Favorite model");
        var profile = Profile(root, model, "profile-favorite", "Favorite profile", isDefault: true);
        var runtime = new RuntimeRecord(
            "runtime-favorite",
            "Favorite runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(root, "llama-server.exe"),
            "{}",
            DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(model);
        await store.SaveNamedModelLaunchProfileAsync(profile);
        await store.UpsertRuntimeAsync(runtime);

        Assert.True(await store.ToggleSelectorFavoriteAsync(SelectorFavoriteKind.Model, model.Id));
        Assert.True(await store.ToggleSelectorFavoriteAsync(SelectorFavoriteKind.LaunchProfile, profile.Id));
        Assert.True(await store.ToggleSelectorFavoriteAsync(SelectorFavoriteKind.Runtime, runtime.Id));
        Assert.Contains(model.Id, await store.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.Model));
        Assert.Contains(profile.Id, await store.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.LaunchProfile));
        Assert.Contains(runtime.Id, await store.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.Runtime));

        Assert.False(await store.ToggleSelectorFavoriteAsync(SelectorFavoriteKind.Model, model.Id));
        Assert.Empty(await store.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.Model));
        Assert.True(await store.ToggleSelectorFavoriteAsync(SelectorFavoriteKind.Model, model.Id));
        await store.DeleteModelAsync(model.Id);
        await store.DeleteRuntimeAsync(runtime.Id);

        Assert.Empty(await store.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.Model));
        Assert.Empty(await store.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.LaunchProfile));
        Assert.Empty(await store.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.Runtime));
    }

    [Fact]
    public async Task SnapshotSortsFavoritesAndDescribesConcurrentStartAndStopActions()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var alpha = Model(root, "model-a", "Alpha");
        var beta = Model(root, "model-b", "Beta");
        var alphaDefault = Profile(root, alpha, "profile-a-default", "Default", isDefault: true);
        var alphaLong = Profile(root, alpha, "profile-a-long", "Long context");
        var betaDefault = Profile(root, beta, "profile-b-default", "Default", isDefault: true);
        foreach (var model in new[] { beta, alpha })
            await store.UpsertModelAsync(model);
        foreach (var profile in new[] { alphaLong, betaDefault, alphaDefault })
            await store.SaveNamedModelLaunchProfileAsync(profile);
        await store.SetLaunchProfileFavoriteAsync(alphaLong.Id, true);
        await store.SetLaunchProfileFavoriteAsync(betaDefault.Id, true);

        var runtime = new RuntimeRecord(
            "runtime-1",
            "Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            "llama-server.exe",
            "{}",
            DateTimeOffset.UtcNow);
        sessions.AttachExisting(
            runtime,
            alpha,
            alphaDefault.Settings.ApplyTo(AppSettings.CreateDefault(root)),
            Path.Combine(root, "alpha.log"),
            LlamaRuntimeState.Loaded,
            "",
            LoadedModelSessionManager.SessionIdFor(alpha.Id),
            DateTimeOffset.UtcNow,
            launchProfileId: alphaDefault.Id,
            launchProfileName: alphaDefault.Name);

        var application = new TrayProfileMenuApplicationService(store, sessions);
        var snapshot = await application.BuildSnapshotAsync();

        Assert.Equal(["Alpha", "Beta"], snapshot.Models.Select(model => model.Model.Name));
        Assert.Equal(["Alpha · Long context", "Beta · Default"], snapshot.Favorites.Select(
            favorite => $"{favorite.Model.Name} · {favorite.Profile.Name}"));
        var alphaProfiles = snapshot.Models[0].Profiles;
        Assert.Equal(["Default", "Long context"], alphaProfiles.Select(profile => profile.Profile.Name));
        Assert.Equal(TrayProfileActionKind.Stop, alphaProfiles[0].Action);
        Assert.Equal(TrayProfileActionKind.Start, alphaProfiles[1].Action);
        Assert.Equal(TrayProfileActionKind.Start, snapshot.Models[1].Profiles[0].Action);
        Assert.All(snapshot.Models.SelectMany(model => model.Profiles), profile => Assert.True(profile.CanExecute));

        var loadedProfileId = "";
        var stopCalls = 0;
        var actions = new TrayProfileCommandActions(
            (_, profile) =>
            {
                loadedProfileId = profile.Id;
                return Task.FromResult(ModelRuntimeLoadApplicationOutcome.Started);
            },
            (_, _) =>
            {
                stopCalls++;
                return Task.CompletedTask;
            });
        var switchResult = await application.ExecuteAsync(alphaProfiles[1], actions);
        var stopResult = await application.ExecuteAsync(alphaProfiles[0], actions);

        Assert.Equal(TrayProfileActionKind.Start, switchResult.Action);
        Assert.Equal(ModelRuntimeLoadApplicationOutcome.Started, switchResult.LoadOutcome);
        Assert.Equal(alphaLong.Id, loadedProfileId);
        Assert.Equal(TrayProfileActionKind.Stop, stopResult.Action);
        Assert.False(stopResult.StopCompleted);
        Assert.Equal(1, stopCalls);
    }

    private static ModelRecord Model(string root, string id, string name)
        => new(id, name, Path.Combine(root, $"{id}.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);

    private static NamedModelLaunchProfile Profile(
        string root,
        ModelRecord model,
        string id,
        string name,
        bool isDefault = false)
        => new(
            id,
            model.Id,
            name,
            ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root)) with
            {
                RuntimeId = "runtime-1",
                Port = 8100 + Math.Abs(id.GetHashCode(StringComparison.Ordinal)) % 1000
            },
            DateTimeOffset.UtcNow,
            isDefault);
}
