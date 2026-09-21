using System.Text.Json.Nodes;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class ModelReattachTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task ReattachKeepsStableIdentityAndDependentProfileStateWhileConsolidatingDuplicate()
    {
        var root = CreateTempRoot();
        var oldPath = Path.Combine(root, "old", "model.gguf");
        var newPath = Path.Combine(root, "new", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        WriteMinimalGguf(newPath, "qwen35");
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();

        var oldModel = new ModelRecord("stable-model", "Stable Model", oldPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var duplicate = new ModelRecord("path-derived-duplicate", "Duplicate", newPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root));
        var defaultProfile = new NamedModelLaunchProfile("stable-default", oldModel.Id, "Default", settings, DateTimeOffset.UtcNow, IsDefault: true);
        var codingProfile = new NamedModelLaunchProfile("stable-coding", oldModel.Id, "Coding", settings with { Port = 8091 }, DateTimeOffset.UtcNow);
        var duplicateDefault = new NamedModelLaunchProfile("duplicate-default", duplicate.Id, "Default", settings with { Port = 8092 }, DateTimeOffset.UtcNow, IsDefault: true);
        var benchmarkProfile = new NamedModelLaunchProfile("duplicate-benchmark", duplicate.Id, "Benchmark", settings with { Port = 8093 }, DateTimeOffset.UtcNow);
        var group = new ModelGroupRecord("group", "Batch", ModelGroupRetentionMode.Pinned, 15, ModelGroupEvictionPriority.Normal, DateTimeOffset.UtcNow);

        await store.UpsertModelAsync(oldModel);
        await store.UpsertModelAsync(duplicate);
        await store.SaveNamedModelLaunchProfileAsync(defaultProfile);
        await store.SaveNamedModelLaunchProfileAsync(codingProfile);
        await store.SaveNamedModelLaunchProfileAsync(duplicateDefault);
        await store.SaveNamedModelLaunchProfileAsync(benchmarkProfile);
        await store.UpsertModelGroupAsync(group);
        await store.AssignLaunchProfileGroupAsync(new ModelGroupAssignment(codingProfile.Id, group.Id, DateTimeOffset.UtcNow));
        await store.SetStartupLaunchProfileAsync(codingProfile.Id, loadOnStartup: true);
        await store.SetLaunchProfileFavoriteAsync(codingProfile.Id, favorite: true);
        await store.ToggleSelectorFavoriteAsync(SelectorFavoriteKind.Model, duplicate.Id);

        var result = await new ModelCatalogService(store).ReattachFileAsync(
            oldModel,
            newPath,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(oldModel.Id, result.Model.Id);
        Assert.Equal(Path.GetFullPath(newPath), result.Model.ModelPath);
        Assert.Equal(OwnershipKind.External, result.Model.Ownership);
        Assert.Equal(1, result.RemovedDuplicateRecords);
        Assert.Equal(1, result.MovedLaunchProfiles);
        Assert.Empty(result.IdentityWarnings);
        var persisted = Assert.Single(await store.ListModelsAsync());
        Assert.Equal(oldModel.Id, persisted.Id);
        Assert.Equal(Path.GetFullPath(newPath), persisted.ModelPath);
        Assert.Equal("reattached-file", JsonNode.Parse(persisted.MetadataJson)?["registrationSource"]?.ToString());

        var profiles = await store.ListNamedModelLaunchProfilesAsync(oldModel.Id);
        Assert.Equal(["Benchmark", "Coding", "Default"], profiles.Select(profile => profile.Name).Order().ToArray());
        Assert.Contains(profiles, profile => profile.Id == defaultProfile.Id && profile.IsDefault);
        Assert.Contains(profiles, profile => profile.Id == codingProfile.Id);
        Assert.Contains(profiles, profile => profile.Id == benchmarkProfile.Id && !profile.IsDefault);
        Assert.DoesNotContain(profiles, profile => profile.Id == duplicateDefault.Id);
        Assert.Equal(group.Id, (await store.GetModelGroupSnapshotAsync()).GroupForProfile(codingProfile.Id)?.Id);
        Assert.Contains(codingProfile.Id, await store.ListStartupLaunchProfileIdsAsync());
        Assert.Contains(codingProfile.Id, await store.ListFavoriteLaunchProfileIdsAsync());
        Assert.Contains(oldModel.Id, await store.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.Model));
    }

    [Fact]
    public async Task ReattachRequiresExplicitConfirmationWhenStoredIdentityDiffers()
    {
        var root = CreateTempRoot();
        var oldPath = Path.Combine(root, "missing.gguf");
        var newPath = Path.Combine(root, "relocated.gguf");
        WriteMinimalGguf(newPath, "qwen35");
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var model = new ModelRecord(
            "model",
            "Model",
            oldPath,
            OwnershipKind.External,
            new JsonObject { ["ggufSizeBytes"] = 1 }.ToJsonString(),
            DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(model);
        var catalog = new ModelCatalogService(store);

        var inspection = await catalog.InspectReattachFileAsync(model, newPath, TestContext.Current.CancellationToken);

        Assert.True(inspection.RequiresIdentityConfirmation);
        Assert.Contains(inspection.IdentityWarnings, warning => warning.Contains("file size", StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.ReattachFileAsync(model, inspection));
        var result = await catalog.ReattachFileAsync(model, inspection, confirmIdentityMismatch: true);
        Assert.Equal(Path.GetFullPath(newPath), result.Model.ModelPath);
    }

    [Fact]
    public async Task ReattachApplicationConfirmsMismatchAndRefreshesBothModelSurfaces()
    {
        Loc.LoadLanguage("en");
        var root = CreateTempRoot();
        var oldPath = Path.Combine(root, "missing.gguf");
        var newPath = Path.Combine(root, "relocated.gguf");
        var model = new ModelRecord("model", "Model", oldPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var classification = new GgufFileClassification(newPath, GgufFileRole.MainModel, GgufClassificationConfidence.Metadata, "main model");
        var inspection = new ModelReattachInspection(newPath, classification, ["Its file size changed."], []);
        var refreshes = new List<string>();
        var mismatchConfirmed = false;
        var reattachConfirmedMismatch = false;

        var outcome = await new ModelReattachApplicationService().ChooseAndReattachAsync(
            model,
            root,
            new ModelReattachApplicationActions(
                request =>
                {
                    Assert.Equal(Path.GetFileName(oldPath), request.FileName);
                    return newPath;
                },
                _ => throw new InvalidOperationException("Role confirmation was not expected."),
                confirmation => mismatchConfirmed = confirmation.Inspection == inspection,
                _ => false,
                (_, _) => Task.FromResult(inspection),
                (_, inspected, _, confirmMismatch) =>
                {
                    Assert.Same(inspection, inspected);
                    reattachConfirmedMismatch = confirmMismatch;
                    return Task.FromResult(new ModelReattachResult(model with { ModelPath = newPath }, classification, 0, 0, inspection.IdentityWarnings));
                },
                async (_, action) => await action(),
                () => { refreshes.Add("models"); return Task.CompletedTask; },
                () => { refreshes.Add("overview"); return Task.CompletedTask; },
                _ => { }));

        Assert.Equal(ModelReattachApplicationOutcome.Reattached, outcome);
        Assert.True(mismatchConfirmed);
        Assert.True(reattachConfirmedMismatch);
        Assert.Equal(["models", "overview"], refreshes);
    }

    [Fact]
    public void MissingModelRowOffersLocateActionAndKeepsProfilesVisible()
    {
        Loc.LoadLanguage("en");
        var model = new ModelRecord("model", "Model", "missing.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile(
            "profile",
            model.Id,
            "Coding",
            ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(CreateTempRoot())),
            DateTimeOffset.UtcNow,
            IsDefault: true);
        var viewModel = new ModelsPageViewModel();

        viewModel.ReplaceModels([model], _ => false, [profile], new Dictionary<string, string> { [model.Id] = "Missing" });

        var row = Assert.Single(viewModel.Rows);
        Assert.True(row.IsMissing);
        Assert.True(row.CanOpenFolder);
        Assert.Equal(Loc.T("Models.Reattach.Action"), row.OpenFolderAction);
        Assert.Equal(profile.Id, Assert.Single(viewModel.VariantRows).LaunchProfile?.Id);
    }

    [Fact]
    public void ControlCliBuildsModelReattachRequest()
    {
        var request = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests(
            "models", "reattach", "stable-model", "--file", @"D:\Models\relocated.gguf", "--confirm-mismatch");

        Assert.Equal("POST", request.Method);
        Assert.Equal("/api/v1/models/stable-model/reattach", request.Path);
        Assert.Equal(@"D:\Models\relocated.gguf", request.Body?["file"]?.GetValue<string>());
        Assert.True(request.Body?["confirmIdentityMismatch"]?.GetValue<bool>());
        Assert.False(request.Body?["confirmRole"]?.GetValue<bool>());
    }
}
