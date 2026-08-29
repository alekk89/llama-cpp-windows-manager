using System.Text.Json.Nodes;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class ModelImportTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task ExplicitFileImportPersistsAmbiguousRoleConfirmationAcrossScans()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var path = Path.Combine(models, "Qwen3-MTP-head-Q4_K_M.gguf");
        WriteMinimalGguf(path, "qwen35");
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);

        var classification = ModelCatalogService.ClassifyGguf(path);
        Assert.Equal(GgufFileRole.Ambiguous, classification.Role);
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.ImportFileAsync(path));

        var imported = await catalog.ImportFileAsync(path, confirmRole: true);
        var metadata = JsonNode.Parse(imported.MetadataJson)!;
        Assert.Equal("manual-file", metadata["registrationSource"]?.GetValue<string>());
        Assert.True(metadata["userConfirmedMainModel"]?.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(metadata["confirmedMainModelIdentity"]?.GetValue<string>()));
        Assert.Equal(nameof(GgufFileRole.Ambiguous), metadata["detectedRole"]?.GetValue<string>());

        var scan = await catalog.ScanDetailedAsync(models);
        var rescanned = Assert.Single(scan.RegisteredModels);
        Assert.Equal(imported.Id, rescanned.Id);
        Assert.Equal(GgufFileRole.MainModel, Assert.Single(scan.Files).Role);
        Assert.Contains("Previously confirmed", Assert.Single(scan.Files).Reason, StringComparison.Ordinal);
        Assert.True(JsonNode.Parse(rescanned.MetadataJson)!["userConfirmedMainModel"]?.GetValue<bool>());
    }

    [Fact]
    public async Task ExplicitRoleConfirmationDoesNotFollowAPathAfterGgufReplacement()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var path = Path.Combine(models, "Qwen3-MTP-head-Q4_K_M.gguf");
        WriteMinimalGguf(path, "qwen35");
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);
        await catalog.ImportFileAsync(path, confirmRole: true);

        WriteMinimalGguf(path, "clip");
        var scan = await catalog.ScanDetailedAsync(models);

        Assert.Empty(scan.RegisteredModels);
        Assert.Equal(GgufFileRole.VisionProjector, Assert.Single(scan.Files).Role);
    }

    [Fact]
    public async Task ExplicitMainModelImportDoesNotPersistAnUnneededRoleOverride()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "main.gguf");
        WriteMinimalGguf(path, "qwen35");
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();

        var imported = await new ModelCatalogService(store).ImportFileAsync(path);
        var metadata = JsonNode.Parse(imported.MetadataJson)!;

        Assert.Null(metadata["userConfirmedMainModel"]);
        Assert.Null(metadata["confirmedMainModelIdentity"]);
    }

    [Fact]
    public async Task ExplicitFileImportRejectsUnreadableGgufEvenWithRoleConfirmation()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "not-a-model.gguf");
        await File.WriteAllTextAsync(path, "not a GGUF", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);

        var classification = ModelCatalogService.ClassifyGguf(path);
        Assert.Equal(GgufFileRole.Invalid, classification.Role);
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.ImportFileAsync(path, confirmRole: true));
        Assert.Empty(await store.ListModelsAsync());
    }

    [Fact]
    public async Task ScanDiagnosticsReportEveryGgufRole()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        WriteMinimalGguf(Path.Combine(models, "Main-MTP-Edition.gguf"), "qwen35");
        WriteMinimalGguf(Path.Combine(models, "mmproj-Main.gguf"), "clip");
        WriteMinimalGguf(Path.Combine(models, "Main-draft-EAGLE.gguf"), "qwen35");
        await File.WriteAllTextAsync(Path.Combine(models, "broken.gguf"), "broken", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();

        var result = await new ModelCatalogService(store).ScanDetailedAsync(models);

        Assert.Equal(4, result.DiscoveredCount);
        Assert.Equal(1, result.RegisteredCount);
        Assert.Equal(1, result.CompanionCount);
        Assert.Equal(1, result.AmbiguousCount);
        Assert.Equal(1, result.InvalidCount);
        Assert.Contains(result.Files, file => file.Role == GgufFileRole.MainModel);
        Assert.Contains(result.Files, file => file.Role == GgufFileRole.VisionProjector);
    }

    [Fact]
    public void ControlCliSupportsPersistentExplicitFileImport()
    {
        var request = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests(
            "models", "import", "--file", @"D:\Models\future-name.gguf", "--confirm-role");

        Assert.Equal("POST", request.Method);
        Assert.Equal("/api/v1/models/import", request.Path);
        Assert.Equal(@"D:\Models\future-name.gguf", request.Body?["file"]?.GetValue<string>());
        Assert.True(request.Body?["confirmRole"]?.GetValue<bool>());
        Assert.Throws<InvalidOperationException>(() => LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests(
            "models", "import", "--file", "one.gguf", "--folder", "folder"));
    }

    [Fact]
    public async Task ModelImportApplicationConfirmsAmbiguousFileAndRefreshesUi()
    {
        Loc.LoadLanguage("en");
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Qwen-MTP-head.gguf");
        WriteMinimalGguf(path, "qwen35");
        var confirmationShown = false;
        var confirmRolePassed = false;
        var profileEnsured = false;
        var refreshed = false;
        var status = "";
        var busyStatus = "";
        var model = new ModelRecord("qwen", "Qwen", path, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);

        var outcome = await new ModelImportApplicationService().ChooseAndImportAsync(
            root,
            new ModelImportApplicationActions(
                request =>
                {
                    Assert.Equal(".gguf", request.DefaultExt);
                    Assert.Equal(Loc.T("Models.Import.PickerTitle"), request.Title);
                    Assert.Equal(Loc.T("Models.Import.FileFilter"), request.Filter);
                    return path;
                },
                confirmation =>
                {
                    Assert.Equal(Loc.T("Models.Import.ConfirmationTitle"), confirmation.Title);
                    Assert.Contains(Loc.T("Models.Import.Role.Ambiguous"), confirmation.Message, StringComparison.Ordinal);
                    return confirmationShown = confirmation.Classification.Role == GgufFileRole.Ambiguous;
                },
                (_, confirmRole) =>
                {
                    confirmRolePassed = confirmRole;
                    return Task.FromResult(model);
                },
                _ =>
                {
                    profileEnsured = true;
                    return Task.CompletedTask;
                },
                async (message, action) =>
                {
                    busyStatus = message;
                    await action();
                },
                () =>
                {
                    refreshed = true;
                    return Task.CompletedTask;
                },
                value => status = value));

        Assert.Equal(ModelImportApplicationOutcome.Imported, outcome);
        Assert.True(confirmationShown);
        Assert.True(confirmRolePassed);
        Assert.True(profileEnsured);
        Assert.True(refreshed);
        Assert.Equal(Loc.T("Models.Import.Busy"), busyStatus);
        Assert.Equal(Loc.T("Models.Import.AddedStatus", model.Name), status);
    }
}
