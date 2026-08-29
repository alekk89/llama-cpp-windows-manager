using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class LlamaCppArgumentGoldenTests : ManagerRegressionTestBase
{
    [Theory]
    [MemberData(nameof(RepresentativeLaunches))]
    public void RepresentativeLaunchesPreserveTheCompleteOrderedArgumentList(
        string scenario,
        RuntimeLaunchRequest request,
        string[] expected)
    {
        var arguments = LlamaCppArgumentBuilder.Build(request);

        Assert.True(LlamaCppLaunchValidator.Validate(request).Ok, scenario);
        Assert.Equal(expected, arguments);
    }

    public static IEnumerable<object[]> RepresentativeLaunches()
    {
        yield return Row("CPU native", ValidLaunchRequest(), Expected());
        yield return Row("CUDA single GPU", ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Cuda,
            GpuLayers = 99,
            GpuMode = "single",
            GpuDevices = "CUDA1"
        }, Expected(compute: ["--n-gpu-layers", "99", "--split-mode", "none", "--device", "CUDA1"]));
        yield return Row("CUDA tensor split", ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Cuda,
            GpuLayers = 88,
            GpuMode = "tensor",
            GpuDevices = "CUDA0, CUDA1",
            GpuSplit = "2, 1"
        }, Expected(compute:
        [
            "--n-gpu-layers", "88",
            "--split-mode", "tensor",
            "--device", "CUDA0,CUDA1",
            "--tensor-split", "2,1"
        ]));
        yield return Row("Vulkan", ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Vulkan,
            GpuLayers = 77,
            GpuDevices = "Vulkan0"
        }, Expected(compute: ["--n-gpu-layers", "77", "--device", "Vulkan0"]));
        yield return Row("SYCL", ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Sycl,
            GpuLayers = 66,
            GpuDevices = "SYCL0"
        }, Expected(compute: ["--n-gpu-layers", "66", "--device", "SYCL0"]));
        yield return Row("WSL", ValidLaunchRequest() with
        {
            Mode = RuntimeMode.Wsl,
            WslDistro = "Ubuntu-24.04",
            ExecutablePath = "/usr/local/bin/llama-server"
        }, Expected());
        yield return Row("authenticated localhost", ValidLaunchRequest(), Expected());
        yield return Row("authenticated LAN", ValidLaunchRequest() with
        {
            Host = "0.0.0.0",
            AllowNetworkAccess = true
        }, Expected(host: "0.0.0.0"));
        yield return Row("unauthenticated localhost", ValidLaunchRequest() with
        {
            RequireApiKeyAuth = false,
            ApiKey = ""
        }, Expected());
        yield return Row("vision projector", ValidLaunchRequest() with
        {
            VisionMode = "on",
            VisionProjectorPath = "mmproj.gguf",
            VisionImageMinTokens = 256,
            VisionImageMaxTokens = 1024
        }, Expected(afterContinuous:
        [
            "--mmproj", "mmproj.gguf",
            "--image-min-tokens", "256",
            "--image-max-tokens", "1024"
        ]));
        yield return Row("embedded vision", ValidLaunchRequest() with
        {
            VisionMode = "on",
            VisionProjectorEmbedded = true,
            VisionImageMinTokens = 128
        }, Expected(afterContinuous: ["--image-min-tokens", "128"]));
        yield return Row("draft model", ValidLaunchRequest() with
        {
            SpeculativeType = "draft-simple",
            SpecDraftModelPath = "draft.gguf",
            SpecDraftGpuLayers = 42,
            SpecDraftMinTokens = 2,
            SpecDraftMaxTokens = 8,
            SpecDraftPSplit = 0.4,
            SpecDraftPMin = 0.2,
            SpecDraftCacheTypeK = "q4_0",
            SpecDraftCacheTypeV = "f16"
        }, Expected(afterContinuous:
        [
            "--spec-type", "draft-simple",
            "--model-draft", "draft.gguf",
            "--n-gpu-layers-draft", "42",
            "--spec-draft-n-min", "2",
            "--spec-draft-n-max", "8",
            "--spec-draft-p-split", "0.4",
            "--spec-draft-p-min", "0.2",
            "--cache-type-k-draft", "q4_0",
            "--cache-type-v-draft", "f16"
        ]));
        yield return Row("atomic MTP head", ValidLaunchRequest() with
        {
            SpeculativeType = "atomic-mtp",
            MtpHeadPath = "mtp-head.gguf"
        }, Expected(afterContinuous: ["--spec-type", "mtp", "--mtp-head", "mtp-head.gguf"]));
        yield return Row("prompt cache and checkpoints", ValidLaunchRequest() with
        {
            PromptCacheMode = "on",
            PromptCacheRamMb = 16_384,
            ContextCheckpointsMode = "on",
            ContextCheckpointCount = 48,
            ContextCheckpointEveryNTokens = 512
        }, Expected(beforeContinuous:
        [
            "--cache-ram", "16384",
            "--ctx-checkpoints", "48",
            "--checkpoint-min-step", "512"
        ]));
        yield return Row("custom parameters", ValidLaunchRequest() with
        {
            ExtraArgs = ["--n-cpu-moe", "999", "--custom-flag"]
        }, Expected(afterContinuous: ["--n-cpu-moe", "999", "--custom-flag"]));
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("endpoint")]
    [InlineData("compute")]
    [InlineData("context")]
    [InlineData("cache")]
    [InlineData("sampling")]
    [InlineData("reasoning")]
    [InlineData("vision")]
    [InlineData("speculation")]
    public void ValidationGroupsRejectRepresentativeInvalidValues(string group)
    {
        var request = group switch
        {
            "platform" => ValidLaunchRequest() with { Mode = RuntimeMode.Wsl, WslDistro = "" },
            "endpoint" => ValidLaunchRequest() with { Port = 0 },
            "compute" => ValidLaunchRequest() with { GpuLayers = -1 },
            "context" => ValidLaunchRequest() with { ContextSize = 128 },
            "cache" => ValidLaunchRequest() with { CacheTypeK = "invalid" },
            "sampling" => ValidLaunchRequest() with { TopP = 2 },
            "reasoning" => ValidLaunchRequest() with { ReasoningEffort = "ultra" },
            "vision" => ValidLaunchRequest() with { VisionMode = "on", VisionProjectorPath = "" },
            "speculation" => ValidLaunchRequest() with { SpeculativeType = "atomic-mtp", MtpHeadPath = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, null)
        };

        Assert.False(LlamaCppLaunchValidator.Validate(request).Ok);
    }

    [Fact]
    public void TransitionalRuntimeAdapterDelegatesWithoutChangingBehavior()
    {
#pragma warning disable CS0618
        Assert.Equal(
            LlamaCppLaunchValidator.Validate(ValidLaunchRequest()),
            RuntimeAdapter.Validate(ValidLaunchRequest()));
        Assert.Equal(
            LlamaCppArgumentBuilder.Build(ValidLaunchRequest()),
            RuntimeAdapter.BuildArgs(ValidLaunchRequest()));
#pragma warning restore CS0618
    }

    private static object[] Row(string scenario, RuntimeLaunchRequest request, string[] expected)
        => [scenario, request, expected];

    private static string[] Expected(
        string host = "127.0.0.1",
        IReadOnlyList<string>? compute = null,
        IReadOnlyList<string>? beforeContinuous = null,
        IReadOnlyList<string>? afterContinuous = null)
    {
        var result = new List<string>
        {
            "--model", "model.gguf",
            "--host", host,
            "--port", "8081",
            "--ctx-size", "0"
        };
        result.AddRange(compute ?? []);
        result.AddRange([
            "--parallel", "1",
            "--batch-size", "4096",
            "--ubatch-size", "512",
            "--flash-attn", "auto",
            "--cache-type-k", "q8_0",
            "--cache-type-v", "q8_0",
            "--temp", "0.65",
            "--top-k", "40",
            "--top-p", "0.95",
            "--min-p", "0.05",
            "--repeat-last-n", "64",
            "--repeat-penalty", "1",
            "--presence-penalty", "0",
            "--frequency-penalty", "0"
        ]);
        result.AddRange(beforeContinuous ?? []);
        result.Add("--cont-batching");
        result.AddRange(afterContinuous ?? []);
        return result.ToArray();
    }
}
