using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void HuggingFaceSuggestedLaunchSettingsPreferLlamaServerCommand()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        const string readme = """
        ## Start the server

        ```bash
        llama-server -m Qwen3.6-27B-Q8_0-mtp.gguf \
          --spec-type draft-mtp --spec-draft-n-max 3 \
          --cache-type-k q8_0 --cache-type-v q8_0 \
          -np 1 -c 262144 --temp 0.7 --top-k 20 -ngl 99 --port 8081
        ```

        ## Direct CLI usage

        ```bash
        llama-cli -m Qwen3.6-27B-Q8_0-mtp.gguf -c 4096 -n 2048 --temp 0.2
        ```
        """;

        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(defaults, readme, """{"temperature":0.3,"top_p":0.8}""");

        Assert.NotNull(settings);
        Assert.Equal("draft-mtp", settings.SpeculativeType);
        Assert.Equal(3, settings.SpecDraftMaxTokens);
        Assert.Equal("q8_0", settings.CacheTypeK);
        Assert.Equal("q8_0", settings.CacheTypeV);
        Assert.Equal(1, settings.ParallelSlots);
        Assert.Equal(262_144, settings.ContextSize);
        Assert.Equal(0.7, settings.Temperature);
        Assert.Equal(20, settings.TopK);
        Assert.Equal(0.8, settings.TopP);
        Assert.Equal(99, settings.GpuLayers);
        Assert.Equal(-1, settings.MaxTokens);
    }


    [Fact]
    public void HuggingFaceSuggestedLaunchSettingsParseInlineEqualsQuotedPathsAndDraftOptions()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        const string readme = """
        ```bash
        llama-server --ctx-size=32768 --top-p=0.92 --min-p=0.05 \
          --repeat-penalty=1.08 --presence-penalty=-0.2 --frequency-penalty=0.1 \
          --image-min-tokens=256 --image-max-tokens=1024 \
          --flash-attn=on --rope-scaling=yarn --rope-scale=2 --rope-freq-base=1000000 --rope-freq-scale=0.5 \
          --spec-type=draft-simple --model-draft "D:\models\draft model.gguf" --spec-draft-ngl=10 \
          --spec-draft-n-min=1 --draft-p-split=0.45 --draft-p-min=0.12 \
          --cache-type-k-draft=q4_0 --cache-type-v-draft=q5_1
        ```
        """;

        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(defaults, readme);

        Assert.NotNull(settings);
        Assert.Equal(32_768, settings.ContextSize);
        Assert.Equal(0.92, settings.TopP);
        Assert.Equal(0.05, settings.MinP);
        Assert.Equal(1.08, settings.RepeatPenalty);
        Assert.Equal(-0.2, settings.PresencePenalty);
        Assert.Equal(0.1, settings.FrequencyPenalty);
        Assert.Equal(256, settings.VisionImageMinTokens);
        Assert.Equal(1024, settings.VisionImageMaxTokens);
        Assert.Equal("on", settings.FlashAttention);
        Assert.Equal("yarn", settings.RopeScaling);
        Assert.Equal(2, settings.RopeScale);
        Assert.Equal(1_000_000, settings.RopeFreqBase);
        Assert.Equal(0.5, settings.RopeFreqScale);
        Assert.Equal("draft-simple", settings.SpeculativeType);
        Assert.Equal(@"D:\models\draft model.gguf", settings.SpecDraftModelPath);
        Assert.Equal(10, settings.SpecDraftGpuLayers);
        Assert.Equal(1, settings.SpecDraftMinTokens);
        Assert.Equal(0.45, settings.SpecDraftPSplit);
        Assert.Equal(0.12, settings.SpecDraftPMin);
        Assert.Equal("q4_0", settings.SpecDraftCacheTypeK);
        Assert.Equal("q5_1", settings.SpecDraftCacheTypeV);
    }

    [Fact]
    public void HuggingFaceSuggestedLaunchSettingsParseMtpHeadOptions()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        const string readme = """
        ```bash
        llama-server -m Gemma4-31B-Q8_0.gguf --spec-type mtp --mtp-head "D:\models\mtp-gemma-4-31B-it.gguf"
        ```
        """;

        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(defaults, readme);

        Assert.NotNull(settings);
        Assert.Equal("atomic-mtp", settings.SpeculativeType);
        Assert.Equal(@"D:\models\mtp-gemma-4-31B-it.gguf", settings.MtpHeadPath);
    }

    [Theory]
    [InlineData("draft-dflash")]
    [InlineData("draft-dspark")]
    [InlineData("draft-eagle3")]
    public void HuggingFaceSuggestedLaunchSettingsKeepCurrentDraftTypes(string speculativeType)
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(
            defaults,
            $"llama-server -m target.gguf --spec-type {speculativeType} --model-draft helper.gguf");

        Assert.NotNull(settings);
        Assert.Equal(speculativeType, settings.SpeculativeType);
        Assert.Equal("helper.gguf", settings.SpecDraftModelPath);
    }


    [Fact]
    public void HuggingFaceSuggestedLaunchSettingsApplyConfigJsonAndFallbackToCli()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        const string readme = """
        Direct run:
            llama-cli -m model.gguf -c 8192 -n 256 --temp=0.4
        """;

        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(
            defaults,
            readme,
            """{"top_k":"33","max_new_tokens":128}""",
            """{"max_position_embeddings":65536}""");

        Assert.NotNull(settings);
        Assert.Equal(8_192, settings.ContextSize);
        Assert.Equal(256, settings.MaxTokens);
        Assert.Equal(0.4, settings.Temperature);
        Assert.Equal(33, settings.TopK);
    }


    [Fact]
    public void HuggingFaceSuggestedLaunchSettingsIgnoreOutOfRangeContextConfig()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());

        var settings = HuggingFaceLaunchSettingsSuggester.TryCreate(
            defaults,
            "",
            """{"top_k":44}""",
            """{"max_position_embeddings":524288}""");

        Assert.NotNull(settings);
        Assert.Equal(AppSettings.DefaultContextSize, settings.ContextSize);
        Assert.Equal(44, settings.TopK);
    }


    [Fact]
    public void GgufMetadataReaderRejectsHugeMetadataArraysQuickly()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "huge-array.gguf");
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            writer.Write((uint)3);
            writer.Write((ulong)0);
            writer.Write((ulong)1);
            WriteGgufString(writer, "tokenizer.ggml.tokens");
            writer.Write((uint)9);
            writer.Write((uint)0);
            writer.Write(1_000_001UL);
        }

        var metadata = GgufMetadataReader.TryRead(path);

        Assert.Empty(metadata);
    }


    [Fact]
    public void GgufMetadataReaderRejectsUnsupportedVersions()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "future-version.gguf");
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            writer.Write((uint)99);
            writer.Write((ulong)0);
            writer.Write((ulong)1);
            WriteGgufString(writer, "general.architecture");
            writer.Write((uint)8);
            WriteGgufString(writer, "future");
        }

        var metadata = GgufMetadataReader.TryRead(path);

        Assert.Empty(metadata);
    }

    private static void WriteMinimalGguf(string path, string architecture, params (string Key, uint Value)[] numericMetadata)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
        writer.Write((uint)3);
        writer.Write((ulong)0);
        writer.Write((ulong)(1 + numericMetadata.Length));
        WriteGgufString(writer, "general.architecture");
        writer.Write((uint)8);
        WriteGgufString(writer, architecture);
        foreach (var (key, value) in numericMetadata)
        {
            WriteGgufString(writer, key);
            writer.Write((uint)4);
            writer.Write(value);
        }
    }

}
