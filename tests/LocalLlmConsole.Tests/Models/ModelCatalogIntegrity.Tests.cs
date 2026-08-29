using System.Globalization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class ModelCatalogIntegrityTests : ManagerRegressionTestBase
{
    [Fact]
    public void GgufClassificationRejectsMetadataTruncatedAfterValidArchitecture()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "truncated.gguf");
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            writer.Write(3u);
            writer.Write(0ul);
            writer.Write(2ul);
            WriteGgufString(writer, "general.architecture");
            writer.Write(8u);
            WriteGgufString(writer, "qwen3");
            writer.Write(128ul);
        }

        var classification = ModelCatalogService.ClassifyGguf(path);

        Assert.Equal(GgufFileRole.Invalid, classification.Role);
    }

    [Fact]
    public void GgufClassificationRejectsUnknownValueTypeAfterValidArchitecture()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "unknown-type.gguf");
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            writer.Write(3u);
            writer.Write(0ul);
            writer.Write(2ul);
            WriteGgufString(writer, "general.architecture");
            writer.Write(8u);
            WriteGgufString(writer, "qwen3");
            WriteGgufString(writer, "future.metadata");
            writer.Write(99u);
        }

        var classification = ModelCatalogService.ClassifyGguf(path);

        Assert.Equal(GgufFileRole.Invalid, classification.Role);
    }

    [Fact]
    public async Task ModelCapabilityCacheServiceCachesByModelCacheKey()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord(
            "qwen",
            "Qwen",
            Path.Combine(root, "qwen.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var inspected = ModelCapabilityService.Empty() with { Architecture = "qwen3" };
        var keyReads = 0;
        var inspections = 0;
        var service = new ModelCapabilityCacheService(
            _ =>
            {
                keyReads++;
                return "stable-key";
            },
            _ =>
            {
                inspections++;
                return inspected;
            });

        var first = await service.ReadAsync(model, TestContext.Current.CancellationToken);
        var second = await service.ReadAsync(model, TestContext.Current.CancellationToken);

        Assert.Equal(inspected, first);
        Assert.Equal(inspected, second);
        Assert.Equal(2, keyReads);
        Assert.Equal(1, inspections);
    }


    [Fact]
    public async Task DownloadRegistrationRejectsAppOwnedPathOutsideModelsRoot()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.RegisterDownloadedAsync(Path.Combine(root, "models"), "Model", Path.Combine(root, "outside", "model.gguf"), "{}"));
    }


    [Fact]
    public async Task ModelCatalogAddsGgufManifestToRegisteredModels()
    {
        var root = CreateTempRoot();
        var modelsRoot = Path.Combine(root, "models");
        Directory.CreateDirectory(modelsRoot);
        var modelPath = Path.Combine(modelsRoot, "Qwen3-Q4_K_M.gguf");
        WriteMinimalGguf(modelPath);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);

        var record = await catalog.RegisterDownloadedAsync(modelsRoot, "Qwen3", modelPath, """{"source":"test"}""");
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(record.MetadataJson)!;

        Assert.Equal("test", metadata["source"]?.ToString());
        Assert.Equal("true", metadata["ggufMetadataAvailable"]?.ToString().ToLowerInvariant());
        Assert.Equal("qwen3", metadata["ggufArchitecture"]?.ToString());
        Assert.Equal("Q4_K_M", metadata["ggufQuantization"]?.ToString());
        Assert.Equal("32768", metadata["ggufContextLength"]?.ToString());
        Assert.Equal("7000000000", metadata["ggufParameterCount"]?.ToString());
        Assert.Equal(new FileInfo(modelPath).Length.ToString(CultureInfo.InvariantCulture), metadata["ggufSizeBytes"]?.ToString());
        Assert.Equal("true", metadata["ggufHasChatTemplate"]?.ToString().ToLowerInvariant());
    }


    [Fact]
    public async Task DownloadRegistrationCollapsesExternalDuplicateForSameModelPath()
    {
        var root = CreateTempRoot();
        var modelsRoot = Path.Combine(root, "models");
        var modelPath = Path.Combine(modelsRoot, "repo-model", "Model-Q4_K_M.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        WriteMinimalGguf(modelPath);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);
        var external = new ModelRecord(
            "external-model",
            "External Model",
            modelPath,
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root) with { Port = 8099 });
        await store.UpsertModelAsync(external);
        await store.SaveModelLaunchSettingsAsync(external.Id, settings);

        var appOwned = await catalog.RegisterDownloadedAsync(modelsRoot, "Model-Q4_K_M.gguf", modelPath, """{"source":"download"}""");
        var models = await store.ListModelsAsync();

        Assert.Equal("Model Q4 K M", appOwned.Name);
        Assert.Single(models);
        Assert.Equal(appOwned.Id, models[0].Id);
        Assert.Equal("Model Q4 K M", models[0].Name);
        Assert.Equal(OwnershipKind.AppOwned, models[0].Ownership);
        Assert.Equal(Path.GetFullPath(modelPath), Path.GetFullPath(models[0].ModelPath));
        Assert.Null(await store.GetModelLaunchSettingsAsync(external.Id));
        Assert.Equal(8099, (await store.GetModelLaunchSettingsAsync(appOwned.Id))?.Port);
    }

    [Fact]
    public async Task DownloadRegistrationKeepsIdenticallyNamedFilesInDifferentManagedFoldersDistinct()
    {
        var root = CreateTempRoot();
        var modelsRoot = Path.Combine(root, "models");
        var firstPath = Path.Combine(modelsRoot, "repo-one", "model-q4.gguf");
        var secondPath = Path.Combine(modelsRoot, "repo-two", "model-q4.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondPath)!);
        WriteMinimalGguf(firstPath);
        WriteMinimalGguf(secondPath);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);

        var first = await catalog.RegisterDownloadedAsync(modelsRoot, "model-q4.gguf", firstPath, "{}");
        var second = await catalog.RegisterDownloadedAsync(modelsRoot, "model-q4.gguf", secondPath, "{}");
        var models = await store.ListModelsAsync();

        Assert.NotEqual(first.Id, second.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, models.Count(model => model.Ownership == OwnershipKind.AppOwned));
        Assert.Contains(models, model => Path.GetFullPath(model.ModelPath) == Path.GetFullPath(firstPath));
        Assert.Contains(models, model => Path.GetFullPath(model.ModelPath) == Path.GetFullPath(secondPath));
    }


    [Fact]
    public async Task ModelCatalogScanCollapsesExistingAppOwnedDuplicateForSameModelPath()
    {
        var root = CreateTempRoot();
        var modelsRoot = Path.Combine(root, "models");
        var modelPath = Path.Combine(modelsRoot, "repo-model", "Model-Q4_K_M.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        WriteMinimalGguf(modelPath);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);
        var appOwned = new ModelRecord(
            "app-owned-model",
            "App Model",
            modelPath,
            OwnershipKind.AppOwned,
            "{}",
            DateTimeOffset.UtcNow);
        var external = new ModelRecord(
            "external-model",
            "External Model",
            modelPath,
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root) with { Port = 8098 });
        await store.UpsertModelAsync(external);
        await store.SaveModelLaunchSettingsAsync(external.Id, settings);
        await store.UpsertModelAsync(appOwned);

        await catalog.ScanAsync(modelsRoot);
        var models = await store.ListModelsAsync();

        Assert.Single(models);
        Assert.Equal(appOwned.Id, models[0].Id);
        Assert.Equal(OwnershipKind.AppOwned, models[0].Ownership);
        Assert.Null(await store.GetModelLaunchSettingsAsync(external.Id));
        Assert.Equal(8098, (await store.GetModelLaunchSettingsAsync(appOwned.Id))?.Port);
    }


    [Fact]
    public async Task ModelCatalogCleanupCollapsesDuplicateRecordsWithoutFilesystemScan()
    {
        var root = CreateTempRoot();
        var modelsRoot = Path.Combine(root, "models");
        var modelPath = Path.Combine(modelsRoot, "repo-model", "Model-Q4_K_M.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        WriteMinimalGguf(modelPath);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);
        var appOwned = new ModelRecord("app-owned-model", "App Model", modelPath, OwnershipKind.AppOwned, "{}", DateTimeOffset.UtcNow);
        var external = new ModelRecord("external-model", "External Model", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(external);
        await store.UpsertModelAsync(appOwned);

        var removed = await catalog.CleanupDuplicateModelRecordsAsync();
        var models = await store.ListModelsAsync();

        Assert.Equal(1, removed);
        Assert.Single(models);
        Assert.Equal(appOwned.Id, models[0].Id);
    }


    [Fact]
    public async Task ModelCatalogPreservesRegistryOnlyLaunchAliasesDuringDeduplication()
    {
        var root = CreateTempRoot();
        var modelsRoot = Path.Combine(root, "models");
        var modelPath = Path.Combine(modelsRoot, "repo-model", "Model-Q4_K_M.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        WriteMinimalGguf(modelPath);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);
        var appOwned = new ModelRecord("app-owned-model", "App Model", modelPath, OwnershipKind.AppOwned, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(appOwned);
        var alias = new ModelRecord(
            "legacy-alias",
            "App Model 32K",
            appOwned.ModelPath,
            OwnershipKind.RegistryOnly,
            ModelAliasService.CreateMetadata(appOwned, [appOwned]),
            DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(alias);
        await store.SaveModelLaunchSettingsAsync(alias.Id, ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root) with { Port = 8097 }));

        await catalog.ScanAsync(modelsRoot);
        var removed = await catalog.CleanupDuplicateModelRecordsAsync();
        var models = await store.ListModelsAsync();

        Assert.Equal(0, removed);
        Assert.Contains(models, model => model.Id == appOwned.Id && model.Ownership == OwnershipKind.AppOwned);
        var savedAlias = Assert.Single(models, ModelAliasService.IsLaunchAlias);
        Assert.Equal(alias.Id, savedAlias.Id);
        Assert.Equal("App Model 32K", savedAlias.Name);
        Assert.Equal(8097, (await store.GetModelLaunchSettingsAsync(alias.Id))?.Port);
    }


    [Fact]
    public async Task ModelCatalogCleanupRemovesGgufExtensionFromDisplayNames()
    {
        var root = CreateTempRoot();
        var modelsRoot = Path.Combine(root, "models");
        var modelPath = Path.Combine(modelsRoot, "repo-model", "Qwen3.5-9B-Q4_K_M.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        WriteMinimalGguf(modelPath);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);
        await store.UpsertModelAsync(new ModelRecord(
            "app-owned-model",
            "Qwen3.5-9B-Q4_K_M.gguf",
            modelPath,
            OwnershipKind.AppOwned,
            "{}",
            DateTimeOffset.UtcNow));

        var changed = await catalog.CleanupModelRecordsAsync();
        var model = Assert.Single(await store.ListModelsAsync());

        Assert.Equal(1, changed);
        Assert.Equal("Qwen3.5 9B Q4 K M", model.Name);
        Assert.DoesNotContain(".gguf", model.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ModelCatalogRefreshRetainsMissingModelAndItsLaunchProfileWithVisibleStatus()
    {
        var root = CreateTempRoot();
        var missingPath = Path.Combine(root, "models", "removed-model.gguf");
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var model = new ModelRecord(
            "missing-model",
            "Removed Model",
            missingPath,
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile(
            "profile:missing-model",
            model.Id,
            "Coding",
            ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root)),
            DateTimeOffset.UtcNow,
            IsDefault: true);
        await store.UpsertModelAsync(model);
        await store.SaveNamedModelLaunchProfileAsync(profile);
        var refresh = new ModelCatalogRefreshApplicationService(store, new ModelCatalogService(store));

        var result = await refresh.RefreshAsync(
            new ModelCatalogRefreshApplicationActions(_ => Task.FromResult<IReadOnlyList<NamedModelLaunchProfile>>([profile])),
            TestContext.Current.CancellationToken);

        Assert.Equal(model.Id, Assert.Single(result.Models).Id);
        Assert.Equal(profile.Id, Assert.Single(result.NamedLaunchProfiles).Id);
        Assert.Equal("Missing", result.ModelSizeLabels[model.Id]);
        Assert.Equal(profile.Id, Assert.Single(await store.ListNamedModelLaunchProfilesAsync(model.Id)).Id);
    }

    [Fact]
    public async Task ModelCatalogRefreshApplicationServiceCleansAndCollectsLaunchProfiles()
    {
        var root = CreateTempRoot();
        var modelsRoot = Path.Combine(root, "models");
        var modelPath = Path.Combine(modelsRoot, "repo-model", "Qwen3.5-9B-Q4_K_M.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        WriteMinimalGguf(modelPath);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);
        var refresh = new ModelCatalogRefreshApplicationService(store, catalog);
        var appOwned = new ModelRecord("app-owned-model", "Qwen3.5-9B-Q4_K_M.gguf", modelPath, OwnershipKind.AppOwned, "{}", DateTimeOffset.UtcNow);
        var external = new ModelRecord("external-model", "External Model", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root) with { Port = 8096 });
        var readIds = new List<string>();
        await store.UpsertModelAsync(external);
        await store.UpsertModelAsync(appOwned);

        var result = await refresh.RefreshAsync(new ModelCatalogRefreshApplicationActions(async models =>
        {
            var created = new List<NamedModelLaunchProfile>();
            foreach (var model in models)
            {
                readIds.Add(model.Id);
                var named = new NamedModelLaunchProfile(
                    $"default:{model.Id}",
                    model.Id,
                    "Default",
                    profile,
                    DateTimeOffset.UtcNow,
                    IsDefault: true);
                await store.SaveNamedModelLaunchProfileAsync(named);
                created.Add(named);
            }
            return created;
        }), TestContext.Current.CancellationToken);

        var model = Assert.Single(result.Models);
        Assert.Equal(appOwned.Id, model.Id);
        Assert.Equal(["app-owned-model"], readIds);
        Assert.Equal(8096, result.LaunchProfileFor(model)?.Port);
        Assert.Equal(DisplayFormatService.Bytes(new FileInfo(modelPath).Length), result.ModelSizeLabels[model.Id]);
    }


}
