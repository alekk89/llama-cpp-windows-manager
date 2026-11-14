using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.Tests;

public sealed class RuntimeDefinedChoicesTests : ManagerRegressionTestBase
{
    // Shape of llama.cpp's common/arg.cpp help, as printed by a TurboQuant fork.
    private const string ForkHelp = """
        -ctk,  --cache-type-k TYPE              KV cache data type for K
                                                allowed values: f32, f16, bf16, q8_0, q4_0, q4_1, iq4_nl,
                                                q5_0, q5_1, turbo2, turbo3, turbo4
                                                (default: f16)
                                                (env: LLAMA_ARG_CACHE_TYPE_K)
        -ctv,  --cache-type-v TYPE              KV cache data type for V
                                                allowed values: f32, f16, bf16, q8_0, q4_0, q4_1, iq4_nl,
                                                q5_0, q5_1, turbo2, turbo3, turbo4
                                                (default: f16)
        --rope-scaling {none,linear,yarn}       RoPE frequency scaling method
        --threads-batch N                       prompt processing threads
        """;

    [Fact]
    public void HelpParserReadsLlamaCppAllowedValuesListsAcrossWrappedLines()
    {
        var options = RuntimeLaunchHelpParser.Parse(ForkHelp);

        var cacheK = Assert.Single(options, option => option.Name == "--cache-type-k");
        Assert.Equal(RuntimeLaunchOptionValueKind.Choice, cacheK.ValueKind);
        Assert.Equal(["f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1", "turbo2", "turbo3", "turbo4"], cacheK.Choices);
        Assert.Equal("f16", cacheK.DefaultValue);
    }

    [Fact]
    public void ChoicesComeFromTheRuntimeKeepAppValuesAndNeverDropTheSavedValue()
    {
        var options = RuntimeLaunchHelpParser.Parse(ForkHelp);
        var cacheK = RuntimeDefinedChoicePolicy.CacheTypeK;
        var rope = RuntimeDefinedChoicePolicy.RopeScaling;

        Assert.Contains("turbo3", RuntimeDefinedChoicePolicy.Choices(cacheK, options, "q8_0"));
        Assert.Equal(RuntimeDefinedChoicePolicy.CacheTypes, RuntimeDefinedChoicePolicy.Choices(cacheK, null, "q8_0"));
        // A value saved for another runtime stays selectable instead of being replaced.
        Assert.Equal("turbo3", RuntimeDefinedChoicePolicy.Choices(cacheK, null, "turbo3")[^1]);
        // "auto" means "omit the flag"; it is a Manager value the runtime never lists.
        Assert.Equal(["auto", "none", "linear", "yarn"], RuntimeDefinedChoicePolicy.Choices(rope, options, "auto"));
        // The draft flag is not advertised here, so its defaults apply.
        Assert.Null(RuntimeDefinedChoicePolicy.AdvertisedChoices(RuntimeDefinedChoicePolicy.SpecDraftCacheTypeK, options));
    }

    [Theory]
    [InlineData("turbo3", true)]
    [InlineData("q8_0", true)]
    [InlineData("deepseek-legacy", true)]
    [InlineData("", false)]
    [InlineData("-x", false)]
    [InlineData("q8_0 --rpc", false)]
    [InlineData("q8_0;rm", false)]
    [InlineData("$(id)", false)]
    public void RuntimeValuesMustBeSingleWellFormedTokens(string value, bool expected)
        => Assert.Equal(expected, RuntimeDefinedChoicePolicy.IsWellFormed(value));

    [Fact]
    public void LaunchValidationAcceptsRuntimeSpecificValuesAndRejectsMalformedOnes()
    {
        var request = ValidLaunchRequest() with
        {
            CacheTypeK = "turbo3",
            CacheTypeV = "turbo4",
            SpecDraftCacheTypeK = "turbo2",
            SpecDraftCacheTypeV = "q8_0",
            ReasoningFormat = "auto",
            RopeScaling = "yarn"
        };
        Assert.True(LlamaCppLaunchValidator.Validate(request).Ok);

        var malformed = LlamaCppLaunchValidator.Validate(request with { CacheTypeK = "q8_0 --rpc host" });
        Assert.False(malformed.Ok);
        Assert.Contains(malformed.Errors, error => error.StartsWith("K cache type", StringComparison.Ordinal));
    }

    [Fact]
    public void ProfileImportKeepsRuntimeSpecificValuesAndRejectsMalformedOnes()
    {
        var defaults = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(CreateTempRoot()));

        var imported = ProfileBundleCodec.ApplySettings(defaults, new JsonObject { ["cacheTypeK"] = "turbo3", ["cacheTypeV"] = "turbo4" });

        Assert.Equal("turbo3", imported.CacheTypeK);
        Assert.Equal("turbo4", imported.CacheTypeV);
        Assert.Throws<InvalidOperationException>(() => ProfileBundleCodec.ApplySettings(defaults, new JsonObject { ["cacheTypeK"] = "--rpc" }));
    }

    [Fact]
    public void BenchmarkPlansAcceptRuntimeSpecificCacheTypesAndRejectMalformedOnes()
    {
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetTempPath()), "runtime");
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile("profile", model.Id, "Default", settings, DateTimeOffset.UtcNow, true);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-bench.exe", "{}", DateTimeOffset.UtcNow);
        BenchmarkPlan Plan(params string[] cacheTypes) => new()
        {
            AllModels = true,
            AllProfiles = true,
            UseProfileRuntime = true,
            PromptSizes = [512],
            GenerationSizes = [128],
            Options = new BenchmarkOptionSet { CacheTypesKv = cacheTypes }
        };

        var accepted = new BenchmarkPlanService().Preview(Plan("q8_0", "turbo3"), [model], [profile], [runtime]);
        var rejected = new BenchmarkPlanService().Preview(Plan("turbo3 -m x"), [model], [profile], [runtime]);

        Assert.DoesNotContain(accepted.Errors, error => error.Contains("cache type", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rejected.Errors, error => error.StartsWith("K/V cache type", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoveryKeepsManagedFlagsForChoicesButDoesNotRenderThem()
    {
        var root = CreateTempRoot();
        var executable = Path.Combine(root, "llama-server.exe");
        await File.WriteAllBytesAsync(executable, [0], TestContext.Current.CancellationToken);
        var runner = new RecordingProcessRunner(new ProcessRunResult(0, ForkHelp, ""));
        var service = new RuntimeLaunchOptionDiscoveryService(runner);
        var runtime = new RuntimeChoice("fork", "Fork", RuntimeBackend.Cuda, RuntimeMode.Native, executable, "Fork");

        var all = await service.DiscoverAllAsync(runtime, "", TestContext.Current.CancellationToken);
        var renderable = await service.DiscoverAsync(runtime, "", TestContext.Current.CancellationToken);

        Assert.Contains("turbo4", RuntimeDefinedChoicePolicy.Choices(RuntimeDefinedChoicePolicy.CacheTypeV, all, "f16"));
        Assert.DoesNotContain(renderable, option => option.Name == "--cache-type-k");
        Assert.Contains(renderable, option => option.Name == "--threads-batch");
        Assert.Equal(1, runner.CallCount);
    }

    private sealed class RecordingProcessRunner(ProcessRunResult result) : IProcessRunner
    {
        public int CallCount { get; private set; }

        public Task<ProcessRunResult> RunAsync(
            System.Diagnostics.ProcessStartInfo psi,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            string? standardInput = null)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}
