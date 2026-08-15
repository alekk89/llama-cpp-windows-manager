using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void HuggingFaceRepoMetadataCacheIsBounded()
    {
        var service = ReadServicePartialSources("HuggingFaceService");

        Assert.Contains("private const int RepoInfoCacheLimit", service, StringComparison.Ordinal);
        Assert.Contains("RepoInfoCacheTtl", service, StringComparison.Ordinal);
        Assert.Contains("CachedRepoInfo", service, StringComparison.Ordinal);
        Assert.Contains("TrimRepoInfoCache(now)", service, StringComparison.Ordinal);
        Assert.Contains("_repoInfoCache.TryRemove", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentDictionary<string, RepoInfo> _repoInfoCache", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelCatalogFindsAdjacentDraftModel()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var main = Path.Combine(models, "Qwen3-main.gguf");
        var draft = Path.Combine(models, "Qwen3-MTP-draft.gguf");
        var projector = Path.Combine(models, "mmproj-model.gguf");
        File.WriteAllText(main, "main");
        File.WriteAllText(draft, "draft");
        File.WriteAllText(projector, "projector");

        var found = ModelCatalogService.FindDraftModel(main);

        Assert.Equal(Path.GetFullPath(draft), Path.GetFullPath(found!));
    }

    [Fact]
    public void ModelCatalogFindsAdjacentDFlashHead()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var main = Path.Combine(models, "Qwen3.6-27B-Q4_K_M.gguf");
        var dflash = Path.Combine(models, "Qwen3.6-27B-DFlash-b16-Q4_K_M.gguf");
        File.WriteAllText(main, "main");
        File.WriteAllText(dflash, "dflash");

        var found = ModelCatalogService.FindDraftModel(main);

        Assert.Equal(Path.GetFullPath(dflash), Path.GetFullPath(found!));
    }

    [Fact]
    public void ModelCatalogFindsAndPrefersAdjacentDSparkHead()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var main = Path.Combine(models, "Qwen3-8B-Q4_K_M.gguf");
        var dflash = Path.Combine(models, "Qwen3-8B-DFlash-b16.gguf");
        var dspark = Path.Combine(models, "Qwen3-8B-DSpark.gguf");
        File.WriteAllText(main, "main");
        File.WriteAllText(dflash, "dflash");
        File.WriteAllText(dspark, "dspark");

        var found = ModelCatalogService.FindDraftModel(main);

        Assert.Equal(Path.GetFullPath(dspark), Path.GetFullPath(found!));
    }

    [Fact]
    public void DraftMtpUsesEmbeddedNextNAndRejectsDifferentModelHelpers()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        var modelFolder = Path.Combine(models, "qwen3.8-27b");
        Directory.CreateDirectory(modelFolder);
        var main = Path.Combine(modelFolder, "Qwen3.8-27B-Q8_0.gguf");
        using (var stream = File.Create(main))
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            writer.Write((uint)3);
            writer.Write((ulong)0);
            writer.Write((ulong)2);
            WriteGgufString(writer, "general.architecture");
            writer.Write((uint)8);
            WriteGgufString(writer, "qwen35");
            WriteGgufString(writer, "qwen35.nextn_predict_layers");
            writer.Write((uint)4);
            writer.Write((uint)1);
        }

        var unrelated = Path.Combine(models, "Qwen3.6-27B-DSpark.gguf");
        var compatible = Path.Combine(modelFolder, "Qwen3.8-27B-DFlash.gguf");
        File.WriteAllText(unrelated, "unrelated");
        File.WriteAllText(compatible, "compatible");

        Assert.True(ModelCatalogService.HasEmbeddedDraftMtp(main));
        Assert.DoesNotContain(unrelated, ModelCatalogService.FindDraftModels(main), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(compatible, ModelCatalogService.FindDraftModels(main), StringComparer.OrdinalIgnoreCase);
        Assert.Null(ModelCatalogService.ResolveDraftModelPath(main, "", "draft-mtp"));
        Assert.Equal(compatible, ModelCatalogService.ResolveDraftModelPath(main, compatible, "draft-mtp"));
        Assert.Equal(compatible, ModelCatalogService.ResolveDraftModelPath(main, "", "draft-dflash"));
    }

    [Fact]
    public void CompanionDiscoveryStaysInModelFolderAndMatchesRequestedSpeculativeType()
    {
        var root = CreateTempRoot();
        var modelFolder = Path.Combine(root, "Qwen3.8-27B");
        var childFolder = Path.Combine(modelFolder, "old");
        Directory.CreateDirectory(childFolder);
        var main = Path.Combine(modelFolder, "Qwen3.8-27B-Q4_K_M.gguf");
        var mtp = Path.Combine(modelFolder, "mtp-Qwen3.8-27B-Q8_0.gguf");
        var dflash = Path.Combine(modelFolder, "Qwen3.8-27B-DFlash.gguf");
        var dspark = Path.Combine(modelFolder, "Qwen3.8-27B-DSpark.gguf");
        var eagle = Path.Combine(modelFolder, "Qwen3.8-27B-EAGLE3.gguf");
        var parentMtp = Path.Combine(root, "mtp-Qwen3.8-27B-parent.gguf");
        var childProjector = Path.Combine(childFolder, "mmproj-Qwen3.8-27B-f16.gguf");
        var localProjector = Path.Combine(modelFolder, "mmproj-Qwen3.8-27B-f16.gguf");
        foreach (var path in new[] { main, mtp, dflash, dspark, eagle, parentMtp, childProjector, localProjector })
            File.WriteAllText(path, path);

        Assert.Equal(mtp, ModelCatalogService.ResolveDraftModelPath(main, "", "draft-mtp"));
        Assert.Equal(dflash, ModelCatalogService.ResolveDraftModelPath(main, "", "draft-dflash"));
        Assert.Equal(dspark, ModelCatalogService.ResolveDraftModelPath(main, "", "draft-dspark"));
        Assert.Equal(eagle, ModelCatalogService.ResolveDraftModelPath(main, "", "draft-eagle3"));
        Assert.Equal(mtp, ModelCatalogService.ResolveMtpHeadPath(main, "", "atomic-mtp"));
        Assert.Equal(localProjector, ModelCatalogService.FindVisionProjector(main));
        Assert.DoesNotContain(parentMtp, ModelCatalogService.FindDraftModels(main), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(childProjector, ModelCatalogService.FindVisionProjectors(main), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompanionDiscoveryUsesGgufArchitectureMetadataAndAllowsSmallerSimpleDrafts()
    {
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);
        var gemma = Path.Combine(root, "Gemma-4-12B-it-Q4_K_M.gguf");
        var gemmaAssistant = Path.Combine(root, "Gemma-4-12B-it-assistant-Q8_0.gguf");
        var mistral = Path.Combine(root, "Mistral-Small-3.1-24B-Q4_K_M.gguf");
        var mistralDraft = Path.Combine(root, "draft-Mistral-Small-3.1-7B-Q8_0.gguf");
        File.WriteAllText(gemma, "main");
        File.WriteAllText(mistral, "main");
        File.WriteAllText(mistralDraft, "draft");
        WriteMinimalGguf(gemmaAssistant, "gemma4-assistant", ("gemma4.nextn_predict_layers", 4u));

        Assert.Equal(gemmaAssistant, ModelCatalogService.ResolveDraftModelPath(gemma, "", "draft-mtp"));
        Assert.Equal(mistralDraft, ModelCatalogService.ResolveDraftModelPath(mistral, "", "draft-simple"));
        Assert.Null(ModelCatalogService.ResolveDraftModelPath(mistral, "", "draft-mtp"));
    }

    [Fact]
    public async Task ModelScanKeepsEmbeddedMtpMainAndSkipsStandaloneAssistantArchitecture()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var qwenMain = Path.Combine(models, "Qwen3.8-27B-Q4_K_M.gguf");
        var gemmaAssistant = Path.Combine(models, "Gemma-4-12B-it-assistant-Q8_0.gguf");
        WriteMinimalGguf(qwenMain, "qwen35", ("qwen35.nextn_predict_layers", 1u));
        WriteMinimalGguf(gemmaAssistant, "gemma4-assistant", ("gemma4.nextn_predict_layers", 4u));
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);

        var registered = await catalog.ScanAsync(models);
        var saved = await store.ListModelsAsync();

        Assert.Equal(1, registered);
        Assert.Equal(qwenMain, Assert.Single(saved).ModelPath);
    }

    [Fact]
    public async Task ModelCatalogTreatsVisionHeadCompanionsAsProjectorsNotMainModels()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var main = Path.Combine(models, "Gemma-main.gguf");
        var visionHead = Path.Combine(models, "Gemma-mtp-vision-f16.gguf");
        var draft = Path.Combine(models, "Gemma-MTP-draft.gguf");
        await File.WriteAllTextAsync(main, "main", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(visionHead, "vision", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(draft, "draft", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);

        var foundProjector = ModelCatalogService.FindVisionProjector(main);
        var registered = await catalog.ScanAsync(models);
        var savedModels = await store.ListModelsAsync();

        Assert.Equal(Path.GetFullPath(visionHead), Path.GetFullPath(foundProjector!));
        Assert.Equal(1, registered);
        Assert.DoesNotContain(savedModels, model => string.Equals(model.ModelPath, visionHead, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(savedModels, model => string.Equals(model.ModelPath, draft, StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task DownloadRecoveryRejectsDestinationOutsideModelsRoot()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var catalog = new ModelCatalogService(store);
        var huggingFace = new HuggingFaceService(store, jobs, catalog);
        var settings = AppSettings.CreateDefault(root);
        var outside = Path.Combine(root, "outside", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        await File.WriteAllTextAsync(outside, "external file must not become app-owned", TestContext.Current.CancellationToken);

        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", 0, 0);
        var payload = new DownloadJobPayload(file, outside);
        var now = DateTimeOffset.UtcNow;
        await store.UpsertJobAsync(new JobRecord(
            "huggingface-download-test",
            "huggingface-download",
            JobStatus.Running,
            System.Text.Json.JsonSerializer.Serialize(payload),
            Path.Combine(root, "logs", "job.log"),
            now,
            now));

        await huggingFace.RecoverInterruptedDownloadsAsync(settings);

        var job = Assert.Single(await store.ListJobsAsync());
        Assert.Equal(JobStatus.Failed, job.Status);
        var recoveredPayload = HuggingFaceService.ParseDownloadPayload(job.PayloadJson);
        Assert.NotNull(recoveredPayload);
        Assert.Contains("outside", recoveredPayload.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(outside));
        Assert.Empty(await store.ListModelsAsync());
    }


    [Fact]
    public async Task DownloadRecoveryRejectsUnsafeWindowsFilenames()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var catalog = new ModelCatalogService(store);
        var huggingFace = new HuggingFaceService(store, jobs, catalog);
        var settings = AppSettings.CreateDefault(root);

        var file = new HuggingFaceFile("owner/repo", "bad:name.gguf", "bad:name.gguf", "", 1, 0);
        var payload = new DownloadJobPayload(file, Path.Combine(settings.ModelsRoot, "repo-bad", "bad:name.gguf"));
        var now = DateTimeOffset.UtcNow;
        await store.UpsertJobAsync(new JobRecord(
            "huggingface-download-unsafe-name",
            "huggingface-download",
            JobStatus.Running,
            System.Text.Json.JsonSerializer.Serialize(payload),
            Path.Combine(root, "logs", "job.log"),
            now,
            now));

        await huggingFace.RecoverInterruptedDownloadsAsync(settings);

        var job = Assert.Single(await store.ListJobsAsync());
        Assert.Equal(JobStatus.Failed, job.Status);
        var recoveredPayload = HuggingFaceService.ParseDownloadPayload(job.PayloadJson);
        Assert.NotNull(recoveredPayload);
        Assert.Contains("filename", recoveredPayload.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await store.ListModelsAsync());
    }


    [Fact]
    public async Task HuggingFaceDownloadRejectsUnsafeVisionProjectorMetadata()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var catalog = new ModelCatalogService(store);
        var huggingFace = new HuggingFaceService(store, jobs, catalog);
        var settings = AppSettings.CreateDefault(root);
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", 1, 0)
        {
            HasVisionProjector = true,
            VisionProjectorPath = "projector.txt",
            VisionProjectorName = "projector.txt"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => huggingFace.StartDownloadAsync(file, settings, TestContext.Current.CancellationToken));

        Assert.Contains("vision projector", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await store.ListJobsAsync());
    }


    [Fact]
    public async Task DownloadRecoveryRejectsCompletedFileWithoutSizeOrChecksum()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var catalog = new ModelCatalogService(store);
        var huggingFace = new HuggingFaceService(store, jobs, catalog);
        var settings = AppSettings.CreateDefault(root);
        var destination = Path.Combine(settings.ModelsRoot, "repo-model", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, "untrusted complete file", TestContext.Current.CancellationToken);

        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", 0, 0);
        var payload = new DownloadJobPayload(file, destination);
        var now = DateTimeOffset.UtcNow;
        await store.UpsertJobAsync(new JobRecord(
            "huggingface-download-no-integrity",
            "huggingface-download",
            JobStatus.Running,
            System.Text.Json.JsonSerializer.Serialize(payload),
            Path.Combine(root, "logs", "job.log"),
            now,
            now));

        await huggingFace.RecoverInterruptedDownloadsAsync(settings);

        var job = Assert.Single(await store.ListJobsAsync());
        Assert.Equal(JobStatus.Failed, job.Status);
        var recoveredPayload = HuggingFaceService.ParseDownloadPayload(job.PayloadJson);
        Assert.NotNull(recoveredPayload);
        Assert.Contains("expected size", recoveredPayload.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(destination));
        Assert.Empty(await store.ListModelsAsync());
    }


    [Fact]
    public void HuggingFaceInstallStateDetectsInstalledFilesFromMetadataAndPaths()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var file = new HuggingFaceFile("owner/repo", "folder/model.gguf", "model.gguf", "Q4", 2048, 0);
        var expected = HuggingFaceInstallStateService.ExpectedDestination(file, settings.ModelsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        File.WriteAllText(expected, "installed");
        var now = DateTimeOffset.UtcNow;
        var byMetadata = new ModelRecord(
            "model-1",
            "Model",
            Path.Combine(settings.ModelsRoot, "other", "other.gguf"),
            OwnershipKind.AppOwned,
            """{"file":{"repo":"owner/repo","path":"folder/model.gguf"}}""",
            now);
        var byFileName = byMetadata with
        {
            Id = "model-2",
            ModelPath = Path.Combine(settings.ModelsRoot, "another", "model.gguf"),
            MetadataJson = "{}"
        };

        var metadataInventory = HuggingFaceInstallStateService.BuildInventory([byMetadata]);
        var fileNameInventory = HuggingFaceInstallStateService.BuildInventory([byFileName]);
        var emptyInventory = HuggingFaceInstallStateService.BuildInventory([]);

        Assert.Contains("owner/repo|folder/model.gguf", metadataInventory.Keys);
        Assert.True(HuggingFaceInstallStateService.IsInstalled(file, metadataInventory, settings.ModelsRoot));
        Assert.True(HuggingFaceInstallStateService.IsInstalled(file, fileNameInventory, settings.ModelsRoot));
        Assert.True(HuggingFaceInstallStateService.IsInstalled(file, emptyInventory, settings.ModelsRoot));
        Assert.Equal("50% (1 KB)", HuggingFaceInstallStateService.FormatDownloadProgress(new DownloadJobPayload(file, expected, 1024, 2048)));
        Assert.Equal("Retry", HuggingFaceInstallStateService.DownloadStartLabel(JobStatus.Failed));
        Assert.True(HuggingFaceInstallStateService.CanStartDownload(JobStatus.Cancelled));
        Assert.True(HuggingFaceInstallStateService.CanPauseDownload(JobStatus.Running));
        Assert.True(HuggingFaceInstallStateService.CanStopDownload(JobStatus.Paused));

        var pairedFile = file with
        {
            HasVisionProjector = true,
            VisionProjectorPath = "mmproj/model-mmproj.gguf",
            VisionProjectorName = "model-mmproj.gguf",
            VisionProjectorSizeBytes = 4096,
            VisionProjectorSha256 = new string('a', 64)
        };
        var pairedPayload = HuggingFaceService.ParseDownloadPayload(System.Text.Json.JsonSerializer.Serialize(new DownloadJobPayload(pairedFile, expected)));

        Assert.NotNull(pairedPayload);
        Assert.Equal("mmproj/model-mmproj.gguf", pairedPayload.File.VisionProjectorPath);
        Assert.Equal(4096, pairedPayload.File.VisionProjectorSizeBytes);
    }


    [Fact]
    public void CompletedDownloadsRegisterAndRefreshBeforeOptionalEnrichmentFinishes()
    {
        var serviceSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "HuggingFace", "HuggingFaceService.Safety.cs"));
        var downloadHistorySource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.DownloadHistory.cs"));
        var downloadHistoryWorkflow = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "HuggingFace", "DownloadHistoryWorkflowService.cs"));
        var registerIndex = serviceSource.IndexOf("await RegisterDownloadedHuggingFaceModelAsync(settings, file, destination, timestamp, recovered, new VisionProjectorDownloadResult(\"\", \"\"));", StringComparison.Ordinal);
        var completedIndex = serviceSource.IndexOf("await _jobs.UpdateAsync(job, JobStatus.Completed, JsonSerializer.Serialize(new DownloadJobPayload(file, destination, completedBytes, completedBytes), JsonOptions), cancellationToken);", StringComparison.Ordinal);
        var projectorIndex = serviceSource.IndexOf("var projector = await TryDownloadVisionProjectorAsync(settings, file, destination, cancellationToken);", StringComparison.Ordinal);

        Assert.Contains("CompleteVerifiedPrimaryModelAsync", serviceSource, StringComparison.Ordinal);
        Assert.True(registerIndex >= 0);
        Assert.True(completedIndex > registerIndex);
        Assert.True(projectorIndex > completedIndex);
        Assert.Contains("Optional post-download setup skipped", serviceSource, StringComparison.Ordinal);
        Assert.Contains("var downloadHistory = AppServices.DownloadHistoryApplication;", downloadHistorySource, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.DownloadCompletionApplication.MonitorAsync(", downloadHistorySource, StringComparison.Ordinal);
        Assert.Contains("new DownloadCompletionApplicationActions(", downloadHistorySource, StringComparison.Ordinal);
        Assert.Contains("downloadHistory.WaitUntilInactiveOrTerminalAsync(completedJobId, interval)", downloadHistorySource, StringComparison.Ordinal);
        Assert.Contains("JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Paused or JobStatus.Interrupted", downloadHistoryWorkflow, StringComparison.Ordinal);
        Assert.Contains("public async Task WaitUntilInactiveOrTerminalAsync(", downloadHistoryWorkflow, StringComparison.Ordinal);
        Assert.Contains("RunDownloadCompletionOnUiThreadAsync", downloadHistorySource, StringComparison.Ordinal);
        Assert.Contains("var catalog = ModelServices.Catalog;", downloadHistorySource, StringComparison.Ordinal);
        Assert.Contains("await catalog.ScanAsync(_settings.ModelsRoot);", downloadHistorySource, StringComparison.Ordinal);
    }


    [Theory]
    [InlineData("owner/repo", "owner/repo", "", "")]
    [InlineData("owner/repo/folder/model-q4.gguf", "owner/repo", "folder/model-q4.gguf", "")]
    [InlineData("https://huggingface.co/owner/repo", "owner/repo", "", "")]
    [InlineData("https://huggingface.co/owner/repo/blob/main/folder/model%20q4.gguf", "owner/repo", "folder/model q4.gguf", "main")]
    [InlineData("https://hf.co/owner/repo/resolve/main/model.gguf?download=true", "owner/repo", "model.gguf", "main")]
    public void HuggingFaceSearchParsesDirectRepoAndFileReferences(string input, string repo, string path, string revision)
    {
        Assert.True(HuggingFaceService.TryParseModelReference(input, out var reference));
        Assert.Equal(repo, reference.Repo);
        Assert.Equal(path, reference.Path);
        Assert.Equal(revision, reference.Revision);
    }


    [Theory]
    [InlineData("")]
    [InlineData("plain search text")]
    [InlineData("https://example.com/owner/repo")]
    [InlineData("owner/repo/config.json")]
    [InlineData("owner/../repo/model.gguf")]
    public void HuggingFaceSearchRejectsNonDirectReferences(string input)
    {
        Assert.False(HuggingFaceService.TryParseModelReference(input, out _));
    }


    [Theory]
    [InlineData("owner/repo", "https://huggingface.co/owner/repo")]
    [InlineData(" owner.name/repo_name ", "https://huggingface.co/owner.name/repo_name")]
    public void HuggingFaceModelCardUrlsRequireSafeRepoIds(string repo, string expected)
    {
        Assert.True(HuggingFaceService.TryCreateModelCardUrl(repo, out var url));
        Assert.Equal(expected, url);

        Assert.False(HuggingFaceService.TryCreateModelCardUrl("owner/../repo", out _));
        Assert.False(HuggingFaceService.TryCreateModelCardUrl("https://example.com/owner/repo", out _));
    }

    [Fact]
    public void HuggingFaceModelCardApplicationServiceOwnsRowParsingOpenAndStatus()
    {
        var service = new HuggingFaceModelCardApplicationService();
        var calls = new List<string>();
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "Q4", 1024, 1);
        var row = new UiRow
        {
            C1 = "fallback/repo",
            Data = System.Text.Json.JsonSerializer.SerializeToNode(file)!.AsObject()
        };
        var fallbackRow = new UiRow { C1 = "fallback/repo" };

        HuggingFaceModelCardApplicationActions Actions()
            => new(
                url => calls.Add($"open:{url}"),
                status => calls.Add($"status:{status}"));

        var opened = service.OpenFromRow(row, Actions());
        var fallback = service.OpenFromRow(fallbackRow, Actions());
        var blocked = service.Open("https://example.com/owner/repo", Actions());

        Assert.Equal("owner/repo", HuggingFaceModelCardApplicationService.RepoFromSearchRow(row));
        Assert.Equal("fallback/repo", HuggingFaceModelCardApplicationService.RepoFromSearchRow(fallbackRow));
        Assert.Equal(HuggingFaceModelCardApplicationOutcome.Opened, opened);
        Assert.Equal(HuggingFaceModelCardApplicationOutcome.Opened, fallback);
        Assert.Equal(HuggingFaceModelCardApplicationOutcome.Blocked, blocked);
        Assert.Contains("open:https://huggingface.co/owner/repo", calls);
        Assert.Contains("open:https://huggingface.co/fallback/repo", calls);
        Assert.Contains("status:Opened Hugging Face model card: owner/repo", calls);
        Assert.Contains("status:The selected row does not contain a valid Hugging Face repository.", calls);
    }


    [Fact]
    public void ModelCapabilityServiceInfersCapabilitiesFromModelMetadata()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "Qwen3-VL-Q4_K_M.gguf");
        var model = new ModelRecord(
            "qwen3-vl",
            "Qwen3 VL Reasoning MoE Embed FIM",
            modelPath,
            OwnershipKind.External,
            """{"tags":["image-text-to-text","reasoning","feature-extraction","fim","moe"],"HasVisionProjector":true}""",
            DateTimeOffset.UtcNow);

        var capabilities = ModelCapabilityService.Inspect(model);
        var summary = ModelCapabilityService.SummaryText(capabilities);
        var context = ModelCapabilityService.ContextLength(
            new Dictionary<string, object?> { ["qwen3.context_length"] = "32768" },
            "qwen3");
        var selectedCapabilities = new SelectedModelCapabilityController();
        var noModelState = selectedCapabilities.Apply(null, ModelCapabilityService.Empty());
        var selectedState = selectedCapabilities.Apply(model, capabilities);

        Assert.Equal("Q4_K_M", capabilities.Quantization);
        Assert.True(capabilities.HasVisionProjector);
        Assert.True(capabilities.LikelyVision);
        Assert.True(capabilities.LikelyReasoning);
        Assert.True(capabilities.IsEmbedding);
        Assert.True(capabilities.IsFim);
        Assert.True(capabilities.IsMoe);
        Assert.False(capabilities.HasMetadata);
        Assert.Equal(32768, context);
        Assert.Contains("Vision: mmproj found", summary, StringComparison.Ordinal);
        Assert.Contains("GGUF metadata: unavailable", summary, StringComparison.Ordinal);
        Assert.True(ModelCapabilityService.LooksVisionCapable("llama-3.2-vision"));
        Assert.Equal(SelectedModelCapabilityController.NoModelText, noModelState.DisplayText);
        Assert.False(noModelState.VisionLaunchSettingsAvailable);
        Assert.Same(capabilities, selectedState.Capabilities);
        Assert.Equal(summary, selectedState.DisplayText);
        Assert.True(selectedState.VisionLaunchSettingsAvailable);
    }


    [Fact]
    public void ModelCapabilityCacheKeyTracksVisionProjectorPairing()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "Qwen3-VL-Q4_K_M.gguf");
        File.WriteAllText(modelPath, "fake model");
        var model = new ModelRecord(
            "qwen3-vl",
            "Qwen3 VL",
            modelPath,
            OwnershipKind.External,
            """{"CapabilityHints":"vision"}""",
            DateTimeOffset.UtcNow);

        var beforeKey = ModelCapabilityService.CacheKey(model);
        var before = ModelCapabilityService.Inspect(model);
        Assert.True(before.LikelyVision);
        Assert.False(before.HasVisionProjector);
        Assert.Contains("projector not found", ModelCapabilityService.SummaryText(before), StringComparison.OrdinalIgnoreCase);

        File.WriteAllText(Path.Combine(root, "mmproj-qwen3-vl.gguf"), "projector");
        var afterKey = ModelCapabilityService.CacheKey(model);
        var after = ModelCapabilityService.Inspect(model);

        Assert.NotEqual(beforeKey, afterKey);
        Assert.True(after.HasVisionProjector);
        Assert.Contains("mmproj found", ModelCapabilityService.SummaryText(after), StringComparison.OrdinalIgnoreCase);
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


    [Fact]
    public void HuggingFaceSuggestedLaunchSettingsPreferLlamaServerCommand()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        const string readme = """
        ## Start the server

        ```bash
        llama-server -m Qwen3.6-27B-Q8_0-mtp.gguf \
          --spec-type draft-mtp --spec-draft-n-max 3 \
          --cache-type-k q8_0 --cache-type-v q8_0 \
          -np 1 -c 262144 --temp 0.7 --top-k 20 -ngl 99 --port 8081
        ```

        ## Direct CLI usage

        ```bash
        llama-cli -m Qwen3.6-27B-Q8_0-mtp.gguf -c 4096 -n 2048 --temp 0.2
        ```
        """;

        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(defaults, readme, """{"temperature":0.3,"top_p":0.8}""");

        Assert.NotNull(settings);
        Assert.Equal("draft-mtp", settings.SpeculativeType);
        Assert.Equal(3, settings.SpecDraftMaxTokens);
        Assert.Equal("q8_0", settings.CacheTypeK);
        Assert.Equal("q8_0", settings.CacheTypeV);
        Assert.Equal(1, settings.ParallelSlots);
        Assert.Equal(262_144, settings.ContextSize);
        Assert.Equal(0.7, settings.Temperature);
        Assert.Equal(20, settings.TopK);
        Assert.Equal(0.8, settings.TopP);
        Assert.Equal(99, settings.GpuLayers);
        Assert.Equal(-1, settings.MaxTokens);
    }


    [Fact]
    public void HuggingFaceSuggestedLaunchSettingsParseInlineEqualsQuotedPathsAndDraftOptions()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        const string readme = """
        ```bash
        llama-server --ctx-size=32768 --top-p=0.92 --min-p=0.05 \
          --repeat-penalty=1.08 --presence-penalty=-0.2 --frequency-penalty=0.1 \
          --image-min-tokens=256 --image-max-tokens=1024 \
          --flash-attn=on --rope-scaling=yarn --rope-scale=2 --rope-freq-base=1000000 --rope-freq-scale=0.5 \
          --spec-type=draft-simple --model-draft "D:\models\draft model.gguf" --spec-draft-ngl=10 \
          --spec-draft-n-min=1 --draft-p-split=0.45 --draft-p-min=0.12 \
          --cache-type-k-draft=q4_0 --cache-type-v-draft=q5_1
        ```
        """;

        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(defaults, readme);

        Assert.NotNull(settings);
        Assert.Equal(32_768, settings.ContextSize);
        Assert.Equal(0.92, settings.TopP);
        Assert.Equal(0.05, settings.MinP);
        Assert.Equal(1.08, settings.RepeatPenalty);
        Assert.Equal(-0.2, settings.PresencePenalty);
        Assert.Equal(0.1, settings.FrequencyPenalty);
        Assert.Equal(256, settings.VisionImageMinTokens);
        Assert.Equal(1024, settings.VisionImageMaxTokens);
        Assert.Equal("on", settings.FlashAttention);
        Assert.Equal("yarn", settings.RopeScaling);
        Assert.Equal(2, settings.RopeScale);
        Assert.Equal(1_000_000, settings.RopeFreqBase);
        Assert.Equal(0.5, settings.RopeFreqScale);
        Assert.Equal("draft-simple", settings.SpeculativeType);
        Assert.Equal(@"D:\models\draft model.gguf", settings.SpecDraftModelPath);
        Assert.Equal(10, settings.SpecDraftGpuLayers);
        Assert.Equal(1, settings.SpecDraftMinTokens);
        Assert.Equal(0.45, settings.SpecDraftPSplit);
        Assert.Equal(0.12, settings.SpecDraftPMin);
        Assert.Equal("q4_0", settings.SpecDraftCacheTypeK);
        Assert.Equal("q5_1", settings.SpecDraftCacheTypeV);
    }

    [Fact]
    public void HuggingFaceSuggestedLaunchSettingsParseMtpHeadOptions()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        const string readme = """
        ```bash
        llama-server -m Gemma4-31B-Q8_0.gguf --spec-type mtp --mtp-head "D:\models\mtp-gemma-4-31B-it.gguf"
        ```
        """;

        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(defaults, readme);

        Assert.NotNull(settings);
        Assert.Equal("atomic-mtp", settings.SpeculativeType);
        Assert.Equal(@"D:\models\mtp-gemma-4-31B-it.gguf", settings.MtpHeadPath);
    }

    [Theory]
    [InlineData("draft-dflash")]
    [InlineData("draft-dspark")]
    [InlineData("draft-eagle3")]
    public void HuggingFaceSuggestedLaunchSettingsKeepCurrentDraftTypes(string speculativeType)
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(
            defaults,
            $"llama-server -m target.gguf --spec-type {speculativeType} --model-draft helper.gguf");

        Assert.NotNull(settings);
        Assert.Equal(speculativeType, settings.SpeculativeType);
        Assert.Equal("helper.gguf", settings.SpecDraftModelPath);
    }


    [Fact]
    public void HuggingFaceSuggestedLaunchSettingsApplyConfigJsonAndFallbackToCli()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        const string readme = """
        Direct run:
            llama-cli -m model.gguf -c 8192 -n 256 --temp=0.4
        """;

        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(
            defaults,
            readme,
            """{"top_k":"33","max_new_tokens":128}""",
            """{"max_position_embeddings":65536}""");

        Assert.NotNull(settings);
        Assert.Equal(8_192, settings.ContextSize);
        Assert.Equal(256, settings.MaxTokens);
        Assert.Equal(0.4, settings.Temperature);
        Assert.Equal(33, settings.TopK);
    }


    [Fact]
    public void HuggingFaceSuggestedLaunchSettingsIgnoreOutOfRangeContextConfig()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());

        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(
            defaults,
            "",
            """{"top_k":44}""",
            """{"max_position_embeddings":524288}""");

        Assert.NotNull(settings);
        Assert.Equal(AppSettings.DefaultContextSize, settings.ContextSize);
        Assert.Equal(44, settings.TopK);
    }


    [Fact]
    public void GgufMetadataReaderRejectsHugeMetadataArraysQuickly()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "huge-array.gguf");
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            writer.Write((uint)3);
            writer.Write((ulong)0);
            writer.Write((ulong)1);
            WriteGgufString(writer, "tokenizer.ggml.tokens");
            writer.Write((uint)9);
            writer.Write((uint)0);
            writer.Write(1_000_001UL);
        }

        var metadata = GgufMetadataReader.TryRead(path);

        Assert.Empty(metadata);
    }


    [Fact]
    public void GgufMetadataReaderRejectsUnsupportedVersions()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "future-version.gguf");
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            writer.Write((uint)99);
            writer.Write((ulong)0);
            writer.Write((ulong)1);
            WriteGgufString(writer, "general.architecture");
            writer.Write((uint)8);
            WriteGgufString(writer, "future");
        }

        var metadata = GgufMetadataReader.TryRead(path);

        Assert.Empty(metadata);
    }

    private static void WriteMinimalGguf(string path, string architecture, params (string Key, uint Value)[] numericMetadata)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
        writer.Write((uint)3);
        writer.Write((ulong)0);
        writer.Write((ulong)(1 + numericMetadata.Length));
        WriteGgufString(writer, "general.architecture");
        writer.Write((uint)8);
        WriteGgufString(writer, architecture);
        foreach (var (key, value) in numericMetadata)
        {
            WriteGgufString(writer, key);
            writer.Write((uint)4);
            writer.Write(value);
        }
    }

}
