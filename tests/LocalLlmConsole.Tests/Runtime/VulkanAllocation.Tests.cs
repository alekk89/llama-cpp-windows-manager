using System.Diagnostics;
using System.Text.Json;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class VulkanAllocationTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData(RuntimeMode.Native)]
    [InlineData(RuntimeMode.Wsl)]
    public void SupervisorAndToolLaunchesUseTheSameProfileEnvironment(RuntimeMode mode)
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { VulkanAllocationBlockSizeMiB = 4096 };
        var runtime = new RuntimeRecord("vk", "Vulkan", mode, RuntimeBackend.Vulkan,
            Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        using var supervisor = new LlamaProcessSupervisor(
            new WslRuntimeStopService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""))), new NativeRuntimeStopService());
        var server = supervisor.CreateProcessStartInfo(runtime, settings, runtime.ExecutablePath, ["--ctx-size", "8192"]);
        var tool = BenchmarkRuntimeToolAdapter.CreateStartInfo(runtime, "Ubuntu", runtime.ExecutablePath, ["--fit-ctx", "8192"], "", 4096);
        foreach (var start in new[] { server, tool })
        {
            if (mode == RuntimeMode.Native)
            {
                Assert.Equal("4294967296", start.Environment[RuntimeVulkanEnvironment.Variable]);
                Assert.DoesNotContain(start.ArgumentList, value => value.Contains(RuntimeVulkanEnvironment.Variable, StringComparison.Ordinal));
            }
            else Assert.Contains("export GGML_VK_SUBALLOCATION_BLOCK_SIZE=4294967296; ", start.ArgumentList[^1], StringComparison.Ordinal);
        }
        Assert.Equal(settings, ModelLaunchSettings.FromAppSettings(settings).ApplyTo(AppSettings.CreateDefault(root)));
    }

    [Theory]
    [InlineData(RuntimeBackend.Vulkan, 0)]
    [InlineData(RuntimeBackend.Cuda, 4096)]
    [InlineData(RuntimeBackend.Sycl, 4096)]
    [InlineData(RuntimeBackend.Cpu, 4096)]
    public void DefaultsAndOtherBackendsLeaveInheritedEnvironmentUntouched(RuntimeBackend backend, int size)
    {
        var start = new ProcessStartInfo();
        start.Environment[RuntimeVulkanEnvironment.Variable] = "inherited";
        RuntimeVulkanEnvironment.ApplyNative(start, backend, size);
        Assert.Equal("inherited", start.Environment[RuntimeVulkanEnvironment.Variable]);
        Assert.Equal("", RuntimeVulkanEnvironment.WslPrefix(backend, size));
        Assert.Throws<InvalidOperationException>(() => RuntimeVulkanEnvironment.Value(backend, -1));
        Assert.Equal("2251799812636672", RuntimeVulkanEnvironment.Value(RuntimeBackend.Vulkan, int.MaxValue));
    }

    [Fact]
    public async Task ProfilesPersistAndLegacyProfilesKeepTheRuntimeDefault()
    {
        var root = CreateTempRoot();
        var defaults = AppSettings.CreateDefault(root);
        var settings = ModelLaunchSettings.FromAppSettings(defaults) with { VulkanAllocationBlockSizeMiB = 4096 };
        await using var store = new StateStore(Path.Combine(root, "state", "test.db"));
        await store.InitializeAsync();
        await store.UpsertModelAsync(new ModelRecord("model", "Model", Path.Combine(root, "model.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow));
        await store.SaveNamedModelLaunchProfileAsync(new NamedModelLaunchProfile("profile", "model", "Vulkan", settings, DateTimeOffset.UtcNow));
        Assert.Equal(4096, (await store.GetNamedModelLaunchProfileAsync("profile"))!.Settings.VulkanAllocationBlockSizeMiB);
        await store.SaveAppSettingsAsync(settings.ApplyTo(defaults));
        Assert.Equal(4096, (await store.GetAppSettingsAsync(root)).VulkanAllocationBlockSizeMiB);
        var legacy = JsonSerializer.SerializeToNode(settings)!.AsObject();
        legacy.Remove(nameof(ModelLaunchSettings.VulkanAllocationBlockSizeMiB));
        var restored = legacy.Deserialize<ModelLaunchSettings>()!;
        Assert.Equal(0, restored.VulkanAllocationBlockSizeMiB);
        Assert.Equal(0, restored.ApplyTo(settings.ApplyTo(defaults)).VulkanAllocationBlockSizeMiB);
    }

    [Theory]
    [InlineData(RuntimeMode.Native)]
    [InlineData(RuntimeMode.Wsl)]
    public async Task FittingAppliesSavedAllocationOverride(RuntimeMode mode)
    {
        var root = CreateTempRoot();
        var runtime = new RuntimeRecord("vk", "Vulkan", mode, RuntimeBackend.Vulkan,
            Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(ProfileFitCapabilityService.ResolveExecutable(runtime), "fixture", TestContext.Current.CancellationToken);
        ProcessStartInfo? fitStart = null;
        var runner = new ScriptedProcessRunner(start =>
        {
            if (string.Join(' ', start.ArgumentList).Contains("--help", StringComparison.Ordinal))
                return new ProcessRunResult(0, "--fit-target --fit-ctx", "");
            fitStart = start;
            return new ProcessRunResult(0, "-c 8192 -ngl 99", "");
        });
        var service = new ProfileFitService(runner, new ProfileFitCapabilityService(runner));
        var current = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root)) with { VulkanAllocationBlockSizeMiB = 4096 };
        var result = await service.FitAsync(new ProfileFitRequest("model.gguf", runtime, current, 8192, 4096, [1024], WslDistro: "Ubuntu"), TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.Error);
        Assert.Equal(4096, current.VulkanAllocationBlockSizeMiB);
        Assert.NotNull(fitStart);
        if (mode == RuntimeMode.Native) Assert.Equal("4294967296", fitStart.Environment[RuntimeVulkanEnvironment.Variable]);
        else Assert.Contains("GGML_VK_SUBALLOCATION_BLOCK_SIZE=4294967296", fitStart.ArgumentList[^1], StringComparison.Ordinal);
    }
}
