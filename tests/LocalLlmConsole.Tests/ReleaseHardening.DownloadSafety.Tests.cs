using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
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


}
