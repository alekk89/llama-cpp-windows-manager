using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ProfileFittingTests : ManagerRegressionTestBase
{
    [Fact]
    public void FitOutputMapsGeneratedArgumentsToTypedProfileSettings()
    {
        var root = CreateTempRoot();
        var current = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root)) with
        {
            ContextSize = 196_608,
            GpuLayers = 999,
            GpuSplit = "50,50"
        };
        var runtime = new RuntimeRecord("cuda", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda,
            Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var request = new ProfileFitRequest("model.gguf", runtime, current, 160_000, 65_536, [1536, 1536]);

        var result = ProfileFitOutputParser.Parse(
            request,
            "some output\n-c 180000 -ngl 99 -ts 49,51 -ot \"blk\\.12\\..*=CPU\"\n",
            "CUDA0 projected: 22000 MiB used, 1600 MiB free\nCUDA1 projected: 21900 MiB used, 1700 MiB free");

        Assert.True(result.Success);
        Assert.NotNull(result.Proposal);
        Assert.Equal(160_000, result.Proposal.ContextSize);
        Assert.Equal(99, result.Proposal.GpuLayers);
        Assert.Equal("49,51", result.Proposal.GpuSplit);
        Assert.Equal("blk\\.12\\..*=CPU", result.Proposal.TensorBufferOverrides);
        Assert.Equal(2, result.DeviceEstimates.Count);
        Assert.Contains("Context was reduced.", result.Warnings);
    }

    [Fact]
    public void FitOutputRejectsProposalBelowRequiredMinimumContext()
    {
        var root = CreateTempRoot();
        var current = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root));
        var runtime = new RuntimeRecord("cuda", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda,
            Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var request = new ProfileFitRequest("model.gguf", runtime, current, 131_072, 65_536, [1536]);

        var result = ProfileFitOutputParser.Parse(request, "-c 32768 -ngl 80", "");

        Assert.False(result.Success);
        Assert.Contains("below the requested minimum", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TypedTensorBufferOverridesReachServerArguments()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with
        {
            TensorBufferOverrides = "blk\\.12\\..*=CPU",
            RequireApiKeyAuth = false
        };
        var request = RuntimeLaunchRequestFactory.Create(settings, new RuntimeLaunchRequestContext(
            RuntimeMode.Native,
            RuntimeBackend.Cuda,
            "llama-server.exe",
            "model.gguf",
            "127.0.0.1",
            false));

        var arguments = LlamaCppArgumentBuilder.Build(request);

        var index = arguments.ToList().IndexOf("--override-tensor");
        Assert.True(index >= 0);
        Assert.Equal(settings.TensorBufferOverrides, arguments[index + 1]);
    }

    [Theory]
    [InlineData("CUDA error: out of memory")]
    [InlineData("failed to allocate CUDA buffer of size 1234")]
    [InlineData("VK_ERROR_OUT_OF_DEVICE_MEMORY")]
    public void OomClassifierRecognizesGpuAllocationFailures(string message)
        => Assert.True(RuntimeOutOfMemoryClassifier.IsOutOfMemory("", message));

    [Fact]
    public void OomClassifierDoesNotTreatUnrelatedStartupFailuresAsOom()
        => Assert.False(RuntimeOutOfMemoryClassifier.IsOutOfMemory("port is already in use", "bind failed"));

    [Fact]
    public void ServingBenchmarkResultKeepsObservedVramAndTypedTensorOverrides()
    {
        const string json = """
            {"n_prompt":512,"n_gen":128,"avg_ts":42.5,"gpu_memory_used_mib":23117,"tensor_buffer_overrides":"blk\\.12\\..*=CPU"}
            """;

        var parsed = BenchmarkResultService.TryParse(
            json, "model", "profile", RuntimeMode.Native, RuntimeBackend.Cuda, out var result, out var error);

        Assert.True(parsed, error);
        Assert.NotNull(result);
        Assert.Equal(23_117, result.ObservedGpuMemoryUsedMiB);
        Assert.Equal("blk\\.12\\..*=CPU", result.TensorBufferOverrides);
    }
}
