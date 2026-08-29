using System.Diagnostics;
using System.Text;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimePackageJobsTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task RuntimePackageJobServiceCreatesUpdatesAndParsesPackageJobs()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var service = new RuntimePackageJobService(jobs);
        var preset = new RuntimePackagePreset("official-prebuilt-windows-cuda", "CUDA Windows", RuntimeBackend.Cuda, RuntimeMode.Native, "official-cuda-source");

        var install = await service.CreateInstallJobAsync(preset, TestContext.Current.CancellationToken);
        var check = await service.CreateCheckJobAsync(preset, TestContext.Current.CancellationToken);
        await service.UpdateAsync(
            install,
            JobStatus.Running,
            preset,
            "install",
            Path.Combine(root, "runtime"),
            "Installing package",
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken);

        var stored = await store.ListJobsAsync();
        var updated = stored.Single(job => job.Id == install.Id);
        var payload = RuntimePackageJobService.ParsePayload(updated.PayloadJson);
        var log = await File.ReadAllTextAsync(install.LogPath, TestContext.Current.CancellationToken);

        Assert.Equal("runtime-package-download", install.Kind);
        Assert.Equal("runtime-package-update-check", check.Kind);
        Assert.NotNull(payload);
        Assert.Equal(preset.Id, payload.Preset.Id);
        Assert.Equal(RuntimeBackend.Cuda, payload.Backend);
        Assert.Equal(RuntimeMode.Native, payload.Mode);
        Assert.Equal("official-cuda-source", payload.SourcePresetId);
        Assert.Equal(RuntimePackageSourceCatalog.LatestReleaseApiUrl, payload.ReleaseApiUrl);
        Assert.Equal(RuntimePackageSourceCatalog.ReleasesUrl, payload.ReleasePageUrl);
        Assert.Equal("official llama.cpp", payload.PackageSourceLabel);
        Assert.Equal("official-prebuilt", payload.PackageSourceKey);
        Assert.Equal(RuntimePackageSourceCatalog.OfficialRepositoryUrl, payload.RepositoryUrl);
        Assert.Equal("install", payload.Action);
        Assert.Contains("Installing package", log, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimePackageInstallWorkflowServiceOwnsInstallJobLifecycle()
    {
        var root = CreateTempRoot();
        var source = Path.Combine(root, "package-source", "llama-b9354-bin-win-cpu-x64", "bin");
        var archive = Path.Combine(root, "runtime.zip");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "llama-server.exe"), "fake server", TestContext.Current.CancellationToken);
        System.IO.Compression.ZipFile.CreateFromDirectory(Path.Combine(root, "package-source"), archive);
        var archiveBytes = await File.ReadAllBytesAsync(archive, TestContext.Current.CancellationToken);
        var archiveSha256 = Sha256Hex(archiveBytes);
        var releaseJson = $$"""
        {
          "tag_name": "b9354",
          "target_commitish": "9777256c3130",
          "html_url": "https://example.com/release",
          "published_at": "2026-05-28T10:00:00Z",
          "assets": [
            {
              "name": "llama-b9354-bin-win-cpu-x64.zip",
              "browser_download_url": "https://example.com/win-cpu.zip",
              "size": {{archiveBytes.Length}},
              "digest": "sha256:{{archiveSha256}}"
            }
          ]
        }
        """;
        using var handler = new CapturingHttpHandler(request =>
        {
            if (request.RequestUri?.ToString() == RuntimePackageSourceCatalog.LatestReleaseApiUrl)
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(releaseJson) };
            if (request.RequestUri?.ToString() == "https://example.com/win-cpu.zip")
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(archiveBytes) };
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var runtimes = new RuntimeRegistryService(store);
        var workflow = new RuntimePackageInstallWorkflowService(
            new RuntimePackageInstallService(http, runtimes),
            new RuntimePackageJobService(new JobEngine(store, Path.Combine(root, "logs"))),
            new RuntimePackageWslFileService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", "")), () => "wsl.exe"));
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cpu");

        var result = await workflow.InstallAsync(new RuntimePackageInstallWorkflowRequest(
            preset,
            settings,
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken));
        var job = Assert.Single(await store.ListJobsAsync());
        var payload = RuntimePackageJobService.ParsePayload(job.PayloadJson);
        var registered = Assert.Single(await store.ListRuntimesAsync());
        var log = await File.ReadAllTextAsync(job.LogPath, TestContext.Current.CancellationToken);

        Assert.Equal(result.Job.Id, job.Id);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.NotNull(payload);
        Assert.Equal("install", payload.Action);
        Assert.Equal(result.RuntimeFolder, payload.InstallDir);
        Assert.Equal(result.RuntimeFolder, RuntimeMetadataService.Folder(registered));
        Assert.Equal("b9354", result.UpdateState.LocalTag);
        Assert.Contains("Resolving latest official llama.cpp release", log, StringComparison.Ordinal);
        Assert.Contains("Downloading llama-b9354-bin-win-cpu-x64.zip", log, StringComparison.Ordinal);
        Assert.Contains("installed from b9354", log, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task RuntimePackageWslFileServiceBuildsArchiveAndChmodCommands()
    {
        var root = CreateTempRoot();
        var logPath = Path.Combine(root, "logs", "package-wsl.log");
        var archivePath = Path.Combine(root, "cache", "llama's.tar.gz");
        var installDir = Path.Combine(root, "runtimes", "llama install");
        var executable = Path.Combine(installDir, "bin", "llama-server");
        CreateTarGzipArchive(
            archivePath,
            new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "bin/llama-server")
            {
                DataStream = new MemoryStream([1])
            });
        var runner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "ok", ""));
        var service = new RuntimePackageWslFileService(runner, () => "wsl.exe");

        await service.ExtractArchiveAsync(new RuntimePackageWslArchiveRequest(
            "Ubuntu-24.04",
            archivePath,
            installDir,
            logPath,
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken));
        await service.TryPrepareExecutableAsync(new RuntimePackageWslExecutableRequest(
            new RuntimePackagePreset("pkg", "Package", RuntimeBackend.Cpu, RuntimeMode.Wsl, "source"),
            "Ubuntu-24.04",
            executable,
            logPath,
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken));
        await service.TryPrepareExecutableAsync(new RuntimePackageWslExecutableRequest(
            new RuntimePackagePreset("native", "Native", RuntimeBackend.Cpu, RuntimeMode.Native, "source"),
            "Ubuntu-24.04",
            executable,
            logPath,
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken));

        var extractCommand = runner.Commands[0].Last();
        var chmodCommand = runner.Commands[1].Last();
        var log = await File.ReadAllTextAsync(logPath, TestContext.Current.CancellationToken);

        Assert.Equal(2, runner.Commands.Count);
        Assert.Contains("Ubuntu-24.04", runner.Commands[0]);
        Assert.Contains("tar --overwrite -xzf", extractCommand, StringComparison.Ordinal);
        Assert.Contains(CommandLineService.BashQuote(RuntimePackageWslFileService.WindowsPathToWslPath(archivePath)), extractCommand, StringComparison.Ordinal);
        Assert.Contains(CommandLineService.BashQuote(RuntimePackageWslFileService.WindowsPathToWslPath(installDir)), extractCommand, StringComparison.Ordinal);
        Assert.Contains("chmod +x", chmodCommand, StringComparison.Ordinal);
        Assert.Contains(CommandLineService.BashQuote(RuntimePackageWslFileService.WindowsPathToWslPath(executable)), chmodCommand, StringComparison.Ordinal);
        Assert.Contains("ok", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimePackageWslFileServiceRejectsUnsafeTarArchivesBeforeRunningWsl()
    {
        var root = CreateTempRoot();
        var logPath = Path.Combine(root, "logs", "package-wsl.log");
        var installDir = Path.Combine(root, "runtime");
        var runner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "should not run", ""));
        var service = new RuntimePackageWslFileService(runner, () => "wsl.exe");
        var escapeName = $"llama-runtime-escape-{Guid.NewGuid():N}.txt";

        var traversalArchive = Path.Combine(root, "cache", "runtime-traversal.tar.gz");
        CreateTarGzipArchive(
            traversalArchive,
            new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "bin/llama-server")
            {
                DataStream = new MemoryStream([1])
            },
            new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "../" + escapeName)
            {
                DataStream = new MemoryStream([2])
            });

        var traversal = await AssertRejectsArchiveAsync(traversalArchive);
        Assert.Contains("unsafe path", traversal.Message, StringComparison.OrdinalIgnoreCase);

        var symlinkArchive = Path.Combine(root, "cache", "runtime-symlink.tar.gz");
        CreateTarGzipArchive(
            symlinkArchive,
            new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.SymbolicLink, "bin/outside")
            {
                LinkName = "../../" + escapeName
            });

        var symlink = await AssertRejectsArchiveAsync(symlinkArchive);
        Assert.Contains("outside", symlink.Message, StringComparison.OrdinalIgnoreCase);

        var hardlinkArchive = Path.Combine(root, "cache", "runtime-hardlink.tar.gz");
        CreateTarGzipArchive(
            hardlinkArchive,
            new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.HardLink, "bin/outside-hardlink")
            {
                LinkName = "../" + escapeName
            });

        var hardlink = await AssertRejectsArchiveAsync(hardlinkArchive);
        Assert.Contains("outside", hardlink.Message, StringComparison.OrdinalIgnoreCase);

        var fifoArchive = Path.Combine(root, "cache", "runtime-fifo.tar.gz");
        CreateTarGzipArchive(
            fifoArchive,
            new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.Fifo, "bin/runtime-pipe"));

        var fifo = await AssertRejectsArchiveAsync(fifoArchive);
        Assert.Contains("unsupported tar entry type", fifo.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(runner.Commands);
        Assert.False(File.Exists(Path.Combine(root, escapeName)));

        Task<InvalidOperationException> AssertRejectsArchiveAsync(string archivePath)
            => Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ExtractArchiveAsync(new RuntimePackageWslArchiveRequest(
                    "Ubuntu-24.04",
                    archivePath,
                    installDir,
                    logPath,
                    BoundedLogFile.MegabytesToBytes(1),
                    TestContext.Current.CancellationToken)));
    }


    [Fact]
    public async Task RuntimePackageWslFileServiceReportsArchiveFailuresAndChmodWarnings()
    {
        var root = CreateTempRoot();
        var archivePath = Path.Combine(root, "cache", "runtime.tar.gz");
        CreateTarGzipArchive(
            archivePath,
            new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "bin/llama-server")
            {
                DataStream = new MemoryStream([1])
            });
        var archiveFailure = new RuntimePackageWslFileService(
            new ScriptedProcessRunner(_ => new ProcessRunResult(2, "", "tar failed")),
            () => "wsl.exe");

        var archive = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            archiveFailure.ExtractArchiveAsync(new RuntimePackageWslArchiveRequest(
                "Ubuntu-24.04",
                archivePath,
                Path.Combine(root, "runtime"),
                Path.Combine(root, "logs", "archive.log"),
                BoundedLogFile.MegabytesToBytes(1),
                TestContext.Current.CancellationToken)));
        Assert.Contains("exit code 2", archive.Message, StringComparison.Ordinal);
        Assert.Contains("tar failed", archive.Message, StringComparison.Ordinal);

        var logPath = Path.Combine(root, "logs", "chmod.log");
        var chmodFailure = new RuntimePackageWslFileService(
            new ScriptedProcessRunner(_ => throw new InvalidOperationException("chmod failed")),
            () => "wsl.exe");
        await chmodFailure.TryPrepareExecutableAsync(new RuntimePackageWslExecutableRequest(
            new RuntimePackagePreset("pkg", "Package", RuntimeBackend.Cpu, RuntimeMode.Wsl, "source"),
            "Ubuntu-24.04",
            Path.Combine(root, "runtime", "bin", "llama-server"),
            logPath,
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken));

        var chmodLog = await File.ReadAllTextAsync(logPath, TestContext.Current.CancellationToken);
        Assert.Contains("Warning: could not chmod WSL runtime executable", chmodLog, StringComparison.Ordinal);
        Assert.Contains("chmod failed", chmodLog, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimePackageMetadataIdentifiesInstalledPrebuiltRuntime()
    {
        var root = CreateTempRoot();
        var runtimeFolder = Path.Combine(root, "official-prebuilt-windows-cuda-b9354");
        Directory.CreateDirectory(runtimeFolder);
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cuda");
        var runtime = new RuntimeRecord(
            "runtime-1",
            "Official llama.cpp CUDA Windows",
            RuntimeMode.Native,
            RuntimeBackend.Cuda,
            Path.Combine(runtimeFolder, "llama-server.exe"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = runtimeFolder,
                runtimeMetadata = new
                {
                    managedPackageId = preset.Id,
                    managedPresetId = preset.Id,
                    releaseTag = "b9354"
                }
            }),
            DateTimeOffset.UtcNow);

        var installed = RuntimePackageInventoryPresenter.InstalledPackages([runtime], preset);

        Assert.Equal(preset.Id, RuntimeMetadataService.ManagedPackageId(runtime));
        Assert.Equal(preset.Id, RuntimeMetadataService.ManagedPresetId(runtime));
        Assert.Equal("b9354", RuntimeMetadataService.PackageTag(runtime));
        Assert.Single(installed);
        Assert.Equal("b9354", RuntimePackageInventoryPresenter.LatestInstalledTag(installed));
        Assert.False(RuntimePackageInventoryPresenter.CanInstallPackage(installed, null));
        Assert.True(RuntimePackageInventoryPresenter.CanInstallPackage(installed, new RuntimePackageUpdateState(true, "b9354", "b9355", "", "", DateTimeOffset.UtcNow)));
    }


}
