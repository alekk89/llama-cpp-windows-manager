using LocalLlmConsole.Localization;
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
        await store.SetLaunchProfileFavoriteAsync(second.Id, true);

        var application = new StartupLaunchProfileApplicationService(store);
        var initialSnapshot = await application.GetSettingsSnapshotAsync();
        Assert.Equal([second.Id, first.Id], initialSnapshot.Available.Select(choice => choice.ProfileId));

        await store.SetStartupLaunchProfileAsync(second.Id, true);
        await store.SetStartupLaunchProfileAsync(first.Id, true);
        await store.SetStartupLaunchProfileAsync(second.Id, true);
        Assert.Equal([second.Id, first.Id], await store.ListStartupLaunchProfileIdsAsync());

        await store.SetStartupLaunchProfileAsync(second.Id, false);
        Assert.Equal([first.Id], await store.ListStartupLaunchProfileIdsAsync());

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
        Assert.Equal(Loc.T("StartupProfiles.Status.LoadedWithFailures", 1, 2, 1), statuses[^1]);

        var snapshot = await service.GetSettingsSnapshotAsync();
        Assert.Empty(snapshot.Available);
        Assert.Equal([first.Id, second.Id], snapshot.Selected.Select(choice => choice.ProfileId));
    }

    [Fact]
    public async Task StartupProfileLoadReportsEveryProfileStateInOrder()
    {
        await using var store = await CreateStoreAsync();
        var (service, first, second, third) = await CreateThreeStartupProfilesAsync(store);
        var progress = new List<(string ProfileId, StartupLaunchProfileLoadState State)>();

        var result = await service.LoadConfiguredAsync(new StartupLaunchProfileLoadActions(
            (_, profile, _) => profile.Id == second.Id
                ? Task.FromException<LoadedModelSessionSnapshot>(new InvalidOperationException("port in use"))
                : Task.FromResult<LoadedModelSessionSnapshot>(null!),
            (_, profile) => profile.Id == third.Id,
            _ => { },
            update => progress.Add((update.Choice.ProfileId, update.State))), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                (first.Id, StartupLaunchProfileLoadState.Pending),
                (second.Id, StartupLaunchProfileLoadState.Pending),
                (third.Id, StartupLaunchProfileLoadState.Pending),
                (first.Id, StartupLaunchProfileLoadState.Loading),
                (first.Id, StartupLaunchProfileLoadState.Loaded),
                (second.Id, StartupLaunchProfileLoadState.Loading),
                (second.Id, StartupLaunchProfileLoadState.Failed),
                (third.Id, StartupLaunchProfileLoadState.AlreadyRunning)
            ],
            progress);
        Assert.Equal(2, result.LoadedCount);
        Assert.Equal(1, result.AlreadyRunningCount);
        Assert.False(result.Cancelled);
        Assert.Equal("port in use", Assert.Single(result.Failures).Exception.Message);
    }

    [Fact]
    public async Task CancellingStartupProfilesSkipsTheProfilesThatHaveNotStarted()
    {
        await using var store = await CreateStoreAsync();
        var (service, first, second, third) = await CreateThreeStartupProfilesAsync(store);
        using var cancellation = new CancellationTokenSource();
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstLoad = new TaskCompletionSource<LoadedModelSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempted = new List<string>();
        var states = new Dictionary<string, StartupLaunchProfileLoadState>();
        var statuses = new List<string>();

        var loading = service.LoadConfiguredAsync(new StartupLaunchProfileLoadActions(
            (_, profile, _) =>
            {
                attempted.Add(profile.Id);
                loadStarted.TrySetResult();
                return firstLoad.Task;
            },
            (_, _) => false,
            statuses.Add,
            update => states[update.Choice.ProfileId] = update.State), cancellation.Token);
        await loadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        firstLoad.SetResult(null!);
        var result = await loading;

        Assert.Equal([first.Id], attempted);
        Assert.Equal(StartupLaunchProfileLoadState.Loaded, states[first.Id]);
        Assert.Equal(StartupLaunchProfileLoadState.Skipped, states[second.Id]);
        Assert.Equal(StartupLaunchProfileLoadState.Skipped, states[third.Id]);
        Assert.True(result.Cancelled);
        Assert.Equal(1, result.LoadedCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Equal(Loc.T("StartupProfiles.Status.Cancelled", 1, 3), statuses[^1]);
    }

    [Fact]
    public async Task AnAbortedInFlightStartupLoadIsReportedAsSkippedNotFailed()
    {
        await using var store = await CreateStoreAsync();
        var (service, first, second, _) = await CreateThreeStartupProfilesAsync(store);
        using var cancellation = new CancellationTokenSource();
        var states = new Dictionary<string, StartupLaunchProfileLoadState>();

        var result = await service.LoadConfiguredAsync(new StartupLaunchProfileLoadActions(
            async (_, _, token) =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                return null!;
            },
            (_, _) => false,
            _ => { },
            update => states[update.Choice.ProfileId] = update.State), cancellation.Token);

        Assert.Empty(result.Failures);
        Assert.Equal(3, result.SkippedCount);
        Assert.Equal(StartupLaunchProfileLoadState.Skipped, states[first.Id]);
        Assert.Equal(StartupLaunchProfileLoadState.Skipped, states[second.Id]);
    }

    private static async Task<StateStore> CreateStoreAsync()
    {
        var store = new StateStore(Path.Combine(CreateTempRoot(), "state", "local-llm-console.db"));
        await store.InitializeAsync();
        return store;
    }

    private static async Task<(StartupLaunchProfileApplicationService Service, NamedModelLaunchProfile First, NamedModelLaunchProfile Second, NamedModelLaunchProfile Third)>
        CreateThreeStartupProfilesAsync(StateStore store)
    {
        var root = CreateTempRoot();
        var now = DateTimeOffset.UtcNow;
        var defaults = AppSettings.CreateDefault(root);
        var profiles = new List<NamedModelLaunchProfile>();
        var service = new StartupLaunchProfileApplicationService(store);
        foreach (var (name, port) in new[] { ("Alpha", 8091), ("Beta", 8092), ("Gamma", 8093) })
        {
            var model = new ModelRecord($"model-{name}", name, Path.Combine(root, $"{name}.gguf"), OwnershipKind.External, "{}", now);
            var profile = new NamedModelLaunchProfile(
                $"profile-{name}", model.Id, "Default", ModelLaunchSettings.FromAppSettings(defaults with { Port = port }), now, true);
            await store.UpsertModelAsync(model);
            await store.SaveNamedModelLaunchProfileAsync(profile);
            await service.SetLoadOnStartupAsync(profile.Id, true);
            profiles.Add(profile);
        }

        return (service, profiles[0], profiles[1], profiles[2]);
    }
}
