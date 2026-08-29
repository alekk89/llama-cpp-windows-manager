using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimePackageInstallationTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task RuntimeReleaseFixturesCoverUnknownPartialMalformedAndRateLimitedResponses()
    {
        var validJson = await File.ReadAllTextAsync(FindRepositoryFile(
            "tests", "fixtures", "upstream", "github-release-valid-with-unknown-fields.json"),
            TestContext.Current.CancellationToken);
        var release = RuntimePackageReleaseClient.ParseReleaseJson(validJson);

        Assert.Equal("b9999", release.TagName);
        Assert.Equal(2, release.Assets.Count);
        var archive = release.Assets.Single(asset => asset.Name.EndsWith(".zip", StringComparison.Ordinal));
        Assert.Equal(new string('a', 64), archive.Sha256);
        Assert.EndsWith(".sha256", archive.ChecksumUrl, StringComparison.Ordinal);
        Assert.DoesNotContain(release.Assets, asset => asset.Name.StartsWith("partial-asset", StringComparison.Ordinal));

        var malformedJson = await File.ReadAllTextAsync(FindRepositoryFile(
            "tests", "fixtures", "upstream", "github-release-malformed.json"),
            TestContext.Current.CancellationToken);
        var malformed = Assert.Throws<InvalidOperationException>(() => RuntimePackageReleaseClient.ParseReleaseJson(malformedJson));
        Assert.Contains("release tag", malformed.Message, StringComparison.OrdinalIgnoreCase);

        var rateLimitJson = await File.ReadAllTextAsync(FindRepositoryFile(
            "tests", "fixtures", "upstream", "github-rate-limit.json"),
            TestContext.Current.CancellationToken);
        using var handler = new CapturingHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(rateLimitJson)
        });
        using var client = new HttpClient(handler);
        var rateLimited = await Assert.ThrowsAsync<HttpRequestException>(() =>
            RuntimePackageReleaseClient.FetchLatestReleaseAsync(client, TestContext.Current.CancellationToken));
        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, rateLimited.StatusCode);
    }

    [Fact]
    public async Task RuntimeReleaseClientResolvesOfficialSemanticReleaseToNightlyBinaryRelease()
    {
        const string latestReleaseJson = """
        {
          "tag_name": "v0.3.0",
          "target_commitish": "c1d0e7a004015f23bc0233470b747b596f29b264",
          "html_url": "https://github.com/ggml-org/llama.cpp/releases/tag/v0.3.0",
          "published_at": "2026-08-25T10:22:58Z",
          "assets": [
            {
              "name": "nightly-tag.txt",
              "browser_download_url": "https://github.com/ggml-org/llama.cpp/releases/download/v0.3.0/nightly-tag.txt",
              "size": 7
            }
          ]
        }
        """;
        const string nightlyReleaseJson = """
        {
          "tag_name": "b10621",
          "target_commitish": "master",
          "html_url": "https://github.com/ggml-org/llama.cpp/releases/tag/b10621",
          "published_at": "2026-08-27T08:00:00Z",
          "assets": [
            { "name": "llama-b10621-bin-win-cuda-13.3-x64.zip", "browser_download_url": "https://example.com/cuda.zip", "size": 13 },
            { "name": "cudart-llama-bin-win-cuda-13.3-x64.zip", "browser_download_url": "https://example.com/cudart.zip", "size": 3 }
          ]
        }
        """;
        var requests = new List<string>();
        using var handler = new CapturingHttpHandler(request =>
        {
            var url = request.RequestUri?.ToString() ?? "";
            requests.Add(url);
            return url switch
            {
                RuntimePackageSourceCatalog.LatestReleaseApiUrl => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(latestReleaseJson)
                },
                "https://github.com/ggml-org/llama.cpp/releases/download/v0.3.0/nightly-tag.txt" => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("b10621")
                },
                "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/b10621" => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(nightlyReleaseJson)
                },
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            };
        });
        using var client = new HttpClient(handler);
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate =>
            candidate.Id == "official-prebuilt-windows-cuda");

        var release = await RuntimePackageReleaseClient.FetchLatestReleaseAsync(
            client,
            preset,
            TestContext.Current.CancellationToken);
        var selection = RuntimePackageAssetSelector.SelectAssets(preset, release);

        Assert.Equal("b10621", release.TagName);
        Assert.Equal("llama-b10621-bin-win-cuda-13.3-x64.zip", selection.PrimaryAsset.Name);
        Assert.Equal("cudart-llama-bin-win-cuda-13.3-x64.zip", Assert.Single(selection.AdditionalAssets).Name);
        Assert.Equal(
            [
                RuntimePackageSourceCatalog.LatestReleaseApiUrl,
                "https://github.com/ggml-org/llama.cpp/releases/download/v0.3.0/nightly-tag.txt",
                "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/b10621"
            ],
            requests);
    }

    [Fact]
    public void RuntimePackageAssetSelectorSelectsOfficialReleaseAssets()
    {
        var release = RuntimePackageReleaseClient.ParseReleaseJson("""
        {
          "tag_name": "b9354",
          "html_url": "https://github.com/ggml-org/llama.cpp/releases/tag/b9354",
          "published_at": "2026-05-27T08:00:00Z",
          "assets": [
            { "name": "llama-b9354-bin-win-cuda-13.1-x64.zip", "browser_download_url": "https://example.com/cuda13.zip", "size": 13 },
            { "name": "cudart-llama-bin-win-cuda-13.1-x64.zip", "browser_download_url": "https://example.com/cudart13.zip", "size": 3 },
            { "name": "llama-b9354-bin-win-cuda-12.4-x64.zip", "browser_download_url": "https://example.com/cuda12.zip", "size": 12 },
            { "name": "cudart-llama-bin-win-cuda-12.4-x64.zip", "browser_download_url": "https://example.com/cudart12.zip", "size": 2 },
            { "name": "llama-b9354-bin-win-vulkan-x64.zip", "browser_download_url": "https://example.com/win-vulkan.zip", "size": 4 },
            { "name": "llama-b9354-bin-win-sycl-x64.zip", "browser_download_url": "https://example.com/win-sycl.zip", "size": 9 },
            { "name": "llama-b9354-bin-win-cpu-x64.zip", "browser_download_url": "https://example.com/win-cpu.zip", "size": 5 },
            { "name": "llama-b9354-bin-ubuntu-cuda-13.1-x64.tar.gz", "browser_download_url": "https://example.com/ubuntu-cuda13.tar.gz", "size": 11 },
            { "name": "llama-b9354-bin-ubuntu-cuda-12.4-x64.tar.gz", "browser_download_url": "https://example.com/ubuntu-cuda.tar.gz", "size": 8 },
            { "name": "llama-b9354-bin-ubuntu-vulkan-x64.tar.gz", "browser_download_url": "https://example.com/ubuntu-vulkan.tar.gz", "size": 6 },
            { "name": "llama-b9354-bin-ubuntu-sycl-f16-x64.tar.gz", "browser_download_url": "https://example.com/ubuntu-sycl.tar.gz", "size": 10 },
            { "name": "llama-b9354-bin-ubuntu-x64.tar.gz", "browser_download_url": "https://example.com/ubuntu-cpu.tar.gz", "size": 7 }
          ]
        }
        """);

        var presets = RuntimePackageSourceCatalog.PresetRows();
        var cuda = RuntimePackageAssetSelector.SelectAssets(presets.Single(preset => preset.Id == "official-prebuilt-windows-cuda"), release);
        var cudaCompatibility = RuntimePackageAssetSelector.SelectAssets(presets.Single(preset => preset.Id == "official-prebuilt-windows-cuda"), release, "compatibility");
        var cudaWsl = RuntimePackageAssetSelector.SelectAssets(presets.Single(preset => preset.Id == "official-prebuilt-cuda"), release);
        var cudaWslCompatibility = RuntimePackageAssetSelector.SelectAssets(presets.Single(preset => preset.Id == "official-prebuilt-cuda"), release, "compatibility");
        var vulkanWsl = RuntimePackageAssetSelector.SelectAssets(presets.Single(preset => preset.Id == "official-prebuilt-vulkan"), release);
        var sycl = RuntimePackageAssetSelector.SelectAssets(presets.Single(preset => preset.Id == "official-prebuilt-windows-sycl"), release);
        var syclWsl = RuntimePackageAssetSelector.SelectAssets(presets.Single(preset => preset.Id == "official-prebuilt-sycl"), release);
        var atomicWindows = presets.Single(preset => preset.Id == "atomic-prebuilt-windows-cuda");

        Assert.Equal(
            ["official-prebuilt-windows-cuda", "official-prebuilt-cuda", "official-prebuilt-windows-vulkan", "official-prebuilt-vulkan", "official-prebuilt-windows-sycl", "official-prebuilt-sycl", "official-prebuilt-windows-cpu", "official-prebuilt-cpu", "atomic-prebuilt-windows-cuda", "atomic-prebuilt-cuda", "thetom-prebuilt-windows-cuda", "thetom-prebuilt-vulkan", "thetom-prebuilt-cpu"],
            presets.Select(preset => preset.Id).ToArray());
        Assert.Equal("b9354", release.TagName);
        Assert.Equal("llama-b9354-bin-win-cuda-13.1-x64.zip", cuda.PrimaryAsset.Name);
        Assert.Equal("cudart-llama-bin-win-cuda-13.1-x64.zip", Assert.Single(cuda.AdditionalAssets).Name);
        Assert.Contains("cudart-llama-bin-win-cuda-13.1-x64.zip", cuda.AssetSummary, StringComparison.Ordinal);
        Assert.True(RuntimePackageAssetSelector.AssetSummariesMatch("cudart-llama-bin-win-cuda-13.1-x64.zip, llama-b9354-bin-win-cuda-13.1-x64.zip", cuda.AssetSummary));
        Assert.Equal("llama-b9354-bin-win-cuda-12.4-x64.zip", cudaCompatibility.PrimaryAsset.Name);
        Assert.Equal("cudart-llama-bin-win-cuda-12.4-x64.zip", Assert.Single(cudaCompatibility.AdditionalAssets).Name);
        Assert.Equal("llama-b9354-bin-ubuntu-cuda-13.1-x64.tar.gz", cudaWsl.PrimaryAsset.Name);
        Assert.Equal("llama-b9354-bin-ubuntu-cuda-12.4-x64.tar.gz", cudaWslCompatibility.PrimaryAsset.Name);
        Assert.Equal("CUDA WSL", RuntimePackageSourceCatalog.BackendLabel(cudaWsl.Preset));
        Assert.Equal("llama-b9354-bin-ubuntu-vulkan-x64.tar.gz", vulkanWsl.PrimaryAsset.Name);
        Assert.Equal("Vulkan WSL", RuntimePackageSourceCatalog.BackendLabel(vulkanWsl.Preset));
        Assert.Equal("llama-b9354-bin-win-sycl-x64.zip", sycl.PrimaryAsset.Name);
        Assert.Equal("SYCL Windows", RuntimePackageSourceCatalog.BackendLabel(sycl.Preset));
        Assert.Equal("llama-b9354-bin-ubuntu-sycl-f16-x64.tar.gz", syclWsl.PrimaryAsset.Name);
        Assert.Equal("SYCL WSL", RuntimePackageSourceCatalog.BackendLabel(syclWsl.Preset));
        Assert.Equal("atomic-windows-turboquant-cuda", atomicWindows.SourcePresetId);
        Assert.Equal(RuntimePackageSourceCatalog.AtomicTurboQuantHuggingFaceApiUrl, RuntimePackageSourceCatalog.ReleaseApiUrlFor(atomicWindows));
        Assert.Equal("Atomic llama.cpp prebuilt", RuntimePackageSourceCatalog.PackageSourceLabel(atomicWindows));
        Assert.EndsWith(Path.Combine("official-prebuilt-windows-cuda-b9354"), RuntimePackageInstallFileService.InstallDir(Path.Combine("D:", "runtimes"), cuda), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void RuntimePackageAssetSelectorSelectsTheTomTurboQuantAssets()
    {
        var release = RuntimePackageReleaseClient.ParseReleaseJson("""
        {
          "tag_name": "tqp-v0.3.0",
          "target_commitish": "main",
          "html_url": "https://github.com/TheTom/llama-cpp-turboquant/releases/tag/tqp-v0.3.0",
          "published_at": "2026-07-12T05:28:31Z",
          "assets": [
            { "name": "turboquant-plus-tqp-v0.3.0-windows-x64-cuda12.4.zip", "browser_download_url": "https://example.com/thetom-cuda.zip", "size": 30, "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
            { "name": "turboquant-plus-tqp-v0.3.0-linux-x64-vulkan.tar.gz", "browser_download_url": "https://example.com/thetom-vulkan.tar.gz", "size": 20, "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
            { "name": "turboquant-plus-tqp-v0.3.0-linux-x64-cpu.tar.gz", "browser_download_url": "https://example.com/thetom-cpu.tar.gz", "size": 10, "digest": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc" }
          ]
        }
        """);
        var presets = RuntimePackageSourceCatalog.PresetRows();
        var windowsCudaPreset = presets.Single(candidate => candidate.Id == "thetom-prebuilt-windows-cuda");
        var vulkanPreset = presets.Single(candidate => candidate.Id == "thetom-prebuilt-vulkan");
        var cpuPreset = presets.Single(candidate => candidate.Id == "thetom-prebuilt-cpu");

        var windowsCuda = RuntimePackageAssetSelector.SelectAssets(windowsCudaPreset, release);
        var vulkan = RuntimePackageAssetSelector.SelectAssets(vulkanPreset, release);
        var cpu = RuntimePackageAssetSelector.SelectAssets(cpuPreset, release);

        Assert.Equal("turboquant-plus-tqp-v0.3.0-windows-x64-cuda12.4.zip", windowsCuda.PrimaryAsset.Name);
        Assert.Equal("turboquant-plus-tqp-v0.3.0-linux-x64-vulkan.tar.gz", vulkan.PrimaryAsset.Name);
        Assert.Equal("turboquant-plus-tqp-v0.3.0-linux-x64-cpu.tar.gz", cpu.PrimaryAsset.Name);
        Assert.Equal(new string('a', 64), windowsCuda.PrimaryAsset.Sha256);
        Assert.Equal(RuntimePackageSourceCatalog.TheTomTurboQuantReleaseApiUrl, RuntimePackageSourceCatalog.ReleaseApiUrlFor(windowsCudaPreset));
        Assert.Equal(RuntimePackageSourceCatalog.TheTomTurboQuantRepositoryUrl, RuntimePackageSourceCatalog.RepositoryUrlFor(windowsCudaPreset));
        Assert.Equal("TheTom TurboQuant prebuilt", RuntimePackageSourceCatalog.PackageSourceLabel(windowsCudaPreset));
        Assert.Equal("thetom-windows-turboquant-cuda", windowsCudaPreset.SourcePresetId);
        Assert.Equal("thetom-turboquant-vulkan", vulkanPreset.SourcePresetId);
        Assert.Equal("thetom-turboquant-cpu", cpuPreset.SourcePresetId);
    }


    [Fact]
    public void RuntimePackageAssetSelectorSelectsAtomicHuggingFaceAssets()
    {
        var release = RuntimePackageReleaseClient.ParseHuggingFaceModelJson("""
        {
          "id": "atomicmilkshake/llama-cpp-turboquant-binaries",
          "sha": "402c91005e37c8b42a3159c5b0f5f7d062095ba6",
          "lastModified": "2026-04-08T20:01:19.000Z",
          "siblings": [
            { "rfilename": ".gitattributes" },
            { "rfilename": "README.md" },
            { "rfilename": "llama-turboquant-triattention-win-cu13-x64.zip" }
          ]
        }
        """, RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "atomic-prebuilt-windows-cuda"));
        var windowsPreset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "atomic-prebuilt-windows-cuda");
        var wslPreset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "atomic-prebuilt-cuda");

        var windows = RuntimePackageAssetSelector.SelectAssets(windowsPreset, release);
        var unavailable = Assert.Throws<RuntimePackageAssetUnavailableException>(() => RuntimePackageAssetSelector.SelectAssets(wslPreset, release));

        Assert.Equal("hf-402c91005e37", release.TagName);
        Assert.Equal("402c91005e37c8b42a3159c5b0f5f7d062095ba6", release.TargetCommit);
        Assert.Equal(release.TargetCommit, windows.TargetCommit);
        Assert.Equal(RuntimePackageSourceCatalog.AtomicTurboQuantHuggingFacePageUrl, release.HtmlUrl);
        Assert.Equal("llama-turboquant-triattention-win-cu13-x64.zip", windows.PrimaryAsset.Name);
        Assert.Contains("/resolve/402c91005e37c8b42a3159c5b0f5f7d062095ba6/llama-turboquant-triattention-win-cu13-x64.zip?download=true", windows.PrimaryAsset.DownloadUrl, StringComparison.Ordinal);
        Assert.Contains("Atomic llama.cpp TurboQuant CUDA WSL", unavailable.Message, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimePackageAssetSelectorReportsCudaWslUnavailableWhenReleaseOmitsAsset()
    {
        var release = RuntimePackageReleaseClient.ParseReleaseJson("""
        {
          "tag_name": "b9357",
          "html_url": "https://github.com/ggml-org/llama.cpp/releases/tag/b9357",
          "target_commitish": "abcdef1234567890",
          "assets": [
            { "name": "llama-b9357-bin-ubuntu-vulkan-x64.tar.gz", "browser_download_url": "https://example.com/ubuntu-vulkan.tar.gz", "size": 6 }
          ]
        }
        """);
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-cuda");
        var unavailable = new RuntimePackageUpdateState(false, "", release.TagName, release.HtmlUrl, "not available", DateTimeOffset.UtcNow, TargetCommit: release.TargetCommit, IsAvailable: false);

        var ex = Assert.Throws<RuntimePackageAssetUnavailableException>(() => RuntimePackageAssetSelector.SelectAssets(preset, release));

        Assert.Contains("CUDA WSL", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Not published", RuntimePackageInventoryPresenter.LocalStatusLabel([], [], unavailable));
        Assert.False(RuntimePackageInventoryPresenter.CanInstallPackage([], [], unavailable));
    }


    [Fact]
    public void RuntimePackageInstallFileServiceExtractsAndFindsRuntimeExecutable()
    {
        var root = CreateTempRoot();
        var source = Path.Combine(root, "source");
        var nested = Path.Combine(source, "llama-b9354-bin-win-cpu-x64", "bin");
        var archive = Path.Combine(root, "runtime.zip");
        var destination = Path.Combine(root, "runtime");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "llama-server.exe"), "fake");
        System.IO.Compression.ZipFile.CreateFromDirectory(source, archive);

        RuntimePackageInstallFileService.ExtractArchive(archive, destination);

        var executable = RuntimePackageInstallFileService.FindRuntimeExecutable(destination, RuntimeMode.Native);
        Assert.Equal(Path.Combine(destination, "bin", "llama-server.exe"), executable);
        Assert.Equal(destination, RuntimePackageInstallFileService.RuntimeFolderFromExecutable(executable));
    }


    [Fact]
    public void RuntimePackageInstallFileServiceExtractsCompanionArchivesBesidePrimaryRuntime()
    {
        var root = CreateTempRoot();
        var primarySource = Path.Combine(root, "primary-source");
        var primaryNested = Path.Combine(primarySource, "llama-b9354-bin-win-cuda-x64", "bin");
        var companionSource = Path.Combine(root, "companion-source");
        var companionNested = Path.Combine(companionSource, "cudart-llama-bin-win-cuda-12.4-x64", "bin");
        var primaryArchive = Path.Combine(root, "primary.zip");
        var companionArchive = Path.Combine(root, "companion.zip");
        var destination = Path.Combine(root, "runtime");
        Directory.CreateDirectory(primaryNested);
        Directory.CreateDirectory(companionNested);
        File.WriteAllText(Path.Combine(primaryNested, "llama-server.exe"), "fake server");
        File.WriteAllText(Path.Combine(companionNested, "cudart64_12.dll"), "fake cudart");
        System.IO.Compression.ZipFile.CreateFromDirectory(primarySource, primaryArchive);
        System.IO.Compression.ZipFile.CreateFromDirectory(companionSource, companionArchive);

        RuntimePackageInstallFileService.ExtractArchive(primaryArchive, destination);
        RuntimePackageInstallFileService.ExtractArchive(companionArchive, destination);

        Assert.True(File.Exists(Path.Combine(destination, "bin", "llama-server.exe")));
        Assert.True(File.Exists(Path.Combine(destination, "bin", "cudart64_12.dll")));
        Assert.False(Directory.Exists(Path.Combine(destination, "cudart-llama-bin-win-cuda-12.4-x64")));
    }

    [Fact]
    public void RuntimePackageInstallFileServiceRejectsArchiveTraversal()
    {
        var root = CreateTempRoot();
        var archive = Path.Combine(root, "runtime.zip");
        var destination = Path.Combine(root, "runtime");
        var escapeName = $"llama-runtime-escape-{Guid.NewGuid():N}.txt";
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var safe = zip.CreateEntry("bin/llama-server.exe").Open())
            {
                safe.WriteByte(1);
            }

            using (var unsafeEntry = zip.CreateEntry("../" + escapeName).Open())
            {
                unsafeEntry.WriteByte(2);
            }
        }

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RuntimePackageInstallFileService.ExtractArchive(archive, destination));

        Assert.Contains("unsafe path", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), escapeName)));
        Assert.False(File.Exists(Path.Combine(root, escapeName)));
    }

    [Fact]
    public void RuntimePackageInstallFileServiceRejectsTarGzipTraversalAndUnsafeLinks()
    {
        var root = CreateTempRoot();
        var destination = Path.Combine(root, "runtime");
        var escapeName = $"llama-runtime-escape-{Guid.NewGuid():N}.txt";
        var traversalArchive = Path.Combine(root, "runtime-traversal.tar.gz");
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

        var traversal = Assert.Throws<InvalidOperationException>(() =>
            RuntimePackageInstallFileService.ExtractArchive(traversalArchive, destination));

        Assert.Contains("unsafe path", traversal.Message, StringComparison.OrdinalIgnoreCase);

        var linkArchive = Path.Combine(root, "runtime-link.tar.gz");
        CreateTarGzipArchive(
            linkArchive,
            new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.SymbolicLink, "bin/outside")
            {
                LinkName = "../../" + escapeName
            });

        var link = Assert.Throws<InvalidOperationException>(() =>
            RuntimePackageInstallFileService.ExtractArchive(linkArchive, destination));

        Assert.Contains("outside", link.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), escapeName)));
        Assert.False(File.Exists(Path.Combine(root, escapeName)));
    }

    [Fact]
    public async Task RuntimePackageInstallFileServiceVerifiesRuntimePackageSizeAndChecksum()
    {
        var root = CreateTempRoot();
        var bytes = Encoding.UTF8.GetBytes("verified runtime archive");
        var sha256 = Sha256Hex(bytes);
        using var handler = new CapturingHttpHandler(request =>
        {
            var uri = request.RequestUri?.ToString();
            if (uri == "https://example.com/runtime.zip")
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            if (uri == "https://example.com/runtime.zip.sha256")
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent($"{sha256}  runtime.zip") };
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        var verifiedPath = Path.Combine(root, "verified.zip");
        var mismatchPath = Path.Combine(root, "checksum-mismatch.zip");
        var sizeMismatchPath = Path.Combine(root, "size-mismatch.zip");
        var missingChecksumPath = Path.Combine(root, "missing-checksum.zip");

        await RuntimePackageInstallFileService.DownloadAssetAsync(
            http,
            new RuntimePackageAsset("runtime.zip", "https://example.com/runtime.zip", bytes.Length, ChecksumUrl: "https://example.com/runtime.zip.sha256"),
            verifiedPath,
            TestContext.Current.CancellationToken);

        var checksumMismatch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RuntimePackageInstallFileService.DownloadAssetAsync(
                http,
                new RuntimePackageAsset("runtime.zip", "https://example.com/runtime.zip", bytes.Length, Sha256: new string('a', 64)),
                mismatchPath,
                TestContext.Current.CancellationToken));
        var sizeMismatch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RuntimePackageInstallFileService.DownloadAssetAsync(
                http,
                new RuntimePackageAsset("runtime.zip", "https://example.com/runtime.zip", bytes.Length + 1, Sha256: sha256),
                sizeMismatchPath,
                TestContext.Current.CancellationToken));
        var missingChecksum = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RuntimePackageInstallFileService.DownloadAssetAsync(
                http,
                new RuntimePackageAsset("runtime.zip", "https://example.com/runtime.zip", bytes.Length),
                missingChecksumPath,
                TestContext.Current.CancellationToken));

        Assert.True(File.Exists(verifiedPath));
        Assert.Contains("checksum mismatch", checksumMismatch.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("size mismatch", sizeMismatch.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing SHA-256", missingChecksum.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(mismatchPath));
        Assert.False(File.Exists(sizeMismatchPath));
        Assert.False(File.Exists(missingChecksumPath));
    }

    [Fact]
    public async Task RuntimePackageDownloadStopsBeforeUnknownLengthResponseExceedsExpectedSize()
    {
        var root = CreateTempRoot();
        var destination = Path.Combine(root, "oversized.zip");
        var bytes = Enumerable.Repeat((byte)7, 4096).ToArray();
        using var handler = new CapturingHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new UnknownLengthHttpContent(bytes)
        });
        using var http = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            RuntimePackageInstallFileService.DownloadAssetAsync(
                http,
                new RuntimePackageAsset("runtime.zip", "https://example.com/runtime.zip", 1024, Sha256: new string('a', 64)),
                destination,
                TestContext.Current.CancellationToken));

        Assert.Contains("exceeded", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task RuntimePackageDownloadUsesResponseLengthAndRejectsCompletelyUnknownSize()
    {
        var root = CreateTempRoot();
        var bytes = Encoding.UTF8.GetBytes("runtime package with response-derived size");
        var sha256 = Sha256Hex(bytes);
        var boundedDestination = Path.Combine(root, "bounded.zip");
        using (var boundedHandler = new CapturingHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        }))
        using (var boundedHttp = new HttpClient(boundedHandler))
        {
            await RuntimePackageInstallFileService.DownloadAssetAsync(
                boundedHttp,
                new RuntimePackageAsset("runtime.zip", "https://example.com/runtime.zip", 0, Sha256: sha256),
                boundedDestination,
                TestContext.Current.CancellationToken);
        }

        var unknownDestination = Path.Combine(root, "unknown.zip");
        using var unknownHandler = new CapturingHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new UnknownLengthHttpContent(bytes)
        });
        using var unknownHttp = new HttpClient(unknownHandler);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            RuntimePackageInstallFileService.DownloadAssetAsync(
                unknownHttp,
                new RuntimePackageAsset("runtime.zip", "https://example.com/runtime.zip", 0, Sha256: sha256),
                unknownDestination,
                TestContext.Current.CancellationToken));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(boundedDestination, TestContext.Current.CancellationToken));
        Assert.Contains("trustworthy download size", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(unknownDestination));
    }


    [Fact]
    public async Task RuntimePackageInstallServiceDownloadsExtractsStampsAndRegistersRuntime()
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
        var runtimes = new RuntimeRegistryService(store);
        var installer = new RuntimePackageInstallService(http, runtimes);
        var settings = AppSettings.CreateDefault(root);
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cpu");
        var progress = new List<RuntimePackageInstallProgress>();
        var logPath = Path.Combine(root, "logs", "runtime-package.log");

        var result = await installer.InstallAsync(new RuntimePackageInstallRequest(
            preset,
            settings,
            logPath,
            BoundedLogFile.MegabytesToBytes(1),
            progressItem =>
            {
                progress.Add(progressItem);
                return Task.CompletedTask;
            },
            CancellationToken: TestContext.Current.CancellationToken));
        var registered = Assert.Single(await store.ListRuntimesAsync());
        var log = await File.ReadAllTextAsync(logPath, TestContext.Current.CancellationToken);
        var verification = await RuntimeInstallationVerificationService.VerifyAsync(registered, TestContext.Current.CancellationToken);
        var provenance = RuntimeInstallationVerificationService.Describe(registered);
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.RuntimeFolder, "local-llm-runtime.json"),
            TestContext.Current.CancellationToken));

        Assert.Equal(Path.Combine(settings.RuntimeRoot, "official-prebuilt-windows-cpu-b9354"), result.InstallDir);
        Assert.Equal(result.InstallDir, result.RuntimeFolder);
        Assert.Equal(result.RuntimeFolder, RuntimeMetadataService.Folder(registered));
        Assert.Equal("b9354", result.UpdateState.LocalTag);
        Assert.Equal("package:b9354", result.UpdateState.LocalIdentity);
        Assert.Contains("installed from b9354", result.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(result.RuntimeFolder, "local-llm-runtime.json")));
        Assert.True(File.Exists(Path.Combine(result.RuntimeFolder, "bin", "llama-server.exe")));
        Assert.Contains(progress, item => item.Message.Contains("Resolving latest", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(progress, item => item.Message.Contains("Downloading llama-b9354", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Extracting llama-b9354-bin-win-cpu-x64.zip", log, StringComparison.Ordinal);
        Assert.True(verification.IsVerified);
        Assert.Equal(1, verification.VerifiedFiles);
        Assert.True(provenance.IsManaged);
        Assert.True(provenance.CanReverify);
        Assert.Equal("Local integrity checked", provenance.TrustStatus);
        Assert.Equal("b9354", provenance.ReleaseTag);
        Assert.Equal("9777256c3130", metadata?["targetCommit"]?.ToString());
        Assert.Contains("llama-b9354-bin-win-cpu-x64.zip", provenance.Assets, StringComparison.Ordinal);

        var unexpectedPath = Path.Combine(result.RuntimeFolder, "unexpected.dll");
        await File.WriteAllTextAsync(unexpectedPath, "unexpected", TestContext.Current.CancellationToken);
        var unexpected = await RuntimeInstallationVerificationService.VerifyAsync(registered, TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeInstallationVerificationStatus.Modified, unexpected.Status);
        Assert.Contains(unexpected.Problems, problem => problem.Contains("Unexpected", StringComparison.OrdinalIgnoreCase));
        File.Delete(unexpectedPath);

        await File.AppendAllTextAsync(
            Path.Combine(result.RuntimeFolder, "bin", "llama-server.exe"),
            "modified",
            TestContext.Current.CancellationToken);
        var modified = await RuntimeInstallationVerificationService.VerifyAsync(registered, TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeInstallationVerificationStatus.Modified, modified.Status);
        Assert.Contains(modified.Problems, problem => problem.Contains("changed", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task RuntimePackageInstallServiceCleansIncompleteInstallOnFailure()
    {
        var root = CreateTempRoot();
        var badArchiveBytes = System.Text.Encoding.UTF8.GetBytes("not a zip");
        var badArchiveSha256 = Sha256Hex(badArchiveBytes);
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
              "size": {{badArchiveBytes.Length}},
              "digest": "sha256:{{badArchiveSha256}}"
            }
          ]
        }
        """;
        using var handler = new CapturingHttpHandler(request =>
        {
            if (request.RequestUri?.ToString() == RuntimePackageSourceCatalog.LatestReleaseApiUrl)
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(releaseJson) };
            if (request.RequestUri?.ToString() == "https://example.com/win-cpu.zip")
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(badArchiveBytes) };
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var installer = new RuntimePackageInstallService(http, new RuntimeRegistryService(store));
        var settings = AppSettings.CreateDefault(root);
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cpu");
        var installDir = Path.Combine(settings.RuntimeRoot, "official-prebuilt-windows-cpu-b9354");

        await Assert.ThrowsAnyAsync<Exception>(() => installer.InstallAsync(new RuntimePackageInstallRequest(
            preset,
            settings,
            Path.Combine(root, "logs", "runtime-package.log"),
            BoundedLogFile.MegabytesToBytes(1),
            _ => Task.CompletedTask,
            CancellationToken: TestContext.Current.CancellationToken)));

        Assert.False(Directory.Exists(installDir));
        Assert.Empty(await store.ListRuntimesAsync());
    }


}
