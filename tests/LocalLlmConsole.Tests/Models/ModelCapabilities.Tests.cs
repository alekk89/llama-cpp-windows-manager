using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class ModelCapabilitiesTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData("owner/repo", "owner/repo", "", "")]
    [InlineData("owner/repo/folder/model-q4.gguf", "owner/repo", "folder/model-q4.gguf", "")]
    [InlineData("https://huggingface.co/owner/repo", "owner/repo", "", "")]
    [InlineData("https://huggingface.co/owner/repo/blob/main/folder/model%20q4.gguf", "owner/repo", "folder/model q4.gguf", "main")]
    [InlineData("https://hf.co/owner/repo/resolve/main/model.gguf?download=true", "owner/repo", "model.gguf", "main")]
    public void HuggingFaceSearchParsesDirectRepoAndFileReferences(string input, string repo, string path, string revision)
    {
        Assert.True(HuggingFaceService.TryParseModelReference(input, out var reference));
        Assert.Equal(repo, reference.Repo);
        Assert.Equal(path, reference.Path);
        Assert.Equal(revision, reference.Revision);
    }


    [Theory]
    [InlineData("")]
    [InlineData("plain search text")]
    [InlineData("https://example.com/owner/repo")]
    [InlineData("owner/repo/config.json")]
    [InlineData("owner/../repo/model.gguf")]
    public void HuggingFaceSearchRejectsNonDirectReferences(string input)
    {
        Assert.False(HuggingFaceService.TryParseModelReference(input, out _));
    }


    [Theory]
    [InlineData("owner/repo", "https://huggingface.co/owner/repo")]
    [InlineData(" owner.name/repo_name ", "https://huggingface.co/owner.name/repo_name")]
    public void HuggingFaceModelCardUrlsRequireSafeRepoIds(string repo, string expected)
    {
        Assert.True(HuggingFaceService.TryCreateModelCardUrl(repo, out var url));
        Assert.Equal(expected, url);

        Assert.False(HuggingFaceService.TryCreateModelCardUrl("owner/../repo", out _));
        Assert.False(HuggingFaceService.TryCreateModelCardUrl("https://example.com/owner/repo", out _));
    }

    [Fact]
    public void HuggingFaceModelCardApplicationServiceOwnsRowParsingOpenAndStatus()
    {
        var service = new HuggingFaceModelCardApplicationService();
        var calls = new List<string>();
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "Q4", 1024, 1);
        var row = new HuggingFaceSearchRow
        {
            File = file,
            Repo = file.Repo,
            FilePath = file.Path,
            Quant = file.Quant,
            Size = "1 KB",
            Downloads = "1",
            Signals = "GGUF"
        };
        var fallbackFile = file with { Repo = "fallback/repo" };
        var fallbackRow = new HuggingFaceSearchRow
        {
            File = fallbackFile,
            Repo = fallbackFile.Repo,
            FilePath = fallbackFile.Path,
            Quant = fallbackFile.Quant,
            Size = "1 KB",
            Downloads = "1",
            Signals = "GGUF"
        };

        HuggingFaceModelCardApplicationActions Actions()
            => new(
                url => calls.Add($"open:{url}"),
                status => calls.Add($"status:{status}"));

        var opened = service.OpenFromRow(row, Actions());
        var fallback = service.OpenFromRow(fallbackRow, Actions());
        var blocked = service.Open("https://example.com/owner/repo", Actions());

        Assert.Equal("owner/repo", HuggingFaceModelCardApplicationService.RepoFromSearchRow(row));
        Assert.Equal("fallback/repo", HuggingFaceModelCardApplicationService.RepoFromSearchRow(fallbackRow));
        Assert.Equal(HuggingFaceModelCardApplicationOutcome.Opened, opened);
        Assert.Equal(HuggingFaceModelCardApplicationOutcome.Opened, fallback);
        Assert.Equal(HuggingFaceModelCardApplicationOutcome.Blocked, blocked);
        Assert.Contains("open:https://huggingface.co/owner/repo", calls);
        Assert.Contains("open:https://huggingface.co/fallback/repo", calls);
        Assert.Contains("status:Opened Hugging Face model card: owner/repo", calls);
        Assert.Contains("status:The selected row does not contain a valid Hugging Face repository.", calls);
    }


    [Fact]
    public void ModelCapabilityServiceInfersCapabilitiesFromModelMetadata()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "Qwen3-VL-Q4_K_M.gguf");
        var model = new ModelRecord(
            "qwen3-vl",
            "Qwen3 VL Reasoning MoE Embed FIM",
            modelPath,
            OwnershipKind.External,
            """{"tags":["image-text-to-text","reasoning","feature-extraction","fim","moe"],"HasVisionProjector":true}""",
            DateTimeOffset.UtcNow);

        var capabilities = ModelCapabilityService.Inspect(model);
        var summary = ModelCapabilityService.SummaryText(capabilities);
        var context = ModelCapabilityService.ContextLength(
            new Dictionary<string, object?> { ["qwen3.context_length"] = "32768" },
            "qwen3");
        var selectedCapabilities = new SelectedModelCapabilityController();
        var noModelState = selectedCapabilities.Apply(null, ModelCapabilityService.Empty());
        var selectedState = selectedCapabilities.Apply(model, capabilities);

        Assert.Equal("Q4_K_M", capabilities.Quantization);
        Assert.True(capabilities.HasVisionProjector);
        Assert.True(capabilities.LikelyVision);
        Assert.True(capabilities.LikelyReasoning);
        Assert.True(capabilities.IsEmbedding);
        Assert.True(capabilities.IsFim);
        Assert.True(capabilities.IsMoe);
        Assert.False(capabilities.HasMetadata);
        Assert.Equal(32768, context);
        Assert.Contains("Vision: mmproj found", summary, StringComparison.Ordinal);
        Assert.Contains("GGUF metadata: unavailable", summary, StringComparison.Ordinal);
        Assert.True(ModelCapabilityService.LooksVisionCapable("llama-3.2-vision"));
        Assert.Equal(SelectedModelCapabilityController.NoModelText, noModelState.DisplayText);
        Assert.False(noModelState.VisionLaunchSettingsAvailable);
        Assert.Same(capabilities, selectedState.Capabilities);
        Assert.Equal(summary, selectedState.DisplayText);
        Assert.True(selectedState.VisionLaunchSettingsAvailable);
    }


    [Fact]
    public void ModelCapabilityCacheKeyTracksVisionProjectorPairing()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "Qwen3-VL-Q4_K_M.gguf");
        File.WriteAllText(modelPath, "fake model");
        var model = new ModelRecord(
            "qwen3-vl",
            "Qwen3 VL",
            modelPath,
            OwnershipKind.External,
            """{"CapabilityHints":"vision"}""",
            DateTimeOffset.UtcNow);

        var beforeKey = ModelCapabilityService.CacheKey(model);
        var before = ModelCapabilityService.Inspect(model);
        Assert.True(before.LikelyVision);
        Assert.False(before.HasVisionProjector);
        Assert.Contains("projector not found", ModelCapabilityService.SummaryText(before), StringComparison.OrdinalIgnoreCase);

        File.WriteAllText(Path.Combine(root, "mmproj-qwen3-vl.gguf"), "projector");
        var afterKey = ModelCapabilityService.CacheKey(model);
        var after = ModelCapabilityService.Inspect(model);

        Assert.NotEqual(beforeKey, afterKey);
        Assert.True(after.HasVisionProjector);
        Assert.Contains("mmproj found", ModelCapabilityService.SummaryText(after), StringComparison.OrdinalIgnoreCase);
    }


}
