using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeRegistryAndValidationTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task RuntimeRegistryScanRegistersRuntimeOnceWhenRootContainsExecutable()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        Directory.CreateDirectory(runtimeRoot);
        await File.WriteAllTextAsync(Path.Combine(runtimeRoot, "llama-server.exe"), "fake exe", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var registry = new RuntimeRegistryService(store);

        var count = await registry.ScanAsync(runtimeRoot);
        var runtimes = await store.ListRuntimesAsync();

        Assert.Equal(1, count);
        var runtime = Assert.Single(runtimes);
        Assert.Equal(RuntimeMode.Native, runtime.Mode);
        Assert.Equal(RuntimeBackend.Cpu, runtime.Backend);
        Assert.Equal(Path.Combine(runtimeRoot, "llama-server.exe"), runtime.ExecutablePath);
    }


    [Fact]
    public async Task RuntimeRegistryScanRepairsExecutableMovedInsideItsRecordedFolderWithoutDuplicatingRegistration()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var runtimeFolder = Path.Combine(runtimeRoot, "official-cpu");
        var binFolder = Path.Combine(runtimeFolder, "bin");
        Directory.CreateDirectory(binFolder);
        var movedExecutable = Path.Combine(binFolder, "llama-server.exe");
        await File.WriteAllTextAsync(movedExecutable, "fake exe", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var stale = new RuntimeRecord(
            "custom-runtime-id",
            "My official CPU runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(runtimeFolder, "llama-server.exe"),
            System.Text.Json.JsonSerializer.Serialize(new { folder = runtimeFolder }),
            DateTimeOffset.UtcNow.AddDays(-1));
        await store.UpsertRuntimeAsync(stale);

        var count = await new RuntimeRegistryService(store).ScanAsync(runtimeRoot);
        var repaired = Assert.Single(await store.ListRuntimesAsync());

        Assert.Equal(1, count);
        Assert.Equal(stale.Id, repaired.Id);
        Assert.Equal(stale.Name, repaired.Name);
        Assert.Equal(movedExecutable, repaired.ExecutablePath);
        Assert.True(RuntimeAvailabilityService.IsAvailable(repaired));
        Assert.True(repaired.UpdatedAt > stale.UpdatedAt);
    }


    [Fact]
    public async Task RuntimeRegistryInfersCudaFromNearbyRuntimeFiles()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var buildRoot = Path.Combine(runtimeRoot, "cuda-build");
        var binRoot = Path.Combine(buildRoot, "bin");
        var libRoot = Path.Combine(buildRoot, "lib");
        Directory.CreateDirectory(binRoot);
        Directory.CreateDirectory(libRoot);
        await File.WriteAllTextAsync(Path.Combine(binRoot, "llama-server"), "fake wsl binary", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(libRoot, "libcudart.so"), "fake cuda lib", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var registry = new RuntimeRegistryService(store);

        var count = await registry.ScanAsync(runtimeRoot);
        var runtime = Assert.Single(await store.ListRuntimesAsync());

        Assert.Equal(1, count);
        Assert.Equal(RuntimeMode.Wsl, runtime.Mode);
        Assert.Equal(RuntimeBackend.Cuda, runtime.Backend);
        Assert.Equal(Path.Combine(binRoot, "llama-server"), runtime.ExecutablePath);
    }


    [Fact]
    public async Task RuntimeRegistryDoesNotInferGpuBackendFromLooseFolderText()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var buildRoot = Path.Combine(runtimeRoot, "cuda-backup-notes");
        Directory.CreateDirectory(buildRoot);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "llama-server.exe"), "fake native binary", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var registry = new RuntimeRegistryService(store);

        var count = await registry.ScanAsync(runtimeRoot);
        var runtime = Assert.Single(await store.ListRuntimesAsync());

        Assert.Equal(1, count);
        Assert.Equal(RuntimeBackend.Cpu, runtime.Backend);
    }


    [Fact]
    public async Task RuntimeRegistryHonorsExplicitPackagedBackendMetadata()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var buildRoot = Path.Combine(runtimeRoot, "plain-runtime");
        Directory.CreateDirectory(buildRoot);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "llama-server.exe"), "fake native binary", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "local-llm-runtime.json"), """{"backend":"sycl"}""", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var registry = new RuntimeRegistryService(store);

        var count = await registry.ScanAsync(runtimeRoot);
        var runtime = Assert.Single(await store.ListRuntimesAsync());

        Assert.Equal(1, count);
        Assert.Equal(RuntimeBackend.Sycl, runtime.Backend);
    }


    [Fact]
    public void LlamaCppLaunchRejectsInvalidSpeculativeSettings()
    {
        var request = ValidLaunchRequest() with
        {
            SpeculativeType = "maybe-mtp",
            SpecDraftMinTokens = 8,
            SpecDraftMaxTokens = 4,
            SpecDraftPSplit = 2,
            PromptCacheMode = "maybe",
            ContextCheckpointsMode = "maybe",
            RopeScaling = "banana"
        };

        var result = LlamaCppLaunchValidator.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("Speculative type", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Draft min tokens", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Draft split", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Prompt cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("checkpoints", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("RoPE", StringComparison.OrdinalIgnoreCase));
    }


}
