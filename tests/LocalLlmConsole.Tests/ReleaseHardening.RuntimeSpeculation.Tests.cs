using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void RuntimeAdapterBuildsSpeculativeSamplingAndRopeArgs()
    {
        var request = ValidLaunchRequest() with
        {
            SpeculativeType = "draft-mtp",
            SpecDraftModelPath = "draft.gguf",
            SpecDraftGpuLayers = 999,
            SpecDraftMinTokens = 1,
            SpecDraftMaxTokens = 4,
            SpecDraftPSplit = 0.2,
            SpecDraftPMin = 0.05,
            SpecDraftCacheTypeK = "q8_0",
            SpecDraftCacheTypeV = "q8_0",
            MaxTokens = 512,
            Seed = 1234,
            RepeatLastN = 128,
            RepeatPenalty = 1.08,
            PresencePenalty = 0.2,
            FrequencyPenalty = 0.1,
            RopeScaling = "yarn",
            RopeScale = 2,
            RopeFreqBase = 1_000_000,
            RopeFreqScale = 0.5
        };

        var args = RuntimeAdapter.BuildArgs(request);

        Assert.Contains("--spec-type", args);
        Assert.Contains("draft-mtp", args);
        Assert.Contains("--model-draft", args);
        Assert.Contains("draft.gguf", args);
        Assert.Contains("--n-gpu-layers-draft", args);
        Assert.Contains("999", args);
        Assert.Contains("--spec-draft-n-min", args);
        Assert.Contains("--spec-draft-n-max", args);
        Assert.Contains("--cache-type-k-draft", args);
        Assert.Contains("--cache-type-v-draft", args);
        Assert.Contains("--predict", args);
        Assert.Contains("512", args);
        Assert.Contains("--seed", args);
        Assert.Contains("1234", args);
        Assert.Contains("--repeat-last-n", args);
        Assert.Contains("--repeat-penalty", args);
        Assert.Contains("1.08", args);
        Assert.Contains("--presence-penalty", args);
        Assert.Contains("--frequency-penalty", args);
        Assert.Contains("--rope-scaling", args);
        Assert.Contains("yarn", args);
        Assert.Contains("--rope-scale", args);
        Assert.Contains("--rope-freq-base", args);
        Assert.Contains("--rope-freq-scale", args);

        var embeddedMtpArgs = RuntimeAdapter.BuildArgs(request with { SpecDraftModelPath = "" });
        Assert.Contains("draft-mtp", embeddedMtpArgs);
        Assert.DoesNotContain("--model-draft", embeddedMtpArgs);

        var dflashArgs = RuntimeAdapter.BuildArgs(request with
        {
            SpeculativeType = "draft-dflash",
            SpecDraftModelPath = "qwen-dflash-head.gguf",
            SpecDraftMaxTokens = 15
        });
        Assert.Contains("draft-dflash", dflashArgs);
        Assert.Contains("qwen-dflash-head.gguf", dflashArgs);
        Assert.Contains("--spec-draft-n-max", dflashArgs);
        Assert.True(RuntimeAdapter.Validate(request with { SpeculativeType = "draft-dflash" }).Ok);
        Assert.Contains("draft-dflash", LaunchSettingMetadataService.SpeculativeTypeOptions);

        var dsparkArgs = RuntimeAdapter.BuildArgs(request with
        {
            SpeculativeType = "draft-dspark",
            SpecDraftModelPath = "qwen-dspark-head.gguf",
            SpecDraftMaxTokens = 7
        });
        Assert.Contains("draft-dspark", dsparkArgs);
        Assert.Contains("qwen-dspark-head.gguf", dsparkArgs);
        Assert.Contains("--spec-draft-n-max", dsparkArgs);
        Assert.Contains("7", dsparkArgs);
        Assert.True(RuntimeAdapter.Validate(request with { SpeculativeType = "draft-dspark" }).Ok);
        Assert.Contains("draft-dspark", LaunchSettingMetadataService.SpeculativeTypeOptions);

        var mtpArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            SpeculativeType = "atomic-mtp",
            MtpHeadPath = "mtp-head.gguf"
        });
        Assert.Contains("--spec-type", mtpArgs);
        Assert.Contains("mtp", mtpArgs);
        Assert.DoesNotContain("atomic-mtp", mtpArgs);
        Assert.Contains("--mtp-head", mtpArgs);
        Assert.Contains("mtp-head.gguf", mtpArgs);
        Assert.DoesNotContain("--model-draft", mtpArgs);

        var legacyMtpArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            SpeculativeType = "mtp",
            MtpHeadPath = "legacy-mtp-head.gguf"
        });
        Assert.Contains("--spec-type", legacyMtpArgs);
        Assert.Contains("mtp", legacyMtpArgs);
        Assert.Contains("legacy-mtp-head.gguf", legacyMtpArgs);

        var missingMtp = RuntimeAdapter.Validate(ValidLaunchRequest() with { SpeculativeType = "atomic-mtp" });
        Assert.False(missingMtp.Ok);
        Assert.Contains(missingMtp.Errors, error => error.Contains("MTP head", StringComparison.OrdinalIgnoreCase));
    }


}
