using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using LocalLlmConsole.Localization;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeDownloadProjectionTests : ManagerRegressionTestBase
{
    [Fact]
    public void RuntimeDownloadsProjectSourceWorkflowAndVendorPlatformFilters()
    {
        var package = RuntimePackageSourceCatalog.PresetRows()
            .Single(candidate => candidate.Id == "official-prebuilt-windows-cuda");
        var build = RuntimeBuildCatalogService.DefaultPresets
            .Single(candidate => candidate.Id == package.SourcePresetId);
        var view = new RuntimeCatalogViewService(new RuntimePackageStatusService());

        var uncheckedRow = view.BuildPackageRows(
            [package], [build], [], [],
            new Dictionary<string, RuntimeUpdateState>(),
            new Dictionary<string, RuntimePackageUpdateState>()).Single(row => row.Preset?.Id == package.Id);
        var checkedState = new RuntimeUpdateState(true, "", "abcdef1234567890", DateTimeOffset.UtcNow);
        var checkedRows = view.BuildPackageRows(
            [package], [build], [], [],
            new Dictionary<string, RuntimeUpdateState> { [build.Id] = checkedState },
            new Dictionary<string, RuntimePackageUpdateState>());
        var checkedRow = checkedRows.Single(row => row.Preset?.Id == package.Id);
        var source = new RuntimeSourceEntry(
            build.Id,
            build.Label,
            build.RepoUrl,
            build.Branch,
            build.Cuda,
            Path.Combine("D:", "runtimes", "runtime-sources", build.Id),
            checkedState.RemoteCommit,
            DateTimeOffset.UtcNow,
            Mode: RuntimeMode.Native);
        var downloadedRow = view.BuildPackageRows(
            [package], [build], [], [source],
            new Dictionary<string, RuntimeUpdateState> { [build.Id] = checkedState },
            new Dictionary<string, RuntimePackageUpdateState>()).Single(row => row.Preset?.Id == package.Id);
        var packageRoot = CreateTempRoot();
        var packageFolder = Path.Combine(packageRoot, "package-runtime");
        var packageExe = Path.Combine(packageFolder, "llama-server.exe");
        Directory.CreateDirectory(packageFolder);
        File.WriteAllText(packageExe, "");
        var packageRuntime = new RuntimeRecord(
            "package-runtime",
            package.Label,
            RuntimeMode.Native,
            RuntimeBackend.Cuda,
            packageExe,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = packageFolder,
                managedPresetId = build.Id,
                managedPackageId = package.Id,
                releaseTag = "b10000"
            }),
            DateTimeOffset.UtcNow);
        var installedPackageRow = view.BuildPackageRows(
            [package], [build], [packageRuntime], [],
            new Dictionary<string, RuntimeUpdateState>(),
            new Dictionary<string, RuntimePackageUpdateState>()).Single(row => row.Preset?.Id == package.Id);
        var unmanagedRuntime = packageRuntime with
        {
            Id = "unmanaged-cuda-runtime",
            Name = "llama.cpp custom Native Cuda",
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { folder = packageFolder })
        };

        Assert.Equal(RuntimeSourceRowActionKind.Check, uncheckedRow.SourceActionKind);
        Assert.Equal("Check", uncheckedRow.BuildSourceAction);
        Assert.Equal(RuntimeSourceRowActionKind.Download, checkedRow.SourceActionKind);
        Assert.Equal("Download", checkedRow.BuildSourceAction);
        Assert.Equal(RuntimeSourceRowActionKind.Build, downloadedRow.SourceActionKind);
        Assert.Equal("Build", downloadedRow.BuildSourceAction);
        Assert.Same(source, downloadedRow.DownloadedSource);
        Assert.Equal(RuntimeInventoryFilterService.Nvidia, downloadedRow.Vendor);
        Assert.Equal(RuntimeInventoryFilterService.Windows, downloadedRow.Platform);
        Assert.Empty(RuntimeCatalogDataService.InstalledRuntimesForPreset([packageRuntime], build.Id));
        Assert.Empty(RuntimePackageInventoryPresenter.MatchingSourceBuilds([packageRuntime], package));
        Assert.Empty(RuntimeCatalogDataService.InstalledRuntimesForPreset([unmanagedRuntime], build.Id));
        Assert.False(RuntimeMetadataService.IsManagedSourceBuild(unmanagedRuntime));
        Assert.Equal(RuntimeSourceRowActionKind.Check, installedPackageRow.SourceActionKind);
        Assert.True(installedPackageRow.CanBuildSource);
        Assert.Equal(RuntimeDownloadDeleteKind.Package, installedPackageRow.DeleteKind);
        Assert.Equal("Delete All", installedPackageRow.DeleteAction);

        var downloads = new RuntimePackagesPageViewModel();
        downloads.ReplaceRows([
            downloadedRow,
            new RuntimePackagePresetRow { Label = "Vulkan", Vendor = RuntimeInventoryFilterService.Amd, Platform = RuntimeInventoryFilterService.Linux },
            new RuntimePackagePresetRow { Label = "SYCL", Vendor = RuntimeInventoryFilterService.Intel, Platform = RuntimeInventoryFilterService.Windows },
            new RuntimePackagePresetRow { Label = "Add", Vendor = RuntimeInventoryFilterService.All, Platform = RuntimeInventoryFilterService.All }
        ]);
        downloads.ApplyFilters(RuntimeInventoryFilterService.Amd, RuntimeInventoryFilterService.Linux);

        Assert.Equal(["Vulkan", "Add"], downloads.Rows.Select(row => row.Label).ToArray());
        Assert.Equal(RuntimeInventoryFilterService.Nvidia, RuntimeInventoryFilterService.Vendor(RuntimeBackend.Cuda));
        Assert.Equal(RuntimeInventoryFilterService.Amd, RuntimeInventoryFilterService.Vendor(RuntimeBackend.Vulkan));
        Assert.Equal(RuntimeInventoryFilterService.Intel, RuntimeInventoryFilterService.Vendor(RuntimeBackend.Sycl));
        Assert.Equal(RuntimeInventoryFilterService.Linux, RuntimeInventoryFilterService.Platform(RuntimeMode.Wsl));
    }

}
