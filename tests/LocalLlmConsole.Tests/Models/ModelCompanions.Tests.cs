using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class ModelCompanionsTests : ManagerRegressionTestBase
{
    [Fact]
    public void HuggingFaceRepoMetadataCacheIsBounded()
    {
        var service = ReadServicePartialSources("HuggingFaceService");

        Assert.Contains("private const int RepoInfoCacheLimit", service, StringComparison.Ordinal);
        Assert.Contains("RepoInfoCacheTtl", service, StringComparison.Ordinal);
        Assert.Contains("CachedRepoInfo", service, StringComparison.Ordinal);
        Assert.Contains("TrimRepoInfoCache(now)", service, StringComparison.Ordinal);
        Assert.Contains("_repoInfoCache.TryRemove", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentDictionary<string, RepoInfo> _repoInfoCache", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelCatalogFindsAdjacentDraftModel()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var main = Path.Combine(models, "Qwen3-main.gguf");
        var draft = Path.Combine(models, "Qwen3-MTP-draft.gguf");
        var projector = Path.Combine(models, "mmproj-model.gguf");
        File.WriteAllText(main, "main");
        File.WriteAllText(draft, "draft");
        File.WriteAllText(projector, "projector");

        var found = ModelCatalogService.FindDraftModel(main);

        Assert.Equal(Path.GetFullPath(draft), Path.GetFullPath(found!));
    }

    [Fact]
    public void ModelCatalogFindsAdjacentDFlashHead()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var main = Path.Combine(models, "Qwen3.6-27B-Q4_K_M.gguf");
        var dflash = Path.Combine(models, "Qwen3.6-27B-DFlash-b16-Q4_K_M.gguf");
        File.WriteAllText(main, "main");
        File.WriteAllText(dflash, "dflash");

        var found = ModelCatalogService.FindDraftModel(main);

        Assert.Equal(Path.GetFullPath(dflash), Path.GetFullPath(found!));
    }

    [Fact]
    public void ModelCatalogFindsAndPrefersAdjacentDSparkHead()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var main = Path.Combine(models, "Qwen3-8B-Q4_K_M.gguf");
        var dflash = Path.Combine(models, "Qwen3-8B-DFlash-b16.gguf");
        var dspark = Path.Combine(models, "Qwen3-8B-DSpark.gguf");
        File.WriteAllText(main, "main");
        File.WriteAllText(dflash, "dflash");
        File.WriteAllText(dspark, "dspark");

        var found = ModelCatalogService.FindDraftModel(main);

        Assert.Equal(Path.GetFullPath(dspark), Path.GetFullPath(found!));
    }

    [Fact]
    public void DraftMtpUsesEmbeddedNextNAndRejectsDifferentModelHelpers()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        var modelFolder = Path.Combine(models, "qwen3.8-27b");
        Directory.CreateDirectory(modelFolder);
        var main = Path.Combine(modelFolder, "Qwen3.8-27B-Q8_0.gguf");
        using (var stream = File.Create(main))
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            writer.Write((uint)3);
            writer.Write((ulong)0);
            writer.Write((ulong)2);
            WriteGgufString(writer, "general.architecture");
            writer.Write((uint)8);
            WriteGgufString(writer, "qwen35");
            WriteGgufString(writer, "qwen35.nextn_predict_layers");
            writer.Write((uint)4);
            writer.Write((uint)1);
        }

        var unrelated = Path.Combine(models, "Qwen3.6-27B-DSpark.gguf");
        var compatible = Path.Combine(modelFolder, "Qwen3.8-27B-DFlash.gguf");
        File.WriteAllText(unrelated, "unrelated");
        File.WriteAllText(compatible, "compatible");

        Assert.True(ModelCatalogService.HasEmbeddedDraftMtp(main));
        Assert.DoesNotContain(unrelated, ModelCatalogService.FindDraftModels(main), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(compatible, ModelCatalogService.FindDraftModels(main), StringComparer.OrdinalIgnoreCase);
        Assert.Null(ModelCatalogService.ResolveDraftModelPath(main, "", "draft-mtp"));
        Assert.Equal(compatible, ModelCatalogService.ResolveDraftModelPath(main, compatible, "draft-mtp"));
        Assert.Equal(compatible, ModelCatalogService.ResolveDraftModelPath(main, "", "draft-dflash"));
    }

    [Theory]
    [InlineData("qwen35")]
    [InlineData("deepseek2")]
    [InlineData("future_mtp")]
    public void EmbeddedMtpDetectionSkipsLargeTokenizerArraysForAnyArchitecture(string architecture)
    {
        var root = CreateTempRoot();
        var main = Path.Combine(root, $"{architecture}-main.gguf");
        using (var stream = File.Create(main))
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            writer.Write((uint)3);
            writer.Write((ulong)0);
            writer.Write((ulong)5);
            WriteGgufString(writer, "general.architecture");
            writer.Write((uint)8);
            WriteGgufString(writer, architecture);
            WriteGgufString(writer, "tokenizer.ggml.tokens");
            writer.Write((uint)9);
            writer.Write((uint)8);
            writer.Write((ulong)100_001);
            for (var index = 0; index < 100_001; index++)
                writer.Write((ulong)0);
            WriteGgufString(writer, $"{architecture}.nextn_predict_layers");
            writer.Write((uint)10);
            writer.Write((ulong)1);
            WriteGgufString(writer, "tokenizer.ggml.token_type");
            writer.Write((uint)9);
            writer.Write((uint)5);
            writer.Write((ulong)100_001);
            writer.Write(new byte[100_001 * sizeof(int)]);
            WriteGgufString(writer, "tokenizer.chat_template");
            writer.Write((uint)8);
            WriteGgufString(writer, "{{ think }}");
        }

        var inspection = GgufMetadataReader.Inspect(main);

        Assert.True(inspection.Success, inspection.Error);
        Assert.Equal("{{ think }}", inspection.Values["tokenizer.chat_template"]);
        Assert.True(ModelCatalogService.HasEmbeddedDraftMtp(main));
        Assert.Null(ModelCatalogService.ResolveDraftModelPath(main, "", "draft-mtp"));
    }

    [Fact]
    public void EmbeddedMtpDetectionDoesNotDependOnMetadataKeyPosition()
    {
        var root = CreateTempRoot();
        var main = Path.Combine(root, "many-metadata-keys.gguf");
        using (var stream = File.Create(main))
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            writer.Write((uint)3);
            writer.Write((ulong)0);
            writer.Write((ulong)522);
            WriteGgufString(writer, "general.architecture");
            writer.Write((uint)8);
            WriteGgufString(writer, "future_mtp");
            for (var index = 0; index < 520; index++)
            {
                WriteGgufString(writer, $"vendor.metadata_{index}");
                writer.Write((uint)4);
                writer.Write((uint)index);
            }
            WriteGgufString(writer, "future_mtp.nextn_predict_layers");
            writer.Write((uint)4);
            writer.Write((uint)1);
        }

        var inspection = GgufMetadataReader.Inspect(main);

        Assert.True(inspection.Success, inspection.Error);
        Assert.True(ModelCatalogService.HasEmbeddedDraftMtp(main));
    }

    [Fact]
    public void CompanionDiscoveryStaysInModelFolderAndMatchesRequestedSpeculativeType()
    {
        var root = CreateTempRoot();
        var modelFolder = Path.Combine(root, "Qwen3.8-27B");
        var childFolder = Path.Combine(modelFolder, "old");
        Directory.CreateDirectory(childFolder);
        var main = Path.Combine(modelFolder, "Qwen3.8-27B-Q4_K_M.gguf");
        var mtp = Path.Combine(modelFolder, "mtp-Qwen3.8-27B-Q8_0.gguf");
        var dflash = Path.Combine(modelFolder, "Qwen3.8-27B-DFlash.gguf");
        var dspark = Path.Combine(modelFolder, "Qwen3.8-27B-DSpark.gguf");
        var eagle = Path.Combine(modelFolder, "Qwen3.8-27B-EAGLE3.gguf");
        var parentMtp = Path.Combine(root, "mtp-Qwen3.8-27B-parent.gguf");
        var childProjector = Path.Combine(childFolder, "mmproj-Qwen3.8-27B-f16.gguf");
        var localProjector = Path.Combine(modelFolder, "mmproj-Qwen3.8-27B-f16.gguf");
        foreach (var path in new[] { main, mtp, dflash, dspark, eagle, parentMtp, childProjector, localProjector })
            File.WriteAllText(path, path);

        Assert.Equal(mtp, ModelCatalogService.ResolveDraftModelPath(main, "", "draft-mtp"));
        Assert.Equal(dflash, ModelCatalogService.ResolveDraftModelPath(main, "", "draft-dflash"));
        Assert.Equal(dspark, ModelCatalogService.ResolveDraftModelPath(main, "", "draft-dspark"));
        Assert.Equal(eagle, ModelCatalogService.ResolveDraftModelPath(main, "", "draft-eagle3"));
        Assert.Equal(mtp, ModelCatalogService.ResolveMtpHeadPath(main, "", "atomic-mtp"));
        Assert.Equal(localProjector, ModelCatalogService.FindVisionProjector(main));
        Assert.DoesNotContain(parentMtp, ModelCatalogService.FindDraftModels(main), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(childProjector, ModelCatalogService.FindVisionProjectors(main), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompanionDiscoveryUsesGgufArchitectureMetadataAndAllowsSmallerSimpleDrafts()
    {
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);
        var gemma = Path.Combine(root, "Gemma-4-12B-it-Q4_K_M.gguf");
        var gemmaAssistant = Path.Combine(root, "Gemma-4-12B-it-assistant-Q8_0.gguf");
        var mistral = Path.Combine(root, "Mistral-Small-3.1-24B-Q4_K_M.gguf");
        var mistralDraft = Path.Combine(root, "draft-Mistral-Small-3.1-7B-Q8_0.gguf");
        File.WriteAllText(gemma, "main");
        File.WriteAllText(mistral, "main");
        File.WriteAllText(mistralDraft, "draft");
        WriteMinimalGguf(gemmaAssistant, "gemma4-assistant", ("gemma4.nextn_predict_layers", 4u));

        Assert.Equal(gemmaAssistant, ModelCatalogService.ResolveDraftModelPath(gemma, "", "draft-mtp"));
        Assert.Equal(mistralDraft, ModelCatalogService.ResolveDraftModelPath(mistral, "", "draft-simple"));
        Assert.Null(ModelCatalogService.ResolveDraftModelPath(mistral, "", "draft-mtp"));
    }

    [Fact]
    public async Task ModelScanKeepsEmbeddedMtpMainAndSkipsStandaloneAssistantArchitecture()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var qwenMain = Path.Combine(models, "Ornith-Qwen3.5-35B-A3B-MTP-APEX-4.0-UD-Q4_K_XL.gguf");
        var gemmaAssistant = Path.Combine(models, "Gemma-4-12B-it-assistant-Q8_0.gguf");
        WriteMinimalGguf(qwenMain, "qwen35", ("qwen35.nextn_predict_layers", 1u));
        WriteMinimalGguf(gemmaAssistant, "gemma4-assistant", ("gemma4.nextn_predict_layers", 4u));
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);

        var result = await catalog.ScanDetailedAsync(models);
        var saved = await store.ListModelsAsync();

        Assert.Equal(1, result.RegisteredCount);
        Assert.Equal(GgufFileRole.MainModel, result.Files.Single(file => file.Path == qwenMain).Role);
        Assert.True(result.Files.Single(file => file.Path == qwenMain).EmbeddedDraftMtp);
        Assert.Equal(GgufFileRole.SpeculativeAssistant, result.Files.Single(file => file.Path == gemmaAssistant).Role);
        Assert.Equal(qwenMain, Assert.Single(saved).ModelPath);
    }

    [Fact]
    public async Task ModelCatalogTreatsVisionHeadCompanionsAsProjectorsNotMainModels()
    {
        var root = CreateTempRoot();
        var models = Path.Combine(root, "models");
        Directory.CreateDirectory(models);
        var main = Path.Combine(models, "Gemma-main.gguf");
        var visionHead = Path.Combine(models, "Gemma-mtp-vision-f16.gguf");
        var draft = Path.Combine(models, "Gemma-MTP-draft.gguf");
        WriteMinimalGguf(main, "gemma");
        WriteMinimalGguf(visionHead, "clip");
        WriteMinimalGguf(draft, "gemma");
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var catalog = new ModelCatalogService(store);

        var foundProjector = ModelCatalogService.FindVisionProjector(main);
        var registered = await catalog.ScanAsync(models);
        var savedModels = await store.ListModelsAsync();

        Assert.Equal(Path.GetFullPath(visionHead), Path.GetFullPath(foundProjector!));
        Assert.Equal(1, registered);
        Assert.DoesNotContain(savedModels, model => string.Equals(model.ModelPath, visionHead, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(savedModels, model => string.Equals(model.ModelPath, draft, StringComparison.OrdinalIgnoreCase));
    }


}
