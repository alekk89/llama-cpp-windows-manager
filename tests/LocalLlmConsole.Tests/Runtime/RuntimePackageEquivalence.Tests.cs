using System.Diagnostics;
using System.Text;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimePackageEquivalenceTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task RuntimeEquivalenceServiceLinksSourceBuildAndPrebuiltByFingerprint()
    {
        var root = CreateTempRoot();
        var packageFolder = Path.Combine(root, "runtimes", "official-prebuilt-windows-cuda-b9354");
        var sourceFolder = Path.Combine(root, "runtimes", "official-windows-cuda-20260527");
        Directory.CreateDirectory(Path.Combine(packageFolder, "bin"));
        Directory.CreateDirectory(Path.Combine(sourceFolder, "bin"));
        await File.WriteAllTextAsync(Path.Combine(packageFolder, "bin", "llama-server.exe"), "same binary", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sourceFolder, "bin", "llama-server.exe"), "same binary", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var packageRuntime = new RuntimeRecord(
            "package-runtime",
            "Official llama.cpp CUDA Windows",
            RuntimeMode.Native,
            RuntimeBackend.Cuda,
            Path.Combine(packageFolder, "bin", "llama-server.exe"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = packageFolder,
                runtimeMetadata = new
                {
                    managedPackageId = "official-prebuilt-windows-cuda",
                    managedPresetId = "official-prebuilt-windows-cuda",
                    releaseTag = "b9354"
                }
            }),
            now);
        var sourceRuntime = new RuntimeRecord(
            "source-runtime",
            "Official llama.cpp CUDA Windows Source",
            RuntimeMode.Native,
            RuntimeBackend.Cuda,
            Path.Combine(sourceFolder, "bin", "llama-server.exe"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = sourceFolder,
                runtimeMetadata = new
                {
                    managedPresetId = "official-windows-cuda",
                    commit = "9777256c3130"
                }
            }),
            now);
        await store.UpsertRuntimeAsync(packageRuntime);
        await store.UpsertRuntimeAsync(sourceRuntime);

        Assert.True(await RuntimeEquivalenceService.ReconcileOfficialRuntimeEquivalenceAsync(store, await store.ListRuntimesAsync(), TestContext.Current.CancellationToken));
        var runtimes = await store.ListRuntimesAsync();
        var reconciledSource = runtimes.Single(runtime => runtime.Id == sourceRuntime.Id);
        var reconciledPackage = runtimes.Single(runtime => runtime.Id == packageRuntime.Id);
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cuda");

        Assert.Equal(RuntimeMetadataService.RuntimeFingerprint(reconciledPackage), RuntimeMetadataService.RuntimeFingerprint(reconciledSource));
        Assert.Contains(preset.Id, RuntimeMetadataService.EquivalentPackageIds(reconciledSource));
        Assert.Contains(preset.SourcePresetId, RuntimeMetadataService.EquivalentSourcePresetIds(reconciledPackage));
        Assert.Contains(reconciledSource, RuntimePackageInventoryPresenter.InstalledPackages(runtimes, preset));
    }


    [Fact]
    public void RuntimePackageInventoryPresenterReportsSourceBuildCandidates()
    {
        var root = CreateTempRoot();
        var runtime = new RuntimeRecord(
            "source-runtime",
            "Official llama.cpp CUDA Windows",
            RuntimeMode.Native,
            RuntimeBackend.Cuda,
            Path.Combine(root, "runtime", "bin", "llama-server.exe"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = Path.Combine(root, "runtime"),
                runtimeMetadata = new
                {
                    managedPresetId = "official-windows-cuda",
                    commit = "9777256c3130"
                }
            }),
            DateTimeOffset.UtcNow);
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cuda");

        var sourceBuilds = RuntimePackageInventoryPresenter.MatchingSourceBuilds([runtime], preset);

        Assert.Single(sourceBuilds);
        Assert.Equal("Built from source", RuntimePackageInventoryPresenter.LocalStatusLabel([], sourceBuilds));
        Assert.Equal("source:9777256c3130", RuntimePackageInventoryPresenter.LocalIdentity([], sourceBuilds));
        Assert.Contains("source built", RuntimePackageInventoryPresenter.LatestLocalLabel([], sourceBuilds, null), StringComparison.OrdinalIgnoreCase);
    }

}
