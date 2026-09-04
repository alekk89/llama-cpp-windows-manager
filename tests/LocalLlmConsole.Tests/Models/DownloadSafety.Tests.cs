using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class DownloadSafetyTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task DownloadPreparationFailureLeavesAnActionableFailedJob()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        using var service = new HuggingFaceService(store, jobs, new ModelCatalogService(store));
        var settings = AppSettings.CreateDefault(root);
        // A file at the cache-directory path fails before any HTTP request or partial-file write.
        await File.WriteAllTextAsync(settings.CacheRoot, "preserve this file", TestContext.Current.CancellationToken);
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", 1_000, 0);
        var job = await service.StartDownloadAsync(file, settings, TestContext.Current.CancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (service.IsDownloadActive(job.Id) && DateTime.UtcNow < deadline)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.False(service.IsDownloadActive(job.Id));
        var persisted = Assert.Single(await store.ListJobsAsync());
        Assert.Equal(JobStatus.Failed, persisted.Status);
        var payload = HuggingFaceService.ParseDownloadPayload(persisted.PayloadJson);
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.Error));
        Assert.Equal("preserve this file", await File.ReadAllTextAsync(settings.CacheRoot, TestContext.Current.CancellationToken));
        Assert.Empty(await store.ListModelsAsync());
    }

    [Fact]
    public void DownloadValidationPrefersAuthoritativeMetadataSizeOverShortResponseLength()
    {
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", 1_000, 0);
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[250])
        };

        var expected = HuggingFaceService.ValidateDownloadResponse(file, response, requestedOffset: 0);

        Assert.Equal(1_000, expected);
        Assert.Equal(1_000, HuggingFaceService.ExpectedBytes(file, responseTotal: 250));
    }

    [Fact]
    public void RequiredDownloadBytesUsesResponseSizeAndRejectsUnknownSize()
    {
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", 0, 0, Sha256: new string('a', 64));

        Assert.Equal(4_096, HuggingFaceService.RequiredDownloadBytes(file, responseTotal: 4_096));
        var error = Assert.Throws<InvalidDataException>(() =>
            HuggingFaceService.RequiredDownloadBytes(file, responseTotal: 0));

        Assert.Contains("trustworthy size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DownloadValidationRejectsResumeStartingAtWrongOffset()
    {
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", 1_000, 0);
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(new byte[600])
        };
        response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(400, 999, 1_000);

        var error = Assert.Throws<InvalidDataException>(() =>
            HuggingFaceService.ValidateDownloadResponse(file, response, requestedOffset: 500));

        Assert.Contains("requested byte offset", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DownloadValidationRejectsResumeWithWrongTotal()
    {
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", 1_000, 0);
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(new byte[500])
        };
        response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(500, 999, 1_200);

        var error = Assert.Throws<InvalidDataException>(() =>
            HuggingFaceService.ValidateDownloadResponse(file, response, requestedOffset: 500));

        Assert.Contains("metadata expects", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DownloadValidationRejectsUnexpectedPartialResponseForFullRequest()
    {
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", 1_000, 0);
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(new byte[500])
        };
        response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 499, 1_000);

        Assert.Throws<InvalidDataException>(() =>
            HuggingFaceService.ValidateDownloadResponse(file, response, requestedOffset: 0));
    }

    [Fact]
    public async Task BoundedDownloadCopyFailsWhenResponseBodyStalls()
    {
        await using var source = new BlockingReadStream();
        await using var destination = new MemoryStream();

        var error = await Assert.ThrowsAsync<IOException>(() =>
            BoundedStreamCopyService.CopyToAsync(
                source,
                destination,
                maximumBytes: 1_000,
                readIdleTimeout: TimeSpan.FromMilliseconds(25),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("produced no data", error.Message, StringComparison.OrdinalIgnoreCase);
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
        var downloadHistorySource = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Models", "MainWindow.DownloadHistory.cs"));
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


}
