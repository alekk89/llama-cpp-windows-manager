using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class ModelReattachConflictsTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task ConflictingDuplicateProfileIsRenamedWithItsReferences()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var (model, duplicate, newPath) = await SeedAsync(store, root);
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root));
        await store.SaveNamedModelLaunchProfileAsync(new NamedModelLaunchProfile("stable-coding", model.Id, "Coding", settings, DateTimeOffset.UtcNow));
        await store.SaveNamedModelLaunchProfileAsync(new NamedModelLaunchProfile("stable-coding-relocated", model.Id, "coding (RELOCATED)", settings, DateTimeOffset.UtcNow));
        var duplicateCoding = new NamedModelLaunchProfile("duplicate-coding", duplicate.Id, "Coding", settings with { Port = 8095 }, DateTimeOffset.UtcNow);
        await store.SaveNamedModelLaunchProfileAsync(duplicateCoding);
        var group = new ModelGroupRecord("group", "Batch", ModelGroupRetentionMode.Pinned, 15, ModelGroupEvictionPriority.Normal, DateTimeOffset.UtcNow);
        await store.UpsertModelGroupAsync(group);
        await store.AssignLaunchProfileGroupAsync(new ModelGroupAssignment(duplicateCoding.Id, group.Id, DateTimeOffset.UtcNow));
        await store.SetStartupLaunchProfileAsync(duplicateCoding.Id, loadOnStartup: true);
        await store.SetLaunchProfileFavoriteAsync(duplicateCoding.Id, favorite: true);

        var result = await new ModelCatalogService(store).ReattachFileAsync(model, newPath, cancellationToken: TestContext.Current.CancellationToken);

        var renamed = Assert.Single(result.RenamedLaunchProfiles);
        Assert.Equal(new ModelReattachRenamedProfile(duplicateCoding.Id, "Coding", "Coding (relocated 2)", WasDefault: false), renamed);
        var moved = Assert.Single(await store.ListNamedModelLaunchProfilesAsync(model.Id), profile => profile.Id == duplicateCoding.Id);
        Assert.Equal("Coding (relocated 2)", moved.Name);
        Assert.Equal(8095, moved.Settings.Port);
        Assert.Equal(group.Id, (await store.GetModelGroupSnapshotAsync()).GroupForProfile(duplicateCoding.Id)?.Id);
        Assert.Contains(duplicateCoding.Id, await store.ListStartupLaunchProfileIdsAsync());
        Assert.Contains(duplicateCoding.Id, await store.ListFavoriteLaunchProfileIdsAsync());
    }

    [Fact]
    public void RelocatedNameRespectsProfileNameLimitAndUniqueness()
    {
        var longName = new string('x', 200);
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { new string('x', 188) + " (relocated)" };

        var name = StateStore.RelocatedProfileName(longName, existing);

        Assert.Equal(200, name.Length);
        Assert.EndsWith(" (relocated 2)", name, StringComparison.Ordinal);
        Assert.Equal("Default (relocated)", StateStore.RelocatedProfileName("Default", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Default" }));
    }

    [Fact]
    public async Task ReattachWaitsForLifecycleGateBeforeChangingRegistration()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var (model, _, newPath) = await SeedAsync(store, root);
        var catalog = new ModelCatalogService(store);
        var inspection = await catalog.InspectReattachFileAsync(model, newPath, TestContext.Current.CancellationToken);
        var gateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = sessions.ExecuteLifecycleAsync(async () =>
        {
            gateEntered.SetResult();
            await releaseGate.Task;
        }, TestContext.Current.CancellationToken);
        await gateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var reattach = new ModelReattachWorkflowService(catalog, sessions).ReattachAsync(model, inspection, false, false, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(reattach.IsCompleted);
        Assert.Equal(2, (await store.ListModelsAsync()).Count);
        releaseGate.SetResult();
        await holder;
        var result = await reattach.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(Path.GetFullPath(newPath), result.Model.ModelPath);
        Assert.Single(await store.ListModelsAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReattachRechecksLoadedSourceAndDuplicateInsideGate(bool loadSource)
    {
        Loc.LoadLanguage("en");
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var (model, duplicate, newPath) = await SeedAsync(store, root);
        var catalog = new ModelCatalogService(store);
        var workflow = new ModelReattachWorkflowService(catalog, sessions);
        var inspection = await catalog.InspectReattachFileAsync(model, newPath, TestContext.Current.CancellationToken);
        var statuses = new List<string>();
        var loaded = loadSource ? model : duplicate;

        // The load happens after the pre-dialog check passed, as a concurrent load would.
        var outcome = await new ModelReattachApplicationService().ChooseAndReattachAsync(
            model,
            root,
            new ModelReattachApplicationActions(
                _ => newPath,
                _ => true,
                _ => true,
                _ => false,
                (_, _) => Task.FromResult(inspection),
                (target, inspected, confirmRole, confirmMismatch) =>
                {
                    AttachLoadedSession(sessions, loaded, root);
                    return workflow.ReattachAsync(target, inspected, confirmRole, confirmMismatch);
                },
                async (_, action) => await action(),
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                statuses.Add));

        Assert.Equal(ModelReattachApplicationOutcome.Blocked, outcome);
        Assert.Equal(
            loadSource ? Loc.T("Models.Reattach.LoadedStatus", model.Name) : Loc.T("Models.Reattach.TargetLoadedStatus"),
            Assert.Single(statuses));
        Assert.Equal(2, (await store.ListModelsAsync()).Count);
        var blocked = await Assert.ThrowsAsync<ModelReattachBlockedException>(() => workflow.ReattachAsync(model, inspection, false, false, TestContext.Current.CancellationToken));
        Assert.Equal(loaded.Id, blocked.BlockingModel.Id);
        Assert.Equal(loadSource, blocked.IsSource);
    }

    [Fact]
    public async Task ReattachRejectsDuplicatesRegisteredAfterReview()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var (model, _, newPath) = await SeedAsync(store, root);
        var catalog = new ModelCatalogService(store);
        var inspection = await catalog.InspectReattachFileAsync(model, newPath, TestContext.Current.CancellationToken);
        await store.UpsertModelAsync(new ModelRecord("late-duplicate", "Late", newPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ModelReattachWorkflowService(catalog, sessions).ReattachAsync(model, inspection, false, false, TestContext.Current.CancellationToken));

        Assert.Equal(3, (await store.ListModelsAsync()).Count);
    }

    [Fact]
    public void CompletedStatusReportsRenamedProfiles()
    {
        Loc.LoadLanguage("en");
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var classification = new GgufFileClassification("model.gguf", GgufFileRole.MainModel, GgufClassificationConfidence.Metadata, "main model");
        var result = new ModelReattachResult(model, classification, 1, 2, [], [new("p", "Default", "Default (relocated)", true)]);

        var status = ModelReattachApplicationService.BuildCompletedStatus(result);

        Assert.StartsWith(Loc.T("Models.Reattach.CompletedMergedStatus", model.Name, 1), status, StringComparison.Ordinal);
        Assert.Contains("'Default' → 'Default (relocated)'", status, StringComparison.Ordinal);
    }

    private static async Task<(ModelRecord Model, ModelRecord Duplicate, string NewPath)> SeedAsync(StateStore store, string root)
    {
        var newPath = Path.Combine(root, "new", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        WriteMinimalGguf(newPath, "qwen35");
        var model = new ModelRecord("stable-model", "Stable Model", Path.Combine(root, "old", "model.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var duplicate = new ModelRecord("path-duplicate", "Duplicate", newPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(model);
        await store.UpsertModelAsync(duplicate);
        return (model, duplicate, newPath);
    }

    private static void AttachLoadedSession(LoadedModelSessionManager sessions, ModelRecord model, string root)
    {
        var runtime = new RuntimeRecord("runtime", "llama.cpp CPU", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        sessions.AttachExisting(
            runtime,
            model,
            AppSettings.CreateDefault(root) with { Port = 8181 },
            Path.Combine(root, "session.log"),
            LlamaRuntimeState.Loaded,
            "",
            LoadedModelSessionManager.SessionIdFor(model.Id),
            DateTimeOffset.UtcNow);
    }
}
