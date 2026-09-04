using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class GatewayCatalogCacheTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task CachedRoutesRefreshAfterEverySupportedCatalogMutation()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "catalog.db"));
        await store.InitializeAsync();
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root));
        var model = new ModelRecord("one", "One", Path.Combine(root, "one.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile("default:one", model.Id, "Default", settings, model.UpdatedAt, true);
        await store.UpsertModelAsync(model);
        await store.SaveNamedModelLaunchProfileAsync(profile);
        var catalog = new ModelGatewayRouteCatalogApplicationService(store);
        var repairs = 0;
        var actions = new ModelGatewayRouteCatalogActions(async (missing, token) =>
        {
            token.ThrowIfCancellationRequested();
            repairs++;
            foreach (var item in missing) await store.SaveModelLaunchSettingsAsync(item.Id, settings);
        });
        Task<IReadOnlyList<ModelGatewayModelRoute>> Read() => catalog.ListAsync(actions, TestContext.Current.CancellationToken);

        var original = await Read();
        Assert.Same(original, await Read());
        await store.UpsertModelAsync(model with { Name = "Renamed", ModelPath = Path.Combine(root, "renamed.gguf") });
        var renamed = await Read();
        Assert.NotSame(original, renamed);
        Assert.Equal("Renamed", Assert.Single(renamed).Model.Name);
        Assert.Equal("One", Assert.Single(original).Model.Name);

        await store.SaveNamedModelLaunchProfileAsync(profile with { Settings = settings with { CustomParameters = "--alias new-alias" } });
        var aliased = await Read();
        Assert.Equal("new-alias", Assert.Single(aliased).Id);
        Assert.NotNull(ModelGatewayRequestResolver.ResolveModel(aliased, "renamed.gguf"));

        await store.SaveModelLaunchSettingsAsync(model.Id, settings with { ContextSize = 8192 });
        Assert.Equal(8192, Assert.Single(await Read()).Profile.Settings.ContextSize);
        var alternate = profile with { Id = "alternate", Name = "Alternate", IsDefault = false };
        await store.SaveNamedModelLaunchProfileAsync(alternate);
        Assert.Equal(2, (await Read()).Count);
        await store.DeleteNamedModelLaunchProfileAsync(alternate.Id);
        Assert.Single(await Read());

        await store.DeleteNamedModelLaunchProfileAsync(profile.Id);
        Assert.True(Assert.Single(await Read()).Profile.IsDefault);
        Assert.Equal(1, repairs);
        await store.UpsertModelAsync(model with { Id = "two", Name = "Two" });
        Assert.Equal(2, (await Read()).Count);
        Assert.Equal(2, repairs);
        await store.DeleteModelAsync(model.Id);
        Assert.Equal("two", Assert.Single(await Read()).Model.Id);
        await store.DeleteModelAsync("two");
        Assert.Empty(await Read());
    }

    [Fact]
    public async Task ConcurrentReadersShareRepairAndDoNotPublishAnObsoleteRevision()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "catalog.db"));
        await store.InitializeAsync();
        var model = new ModelRecord("one", "One", Path.Combine(root, "one.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(model);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new ModelGatewayRouteCatalogApplicationService(store);
        var repairs = 0;
        var actions = new ModelGatewayRouteCatalogActions(async (_, token) =>
        {
            repairs++;
            started.SetResult();
            await release.Task.WaitAsync(token);
            await store.SaveModelLaunchSettingsAsync(model.Id, ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root)));
        });
        var first = catalog.ListAsync(actions, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var readers = Enumerable.Range(0, 8).Select(_ => catalog.ListAsync(actions, TestContext.Current.CancellationToken)).ToArray();
        using var canceled = new CancellationTokenSource();
        var canceledRead = catalog.ListAsync(actions, canceled.Token);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledRead);
        await store.UpsertModelAsync(model with { Name = "Changed during repair" });
        release.SetResult();
        var result = await first;
        Assert.Equal(1, repairs);
        Assert.Equal("Changed during repair", Assert.Single(result).Model.Name);
        foreach (var read in readers) Assert.Same(result, await read);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.ListAsync(actions, canceled.Token));
    }

    [Fact]
    public void IndexedResolutionMatchesLegacyPrecedenceAndDetachesTheInputList()
    {
        var root = CreateTempRoot();
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root));
        var now = DateTimeOffset.UtcNow;
        var routes = new List<ModelGatewayModelRoute>();
        for (var index = 0; index < 50; index++)
        {
            var model = new ModelRecord($"model-{index}", $"name-{index}", Path.Combine(root, $"file-{index}.gguf"), OwnershipKind.External, "{}", now);
            var profile = new NamedModelLaunchProfile($"profile-{index}", model.Id, $"Profile {index}", settings, now, index % 2 == 0);
            routes.Add(new ModelGatewayModelRoute(model, profile, $"name-{(index + 1) % 50}"));
        }
        var snapshot = new ModelGatewayRouteSnapshot(routes);
        foreach (var route in routes)
        {
            foreach (var key in new[] { route.Id, route.LegacyId, route.Name, route.Profile.Id, route.Model.Name,
                Path.GetFileName(route.Model.ModelPath), Path.GetFileNameWithoutExtension(route.Model.ModelPath), "missing", "" })
            {
                var requested = " " + key.ToUpperInvariant() + " ";
                Assert.Equal(ModelGatewayRequestResolver.ResolveModel(routes, requested), ModelGatewayRequestResolver.ResolveModel(snapshot, requested));
            }
        }
        routes.Clear();
        Assert.Equal(50, snapshot.Count);
        Assert.Equal(50, snapshot.ToArray().Length);
        Assert.Null(snapshot.Resolve("missing"));
    }
}
