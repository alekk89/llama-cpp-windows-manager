using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class DirectModelAliasesTests
{
    [Theory]
    [InlineData(@"D:\models\Qwen3.8-Flash-Next-UD-Q3_K_XL\Qwen3.8-Flash-Next-UD-Q3_K_XL-00001-of-00003.gguf", "Qwen3.8-Flash-Next-UD-Q3_K_XL")]
    [InlineData("/mnt/d/models/my model.gguf", "my-model")]
    [InlineData("model.GGUF", "model")]
    public void MissingAliasAdvertisesShortFilenameWithoutPathOrShard(string path, string expected)
    {
        var effective = RuntimeDirectAliasService.ForLaunch(AppSettings.CreateDefault("workspace"), path, []);
        Assert.Equal([expected], RuntimeModelAliasService.ReadAliases(effective.CustomParameters));
    }

    [Fact]
    public void ExplicitAliasesSuffixesAndCollisionsKeepClientIdsUsable()
    {
        var original = AppSettings.CreateDefault("workspace") with { CustomParameters = "--alias 'owner/Qwen,secondary' --threads 4" };
        var unchanged = RuntimeDirectAliasService.ForLaunch(original, "a.gguf", []);
        Assert.Equal(["owner/Qwen", "secondary"], RuntimeModelAliasService.ReadAliases(unchanged.CustomParameters));
        var configured = original with { DirectModelAliasSuffix = "-direct" };
        var first = RuntimeDirectAliasService.ForLaunch(configured, "a.gguf", []);
        var second = RuntimeDirectAliasService.ForLaunch(configured, "a.gguf", ["owner/Qwen-direct", "owner/Qwen-direct:2", "secondary-direct"]);
        Assert.Equal(["owner/Qwen-direct", "secondary-direct"], RuntimeModelAliasService.ReadAliases(first.CustomParameters));
        Assert.Equal(["owner/Qwen-direct:3", "secondary-direct:2"], RuntimeModelAliasService.ReadAliases(second.CustomParameters));
        Assert.Equal(second.CustomParameters, RuntimeDirectAliasService.ForLaunch(second, "a.gguf", []).CustomParameters);
        Assert.Equal("--alias 'owner/Qwen,secondary' --threads 4", original.CustomParameters);
    }

    [Fact]
    public void AliasInsertionPreservesEmptyQuotedAndJsonArguments()
    {
        var settings = AppSettings.CreateDefault("workspace") with { CustomParameters = "--custom \"it's a value\" --empty \"\" --json '{\"path\":\"D:\\\\models\"}'" };
        var before = CustomLaunchParameterParser.Parse(settings.CustomParameters);
        var effective = RuntimeDirectAliasService.ForLaunch(settings, "a.gguf", []);
        Assert.Equal(before, CustomLaunchParameterParser.Parse(effective.CustomParameters).Take(before.Count));
    }

    [Theory]
    [InlineData("-direct,second")]
    [InlineData("/direct")]
    [InlineData("-direct\nother")]
    public void InvalidSuffixCannotInjectAliasesOrPaths(string suffix)
        => Assert.Throws<InvalidOperationException>(() => RuntimeDirectAliasService.ValidateSuffix(suffix));
}
