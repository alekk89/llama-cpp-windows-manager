using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.Tests;

public sealed class DefaultRuntimeTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task DefaultIsExclusivePersistsAndClearsWhenDeleted()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "state", "manager.db");
        var first = Runtime("first");
        var second = Runtime("second");
        await using (var store = new StateStore(path))
        {
            await store.InitializeAsync();
            Assert.Equal("", await store.GetDefaultRuntimeIdAsync());
            await store.UpsertRuntimeAsync(first);
            await store.UpsertRuntimeAsync(second);
            await store.SetDefaultRuntimeAsync(first.Id);
            await store.SetDefaultRuntimeAsync(second.Id);
            Assert.Equal(second.Id, await store.GetDefaultRuntimeIdAsync());
            await store.DeleteRuntimeAsync(first.Id);
            Assert.Equal(second.Id, await store.GetDefaultRuntimeIdAsync());
        }
        await using var reopened = new StateStore(path);
        await reopened.InitializeAsync();
        Assert.Equal(second.Id, await reopened.GetDefaultRuntimeIdAsync());
        await reopened.DeleteRuntimeAsync(second.Id);
        Assert.Equal("", await reopened.GetDefaultRuntimeIdAsync());
        await reopened.UpsertRuntimeAsync(second);
        Assert.Equal("", await reopened.GetDefaultRuntimeIdAsync());
    }

    [Fact]
    public async Task RuntimeMenuCanReplaceAndClearDefaultButCannotSelectMissingExecutable()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        var first = Runtime("first");
        var second = Runtime("second");
        var missing = Runtime("missing") with { ExecutablePath = Path.Combine(root, "missing.exe") };
        foreach (var runtime in new[] { first, second, missing }) await store.UpsertRuntimeAsync(runtime);
        var service = new RuntimeCatalogCommandApplicationService(new RuntimeCustomRepositoryService());
        Task<string> Toggle(RuntimeRecord runtime) => service.ToggleDefaultRuntimeAsync(store, runtime, () => Task.CompletedTask, _ => { });
        Assert.Equal(first.Id, await Toggle(first));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Toggle(missing));
        Assert.Equal(first.Id, await store.GetDefaultRuntimeIdAsync());
        Assert.Equal(second.Id, await Toggle(second));
        Assert.Equal("", await Toggle(second));
        await store.SetDefaultRuntimeAsync(missing.Id);
        Assert.Equal("", await Toggle(missing));
    }

    [Fact]
    public async Task NewProfilesUseCurrentDefaultAndExistingOrCopiedProfilesKeepTheirRuntime()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        await using var store = new StateStore(Path.Combine(root, "state", "manager.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var profiles = new ModelLaunchProfileService(store, sessions);
        var first = Runtime("first");
        var second = Runtime("second");
        await store.UpsertRuntimeAsync(first);
        await store.UpsertRuntimeAsync(second);
        ModelRecord Model(string id) => new(id, id, Path.Combine(root, id + ".gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var oldModel = Model("old");
        var newModel = Model("new");
        var noDefaultModel = Model("no-default");
        foreach (var model in new[] { oldModel, newModel, noDefaultModel }) await store.UpsertModelAsync(model);
        await store.SetDefaultRuntimeAsync(first.Id);
        var oldProfile = await profiles.EnsureDefaultAsync(oldModel, settings);
        Assert.Equal(first.Id, oldProfile.Settings.RuntimeId);
        await store.SetDefaultRuntimeAsync(second.Id);
        Assert.Equal(second.Id, (await profiles.DraftAsync(newModel, settings)).RuntimeId);
        Assert.Equal(second.Id, (await profiles.EnsureDefaultAsync(newModel, settings)).Settings.RuntimeId);
        Assert.Equal(oldProfile, await profiles.EnsureDefaultAsync(oldModel, settings));
        Assert.Equal(first.Id, (await profiles.DraftAsync(oldModel, settings)).RuntimeId);
        var variant = await new ModelLaunchVariantWorkflowService(profiles).SaveAsNewAsync(new ModelLaunchVariantWorkflowRequest(oldModel, "Copy", settings, first.Id, settings), TestContext.Current.CancellationToken);
        Assert.True(variant.Success);
        Assert.Equal(first.Id, variant.SavedSettings!.RuntimeId);
        await store.DeleteRuntimeAsync(second.Id);
        Assert.Equal("", (await profiles.EnsureDefaultAsync(noDefaultModel, settings)).Settings.RuntimeId);
    }

    [Fact]
    public void RuntimeHighlightTracksOnlyTheDefaultAcrossRefreshesAndFilters()
    {
        var viewModel = new RuntimesPageViewModel();
        var rows = new[] { Row(Runtime("first")), Row(Runtime("second")), Row(null) };
        viewModel.ReplaceRows(rows, defaultRuntimeId: "FIRST");
        Assert.Equal("first", Assert.Single(viewModel.Rows, row => row.IsDefaultRuntime).Runtime!.Id);
        viewModel.ReplaceRows(rows, defaultRuntimeId: "second");
        viewModel.ApplyFilters("All", "All");
        Assert.Equal("second", Assert.Single(viewModel.Rows, row => row.IsDefaultRuntime).Runtime!.Id);
        viewModel.ReplaceRows(rows);
        Assert.DoesNotContain(viewModel.Rows, row => row.IsDefaultRuntime);
    }

    private static RuntimeRecord Runtime(string id) => new(id, id, RuntimeMode.Native, RuntimeBackend.Cpu,
        typeof(DefaultRuntimeTests).Assembly.Location, "{}", DateTimeOffset.UtcNow);

    private static RuntimeCatalogRow Row(RuntimeRecord? runtime) => new()
    {
        Name = runtime?.Name ?? "Source",
        Backend = "CPU",
        State = "Ready",
        Location = "",
        Details = "",
        Runtime = runtime
    };
}
