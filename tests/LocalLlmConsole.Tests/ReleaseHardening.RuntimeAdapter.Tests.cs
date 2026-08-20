using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void RuntimeAdapterRejectsNetworkHostWithoutLanMode()
    {
        var request = ValidLaunchRequest() with { Host = "0.0.0.0" };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("localhost", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterAllowsNetworkHostWithExplicitLanModeAndApiKey()
    {
        var apiKey = new string('a', 32);
        var request = ValidLaunchRequest() with
        {
            Host = "0.0.0.0",
            AllowNetworkAccess = true,
            ApiKey = apiKey
        };

        var result = RuntimeAdapter.Validate(request);
        var args = RuntimeAdapter.BuildArgs(request);

        Assert.True(result.Ok);
        Assert.Contains("0.0.0.0", args);
        Assert.DoesNotContain("--api-key", args);
        Assert.Equal(apiKey, request.ApiKey);
    }


    [Fact]
    public void RuntimeAdapterRequiresApiKeyForModelServing()
    {
        var request = ValidLaunchRequest() with
        {
            Host = "0.0.0.0",
            AllowNetworkAccess = true,
            RequireApiKeyAuth = false,
            ApiKey = ""
        };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("API key", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterRejectsWeakApiKey()
    {
        var request = ValidLaunchRequest() with { ApiKey = "test-key" };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("32", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterRejectsExtremeLaunchValues()
    {
        var request = ValidLaunchRequest() with
        {
            ContextSize = int.MaxValue,
            BatchSize = int.MaxValue,
            MicroBatchSize = int.MaxValue,
            Threads = int.MaxValue
        };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("Context", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Batch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Threads", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterBuildsLocalOnlyArgs()
    {
        var args = RuntimeAdapter.BuildArgs(ValidLaunchRequest());

        Assert.Contains("--host", args);
        Assert.Contains("127.0.0.1", args);
        Assert.Contains("--port", args);
        Assert.Contains("8081", args);
        Assert.DoesNotContain("--api-key", args);
        Assert.DoesNotContain("--cache-ram", args);
        Assert.DoesNotContain("--ctx-checkpoints", args);
        Assert.DoesNotContain("--checkpoint-min-step", args);
        Assert.DoesNotContain("--reasoning-effort", args);
        Assert.DoesNotContain("--reasoning-budget-message", args);
        Assert.DoesNotContain("--reasoning-preserve", args);
        Assert.DoesNotContain("--no-reasoning-preserve", args);
    }

    [Fact]
    public void RuntimeAdapterBuildsCurrentReasoningControls()
    {
        const string budgetMessage = "Conclude the reasoning and provide the final answer.";
        var args = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            ReasoningEffort = "high",
            ReasoningBudget = 16_384,
            ReasoningBudgetMessage = budgetMessage,
            ReasoningPreserve = "on"
        });

        Assert.Equal("high", args[args.ToList().IndexOf("--reasoning-effort") + 1]);
        Assert.Equal("16384", args[args.ToList().IndexOf("--reasoning-budget") + 1]);
        Assert.Equal(budgetMessage, args[args.ToList().IndexOf("--reasoning-budget-message") + 1]);
        Assert.Contains("--reasoning-preserve", args);

        var noPreserve = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with { ReasoningPreserve = "off" });
        Assert.Contains("--no-reasoning-preserve", noPreserve);
    }

    [Fact]
    public void ReasoningControlsRoundTripThroughSavedLaunchProfiles()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        var edited = defaults with
        {
            ReasoningEffort = "xhigh",
            ReasoningBudget = 16_384,
            ReasoningBudgetMessage = "Finish reasoning now.",
            ReasoningPreserve = "off"
        };

        var profile = ModelLaunchSettings.FromAppSettings(edited, "runtime-current");
        var restored = profile.ApplyTo(defaults);

        Assert.Equal("xhigh", profile.ReasoningEffort);
        Assert.Equal(16_384, restored.ReasoningBudget);
        Assert.Equal("Finish reasoning now.", restored.ReasoningBudgetMessage);
        Assert.Equal("off", restored.ReasoningPreserve);
    }

    [Fact]
    public void RuntimeAdapterRejectsInvalidReasoningControls()
    {
        var result = RuntimeAdapter.Validate(ValidLaunchRequest() with
        {
            ReasoningEffort = "ultra",
            ReasoningBudgetMessage = new string('x', 4097),
            ReasoningPreserve = "sometimes"
        });

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("effort", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("4096", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Preserve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimeAdapterBuildsSingleAndMultiGpuSelectionArgs()
    {
        var autoArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Cuda,
            GpuLayers = 999
        });
        Assert.DoesNotContain("--split-mode", autoArgs);
        Assert.DoesNotContain("--device", autoArgs);
        Assert.DoesNotContain("--tensor-split", autoArgs);

        var singleArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Cuda,
            GpuLayers = 999,
            GpuMode = "single",
            GpuDevices = "CUDA1"
        });
        Assert.Equal("none", singleArgs[singleArgs.ToList().IndexOf("--split-mode") + 1]);
        Assert.Equal("CUDA1", singleArgs[singleArgs.ToList().IndexOf("--device") + 1]);

        var layerArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Cuda,
            GpuLayers = 999,
            GpuMode = "layer",
            GpuDevices = " CUDA0, CUDA1, CUDA2 ",
            GpuSplit = " 2, 1, 1 "
        });
        Assert.Equal("layer", layerArgs[layerArgs.ToList().IndexOf("--split-mode") + 1]);
        Assert.Equal("CUDA0,CUDA1,CUDA2", layerArgs[layerArgs.ToList().IndexOf("--device") + 1]);
        Assert.Equal("2,1,1", layerArgs[layerArgs.ToList().IndexOf("--tensor-split") + 1]);

        var tensorArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Cuda,
            GpuLayers = 999,
            GpuMode = "tensor",
            GpuDevices = "CUDA0,CUDA1",
            GpuSplit = "1,1"
        });
        Assert.Equal("tensor", tensorArgs[tensorArgs.ToList().IndexOf("--split-mode") + 1]);
        Assert.Equal("1,1", tensorArgs[tensorArgs.ToList().IndexOf("--tensor-split") + 1]);
    }

    [Fact]
    public void RuntimeAdapterValidatesGpuSelectionSettings()
    {
        Assert.False(RuntimeAdapter.Validate(ValidLaunchRequest() with
        {
            GpuMode = "single",
            GpuDevices = "CUDA0,CUDA1"
        }).Ok);
        Assert.False(RuntimeAdapter.Validate(ValidLaunchRequest() with
        {
            GpuMode = "layer",
            GpuDevices = "CUDA0,CUDA1",
            GpuSplit = "1"
        }).Ok);
        Assert.False(RuntimeAdapter.Validate(ValidLaunchRequest() with
        {
            GpuMode = "tensor",
            GpuSplit = "0,0"
        }).Ok);
        Assert.False(RuntimeAdapter.Validate(ValidLaunchRequest() with
        {
            GpuMode = "unsupported"
        }).Ok);
    }

    [Fact]
    public void CustomLaunchParameterParserPreservesQuotedWindowsPathsAndEscapes()
    {
        var args = CustomLaunchParameterParser.Parse("""--n-cpu-moe 999 --device-draft CUDA1 --model-draft "D:\Models\draft model.gguf" --flag\ with\ spaces 'single quoted value'""");

        Assert.Equal([
            "--n-cpu-moe",
            "999",
            "--device-draft",
            "CUDA1",
            "--model-draft",
            @"D:\Models\draft model.gguf",
            "--flag with spaces",
            "single quoted value"
        ], args);
        Assert.Empty(CustomLaunchParameterParser.Parse(""));
        Assert.Contains("unterminated quote", Assert.Throws<InvalidOperationException>(() =>
            CustomLaunchParameterParser.Parse("\"oops")).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeAdapterAppendsCustomExtraArgs()
    {
        var args = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            ExtraArgs = CustomLaunchParameterParser.Parse("--n-cpu-moe 999 --device-draft CUDA1 --model-draft \"D:\\Models\\draft model.gguf\"")
        });

        Assert.Equal([
            "--n-cpu-moe",
            "999",
            "--device-draft",
            "CUDA1",
            "--model-draft",
            @"D:\Models\draft model.gguf"
        ], args.TakeLast(6).ToArray());
    }

    [Fact]
    public void RuntimeAdapterBuildsPromptCacheAndCheckpointArgs()
    {
        var onArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            PromptCacheMode = "on",
            PromptCacheRamMb = 16_384,
            ContextCheckpointsMode = "on",
            ContextCheckpointCount = 48,
            ContextCheckpointEveryNTokens = 512
        });
        var offArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            PromptCacheMode = "off",
            ContextCheckpointsMode = "off"
        });

        Assert.Contains("--cache-ram", onArgs);
        Assert.Contains("16384", onArgs);
        Assert.Contains("--ctx-checkpoints", onArgs);
        Assert.Contains("48", onArgs);
        Assert.Contains("--checkpoint-min-step", onArgs);
        Assert.Contains("512", onArgs);

        Assert.Contains("--cache-ram", offArgs);
        Assert.Contains("0", offArgs);
        Assert.Contains("--ctx-checkpoints", offArgs);
        Assert.DoesNotContain("--checkpoint-min-step", offArgs);
    }


    [Fact]
    public void RuntimeAdapterTreatsSyclAsGpuBackend()
    {
        var onArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Sycl,
            GpuLayers = 99,
            MmapMode = "on"
        });
        var offArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            Backend = RuntimeBackend.Sycl,
            GpuLayers = 88,
            MmapMode = "off"
        });

        Assert.Contains("--n-gpu-layers", onArgs);
        Assert.Contains("99", onArgs);
        Assert.Contains("--mmap", onArgs);
        Assert.Contains("--n-gpu-layers", offArgs);
        Assert.Contains("88", offArgs);
        Assert.Contains("--no-mmap", offArgs);
    }


    [Fact]
    public void RuntimeAdapterTreatsMetalAsGpuBackend()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeAdapter.cs"));

        Assert.Contains("request.Backend is RuntimeBackend.Cuda or RuntimeBackend.Vulkan or RuntimeBackend.Metal or RuntimeBackend.Sycl", source, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeAdapterValidatesVisionProjectorPairing()
    {
        var missing = RuntimeAdapter.Validate(ValidLaunchRequest() with { VisionMode = "on", VisionProjectorPath = "" });

        Assert.False(missing.Ok);
        Assert.Contains(missing.Errors, error => error.Contains("mmproj", StringComparison.OrdinalIgnoreCase));

        var args = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            VisionMode = "on",
            VisionProjectorPath = "mmproj.gguf",
            VisionImageMinTokens = 256,
            VisionImageMaxTokens = 1024
        });
        Assert.Contains("--mmproj", args);
        Assert.Contains("mmproj.gguf", args);
        Assert.Contains("--image-min-tokens", args);
        Assert.Contains("256", args);
        Assert.Contains("--image-max-tokens", args);
        Assert.Contains("1024", args);

        var embeddedArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            VisionMode = "on",
            VisionProjectorEmbedded = true,
            VisionImageMinTokens = 128
        });
        Assert.DoesNotContain("--mmproj", embeddedArgs);
        Assert.Contains("--image-min-tokens", embeddedArgs);

        var offArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with { VisionMode = "off" });
        Assert.Contains("--no-mmproj", offArgs);

        var invalid = RuntimeAdapter.Validate(ValidLaunchRequest() with { VisionImageMinTokens = 2048, VisionImageMaxTokens = 1024 });
        Assert.False(invalid.Ok);
        Assert.Contains(invalid.Errors, error => error.Contains("Image min tokens", StringComparison.OrdinalIgnoreCase));
    }


}
