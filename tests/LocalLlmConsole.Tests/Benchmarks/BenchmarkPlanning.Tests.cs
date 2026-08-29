using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using System.Text.Json;

namespace LocalLlmConsole.Tests;

public sealed class BenchmarkPlanningTests
{
    [Fact]
    public void CommandBuilderSuppressesOmittedUpstreamTestFamilies()
    {
        var plan = new BenchmarkPlan
        {
            ModelIds = ["model"],
            PromptSizes = [],
            GenerationSizes = [],
            PromptGenerationPairs = [new BenchmarkPromptGenerationPair(4096, 128)]
        };
        var item = WorkItem(EffectiveOptions());

        var args = BenchmarkCommandBuilder.Build(plan, item, "model.gguf").ToArray();

        Assert.Equal("0", ValueAfter(args, "--n-prompt"));
        Assert.Equal("0", ValueAfter(args, "--n-gen"));
        Assert.Equal("4096,128", ValueAfter(args, "-pg"));
    }

    [Fact]
    public void PreviewCountsUpstreamMatrixAndDeduplicatesEquivalentProfiles()
    {
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetTempPath()), "runtime") with
        {
            BatchSize = 2048,
            MicroBatchSize = 512,
            Threads = 8
        };
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profiles = new[]
        {
            new NamedModelLaunchProfile("p1", model.Id, "Default", settings, DateTimeOffset.UtcNow, true),
            new NamedModelLaunchProfile("p2", model.Id, "Reasoning", settings with { ReasoningMode = "off" }, DateTimeOffset.UtcNow)
        };
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var plan = new BenchmarkPlan
        {
            AllModels = true,
            AllProfiles = true,
            UseProfileRuntime = true,
            PromptSizes = [512, 2048],
            GenerationSizes = [128, 256],
            PromptGenerationPairs = [new BenchmarkPromptGenerationPair(4096, 128)],
            Depths = [0, 32768],
            Repetitions = 5,
            Options = new BenchmarkOptionSet { FlashAttention = ["on", "off"] }
        };

        var preview = new BenchmarkPlanService().Preview(plan, [model], profiles, [runtime]);

        Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.Errors));
        Assert.Single(preview.WorkItems);
        Assert.Equal(20, preview.ExpectedResultRows);
        Assert.Equal(100, preview.TimedRepetitions);
        Assert.Equal(1, preview.DeduplicatedWorkItems);
        Assert.Equal(2, preview.WorkItems[0].ProfileNames.Count);
    }

    [Fact]
    public void ResultParserSeparatesWorkloadFromEnvironment()
    {
        const string rowA = """{"build_commit":"a","build_number":1,"cpu_info":"CPU","gpu_info":"GPU","backends":"CUDA","devices":"CUDA0","model_filename":"m.gguf","model_type":"m","model_size":1,"model_n_params":2,"n_prompt":512,"n_gen":0,"n_depth":0,"n_batch":2048,"n_ubatch":512,"n_threads":8,"n_gpu_layers":-1,"n_cpu_moe":0,"type_k":"f16","type_v":"f16","split_mode":"layer","main_gpu":0,"no_kv_offload":false,"flash_attn":"on","tensor_split":"","load_mode":"mmap","avg_ns":100,"stddev_ns":2,"avg_ts":500.0,"stddev_ts":1.0,"test_time":"now"}""";
        var rowB = rowA.Replace("\"build_commit\":\"a\"", "\"build_commit\":\"b\"").Replace("\"build_number\":1", "\"build_number\":2");

        Assert.True(BenchmarkResultService.TryParse(
            rowA, "model-fingerprint", "command", RuntimeMode.Native, RuntimeBackend.Cuda,
            out var first, out var firstError, "v2.5.0", "Windows test host"), firstError);
        Assert.True(BenchmarkResultService.TryParse(rowB, "model-fingerprint", "command", RuntimeMode.Native, RuntimeBackend.Cuda, out var second, out var secondError), secondError);

        Assert.Equal(first!.WorkloadSignature, second!.WorkloadSignature);
        Assert.NotEqual(first.EnvironmentSignature, second.EnvironmentSignature);
        Assert.Equal(BenchmarkResultClassification.PromptProcessing, first.Classification);
        Assert.Equal("v2.5.0", first.ManagerVersion);
        Assert.Equal("Windows test host", first.OperatingEnvironment);
    }

    [Fact]
    public void ResultParserRejectsUnrelatedJsonObjects()
    {
        Assert.False(BenchmarkResultService.TryParse(
            "{\"progress\":50}", "model", "command", RuntimeMode.Native, RuntimeBackend.Cpu,
            out var parsed, out var error));
        Assert.Null(parsed);
        Assert.Contains("valid llama-bench", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComparisonUsesOnlyMatchingWorkloadsAndFlagsEnvironmentChanges()
    {
        const string json = """{"build_commit":"a","build_number":1,"cpu_info":"CPU","gpu_info":"GPU","backends":"CUDA","devices":"CUDA0","model_filename":"m.gguf","model_type":"m","model_size":1,"model_n_params":2,"n_prompt":512,"n_gen":0,"n_depth":0,"n_batch":2048,"n_ubatch":512,"n_threads":8,"n_gpu_layers":-1,"n_cpu_moe":0,"type_k":"f16","type_v":"f16","split_mode":"layer","main_gpu":0,"no_kv_offload":false,"flash_attn":"on","tensor_split":"","load_mode":"mmap","avg_ns":100,"stddev_ns":2,"avg_ts":500.0,"stddev_ts":1.0,"test_time":"now"}""";
        Assert.True(BenchmarkResultService.TryParse(json, "model", "command", RuntimeMode.Native, RuntimeBackend.Cuda, out var baseline, out _));
        Assert.True(BenchmarkResultService.TryParse(json.Replace("\"avg_ts\":500.0", "\"avg_ts\":550.0"),
            "model", "command", RuntimeMode.Native, RuntimeBackend.Cuda, out var candidate, out _, operatingEnvironment: "changed"));
        var now = DateTimeOffset.UtcNow;

        var rows = BenchmarkComparisonService.Compare(
            [new StoredBenchmarkResult(1, "a", "item", 1, 1, false, baseline!, now)],
            [new StoredBenchmarkResult(2, "b", "item", 1, 1, false, candidate!, now)]);

        var comparison = Assert.Single(rows);
        Assert.Equal(10, comparison.PercentChange, precision: 6);
        Assert.False(comparison.EnvironmentMatches);
    }

    [Fact]
    public void SpeedReportSeparatesPromptAndGenerationScalesAndAveragesRepetitions()
    {
        const string promptJson = """{"build_commit":"a","build_number":1,"cpu_info":"CPU","gpu_info":"GPU","backends":"CUDA","devices":"CUDA0","model_filename":"m.gguf","model_type":"m","model_size":1,"model_n_params":2,"n_prompt":512,"n_gen":0,"n_depth":0,"n_batch":2048,"n_ubatch":512,"n_threads":8,"n_gpu_layers":-1,"n_cpu_moe":0,"type_k":"f16","type_v":"f16","split_mode":"layer","main_gpu":0,"no_kv_offload":false,"flash_attn":"on","tensor_split":"","load_mode":"mmap","avg_ns":100,"stddev_ns":2,"avg_ts":500.0,"stddev_ts":1.0,"test_time":"now"}""";
        const string servingJson = """{"n_prompt":16384,"n_gen":512,"n_ctx":32768,"n_batch":2048,"avg_ts":70.0,"stddev_ts":1.2,"execution_mode":"profile_serving","profile_id":"default","profile_name":"Default","speculative_type":"none","concurrency":1,"request_count":5,"avg_prompt_ts":900.0,"avg_latency_ms":2200.0}""";
        Assert.True(BenchmarkResultService.TryParse(promptJson, "model", "direct", RuntimeMode.Native, RuntimeBackend.Cuda, out var prompt, out var promptError), promptError);
        Assert.True(BenchmarkResultService.TryParse(servingJson, "model", "profile", RuntimeMode.Native, RuntimeBackend.Cuda, out var serving, out var servingError), servingError);
        var now = DateTimeOffset.UtcNow;

        var sections = BenchmarkSpeedReportService.Build(
        [
            new StoredBenchmarkResult(1, "run", "direct", 1, 1, false, prompt!, now),
            new StoredBenchmarkResult(2, "run", "serving", 1, 1, false, serving!, now),
            new StoredBenchmarkResult(3, "run", "serving", 1, 2, false,
                serving! with { AverageTokensPerSecond = 80, AveragePromptTokensPerSecond = 1100 }, now),
            new StoredBenchmarkResult(4, "run", "partial", 1, 3, true,
                serving! with { AverageTokensPerSecond = 9999, AveragePromptTokensPerSecond = 9999 }, now)
        ]);

        var promptSection = Assert.Single(sections, section => section.Kind == BenchmarkSpeedReportKind.PromptProcessing);
        Assert.Equal(2, promptSection.Bars.Count);
        Assert.Contains(promptSection.Bars, bar => bar.TokensPerSecond == 500);
        Assert.Contains(promptSection.Bars, bar => bar.TokensPerSecond == 1000);
        var generationSection = Assert.Single(sections, section => section.Kind == BenchmarkSpeedReportKind.Generation);
        var servingBar = Assert.Single(generationSection.Bars);
        Assert.Equal(75, servingBar.TokensPerSecond);
        Assert.Equal("No speculative decoding", servingBar.ConfigurationLabel);
        Assert.DoesNotContain(sections.SelectMany(section => section.Bars), bar => bar.TokensPerSecond == 9999);
    }

    [Fact]
    public void SpeedReportMakesSpeculativeConfigurationsProminent()
    {
        const string servingJson = """{"n_prompt":8192,"n_gen":512,"n_ctx":65536,"n_batch":8162,"avg_ts":38.0,"execution_mode":"profile_serving","profile_id":"default","profile_name":"Default","speculative_type":"draft-dflash","concurrency":1,"request_count":5,"avg_prompt_ts":1200.0,"avg_latency_ms":20000.0}""";
        Assert.True(BenchmarkResultService.TryParse(servingJson, "model", "profile", RuntimeMode.Native, RuntimeBackend.Cuda, out var dflash, out var error), error);
        var mtp = dflash! with
        {
            WorkloadSignature = "mtp-workload",
            SpeculativeType = "draft-mtp",
            AverageTokensPerSecond = 36
        };
        var now = DateTimeOffset.UtcNow;

        var sections = BenchmarkSpeedReportService.Build(
        [
            new StoredBenchmarkResult(1, "run", "dflash", 1, 1, false, dflash, now),
            new StoredBenchmarkResult(2, "run", "mtp", 1, 2, false, mtp, now)
        ]);

        var generation = Assert.Single(sections, section => section.Kind == BenchmarkSpeedReportKind.Generation);
        Assert.Contains(generation.Bars, bar => bar.ConfigurationLabel == "DFlash2");
        Assert.Contains(generation.Bars, bar => bar.ConfigurationLabel == "MTP");
    }

    [Fact]
    public void AppOwnedExpertArgumentsAreRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            BenchmarkCommandBuilder.ValidateAdditionalArguments(["--output", "md"]));

        Assert.Contains("owned", error.Message, StringComparison.OrdinalIgnoreCase);
        var rpc = Assert.Throws<InvalidOperationException>(() =>
            BenchmarkCommandBuilder.ValidateAdditionalArguments(["--rpc", "127.0.0.1:50052"]));
        Assert.Contains("safety boundary", rpc.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpertAliasesAndStructuredConflictsAreRejected()
    {
        var duplicate = Assert.Throws<InvalidOperationException>(() =>
            BenchmarkCommandBuilder.ValidateAdditionalArguments(["-t", "4", "--threads", "8"]));
        Assert.Contains("duplicates", duplicate.Message, StringComparison.OrdinalIgnoreCase);

        var item = WorkItem(EffectiveOptions() with { AdditionalArguments = ["--batch-size", "1024"] });
        var conflict = Assert.Throws<InvalidOperationException>(() => BenchmarkCommandBuilder.Build(new BenchmarkPlan(), item, "model.gguf"));
        Assert.Contains("structured", conflict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandBuilderCoversAdvancedLlamaBenchOptions()
    {
        var options = EffectiveOptions() with
        {
            CpuMoeLayers = [8],
            FitTargetsMiB = [512],
            FitContexts = [4096],
            NumaModes = ["isolate"],
            Priorities = [2],
            CpuMasks = ["0xff"],
            CpuStrict = ["1"],
            PollValues = [75],
            Embeddings = ["1"],
            NoOpOffload = ["0"],
            NoHost = ["1"],
            TensorOverrides = ["blk\\.0\\..*=CUDA0"]
        };

        var arguments = BenchmarkCommandBuilder.Build(new BenchmarkPlan(), WorkItem(options), "model.gguf");

        Assert.Equal("8", ValueAfter(arguments, "--n-cpu-moe"));
        Assert.Equal("512", ValueAfter(arguments, "--fit-target"));
        Assert.Equal("4096", ValueAfter(arguments, "--fit-ctx"));
        Assert.Equal("isolate", ValueAfter(arguments, "--numa"));
        Assert.Equal("2", ValueAfter(arguments, "--prio"));
        Assert.Equal("0xff", ValueAfter(arguments, "--cpu-mask"));
        Assert.Equal("1", ValueAfter(arguments, "--cpu-strict"));
        Assert.Equal("75", ValueAfter(arguments, "--poll"));
        Assert.Equal("1", ValueAfter(arguments, "--embeddings"));
        Assert.Equal("0", ValueAfter(arguments, "--no-op-offload"));
        Assert.Equal("1", ValueAfter(arguments, "--no-host"));
        Assert.Equal("blk\\.0\\..*=CUDA0", ValueAfter(arguments, "--override-tensor"));
    }

    [Fact]
    public void PreviewSaturatesPathologicalMatrixWithoutOverflowing()
    {
        var values = Enumerable.Range(1, 64).ToArray();
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetTempPath()), "runtime");
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile("profile", model.Id, "Default", settings, DateTimeOffset.UtcNow, true);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, "llama-server.exe", "{}", DateTimeOffset.UtcNow);

        var preview = new BenchmarkPlanService().Preview(new BenchmarkPlan
        {
            AllModels = true,
            AllProfiles = true,
            UseProfileRuntime = true,
            PromptSizes = values,
            GenerationSizes = values,
            Depths = values,
            Options = new BenchmarkOptionSet { Threads = values, BatchSizes = values, MicroBatchSizes = values }
        }, [model], [profile], [runtime]);

        Assert.False(preview.IsValid);
        Assert.Equal(BenchmarkPlanService.MaximumResultRows + 1, preview.ExpectedResultRows);
        Assert.Contains(preview.Errors, error => error.Contains("result rows", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProfileServingPreviewPreservesDistinctProfilesAndCountsConcurrency()
    {
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetTempPath()), "runtime") with
        {
            SpeculativeType = "draft-mtp"
        };
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profiles = new[]
        {
            new NamedModelLaunchProfile("p1", model.Id, "MTP 4", settings with { SpecDraftMaxTokens = 4 }, DateTimeOffset.UtcNow, true),
            new NamedModelLaunchProfile("p2", model.Id, "MTP 8", settings with { SpecDraftMaxTokens = 8 }, DateTimeOffset.UtcNow)
        };
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var plan = new BenchmarkPlan
        {
            ExecutionMode = BenchmarkExecutionMode.ProfileServing,
            AllModels = true,
            AllProfiles = true,
            PromptSizes = [512, 2048],
            GenerationSizes = [128],
            Repetitions = 3,
            Serving = new BenchmarkServingOptions { Concurrencies = [1, 4] }
        };

        var preview = new BenchmarkPlanService().Preview(plan, [model], profiles, [runtime]);

        Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.Errors));
        Assert.Equal(2, preview.WorkItems.Count);
        Assert.Equal(8, preview.ExpectedResultRows);
        Assert.Equal(24, preview.TimedRepetitions);
        Assert.All(preview.WorkItems, item => Assert.Equal(BenchmarkExecutionMode.ProfileServing, item.ExecutionMode));
        Assert.NotEqual(preview.WorkItems[0].EffectiveCommandSignature, preview.WorkItems[1].EffectiveCommandSignature);
    }

    [Fact]
    public void ProfileServingExpandsOnlyExplicitLaunchVariablesFromSavedProfile()
    {
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetTempPath()), "runtime") with
        {
            ContextSize = 4096,
            BatchSize = 2048,
            MicroBatchSize = 256,
            Threads = 9,
            CacheTypeK = "q8_0",
            SpeculativeType = "draft-mtp",
            SpecDraftMaxTokens = 7
        };
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile("profile", model.Id, "Saved profile", settings, DateTimeOffset.UtcNow, true);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var plan = new BenchmarkPlan
        {
            ExecutionMode = BenchmarkExecutionMode.ProfileServing,
            AllModels = true,
            AllProfiles = true,
            PromptSizes = [512],
            GenerationSizes = [128],
            Options = new BenchmarkOptionSet { BatchSizes = [1024, 2048] },
            Serving = new BenchmarkServingOptions { ContextSizes = [8192, 16384], Concurrencies = [1] }
        };

        var preview = new BenchmarkPlanService().Preview(plan, [model], [profile], [runtime]);

        Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.Errors));
        Assert.Equal(4, preview.WorkItems.Count);
        Assert.Equal([(8192, 1024), (8192, 2048), (16384, 1024), (16384, 2048)],
            preview.WorkItems.Select(item => (item.LaunchSettings!.ContextSize, item.LaunchSettings.BatchSize)).ToArray());
        Assert.All(preview.WorkItems, item =>
        {
            Assert.Equal(256, item.LaunchSettings!.MicroBatchSize);
            Assert.Equal(9, item.LaunchSettings.Threads);
            Assert.Equal("q8_0", item.LaunchSettings.CacheTypeK);
            Assert.Equal("draft-mtp", item.LaunchSettings.SpeculativeType);
            Assert.Equal(7, item.LaunchSettings.SpecDraftMaxTokens);
        });
    }

    [Fact]
    public void ProfileServingExpandsPerformanceAndSpeculativeVariantsWithoutReusingWrongCompanions()
    {
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetTempPath()), "runtime") with
        {
            BatchSize = 512,
            MicroBatchSize = 512,
            Threads = 12,
            GpuLayers = 999,
            GpuMode = "layer",
            GpuDevices = "CUDA0,CUDA1",
            GpuSplit = "1,1",
            SpeculativeType = "draft-simple",
            SpecDraftModelPath = @"D:\models\saved-draft.gguf"
        };
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile("profile", model.Id, "Saved profile", settings, DateTimeOffset.UtcNow, true);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var plan = new BenchmarkPlan
        {
            ExecutionMode = BenchmarkExecutionMode.ProfileServing,
            AllModels = true,
            AllProfiles = true,
            PromptSizes = [512],
            GenerationSizes = [128],
            Options = new BenchmarkOptionSet
            {
                Threads = [8],
                MicroBatchSizes = [128, 256],
                GpuLayers = [-1],
                FlashAttention = ["on"],
                CacheTypesK = ["f16"],
                CacheTypesV = ["q8_0"],
                KvOffload = ["off"],
                SplitModes = ["none", "layer"],
                TensorSplits = ["1,1"]
            },
            Serving = new BenchmarkServingOptions
            {
                SpeculativeTypes = ["draft-simple", "draft-mtp", "none"],
                Concurrencies = [1]
            }
        };

        var preview = new BenchmarkPlanService().Preview(plan, [model], [profile], [runtime]);

        Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.Errors));
        Assert.Equal(12, preview.WorkItems.Count);
        Assert.All(preview.WorkItems, item =>
        {
            var launch = item.LaunchSettings!;
            Assert.Equal(8, launch.Threads);
            Assert.Contains(launch.MicroBatchSize, new[] { 128, 256 });
            Assert.Equal(999, launch.GpuLayers);
            Assert.Equal("on", launch.FlashAttention);
            Assert.Equal("f16", launch.CacheTypeK);
            Assert.Equal("q8_0", launch.CacheTypeV);
            Assert.Equal("off", launch.KvOffload);
            if (launch.GpuMode == "single")
            {
                Assert.Equal("CUDA0", launch.GpuDevices);
                Assert.Empty(launch.GpuSplit);
            }
            else
            {
                Assert.Equal("layer", launch.GpuMode);
                Assert.Equal("1,1", launch.GpuSplit);
            }
            if (launch.SpeculativeType == "draft-simple")
                Assert.Equal(settings.SpecDraftModelPath, launch.SpecDraftModelPath);
            else
            {
                Assert.Empty(launch.SpecDraftModelPath);
                Assert.Empty(launch.MtpHeadPath);
            }
        });
    }

    [Fact]
    public void PairedGpuConfigurationsRemainExactForServingAndDirectBenchmarks()
    {
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetTempPath()), "runtime") with
        {
            GpuMode = "row",
            GpuDevices = "CUDA0,CUDA1",
            GpuSplit = "2,1"
        };
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile("profile", model.Id, "Default", settings, DateTimeOffset.UtcNow, true);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var options = new BenchmarkOptionSet
        {
            GpuConfigurations =
            [
                new BenchmarkGpuConfiguration("tensor", "1,1"),
                new BenchmarkGpuConfiguration("layer")
            ]
        };
        var servingPlan = new BenchmarkPlan
        {
            ExecutionMode = BenchmarkExecutionMode.ProfileServing,
            AllModels = true,
            AllProfiles = true,
            PromptSizes = [512],
            GenerationSizes = [128],
            Options = options,
            Serving = new BenchmarkServingOptions { Concurrencies = [1] }
        };

        var serving = new BenchmarkPlanService().Preview(servingPlan, [model], [profile], [runtime]);

        Assert.True(serving.IsValid, string.Join(Environment.NewLine, serving.Errors));
        Assert.Equal(2, serving.WorkItems.Count);
        Assert.Contains(serving.WorkItems, item => item.LaunchSettings!.GpuMode == "tensor" && item.LaunchSettings.GpuSplit == "1,1");
        Assert.Contains(serving.WorkItems, item => item.LaunchSettings!.GpuMode == "layer" && item.LaunchSettings.GpuSplit == "");
        Assert.DoesNotContain(serving.WorkItems, item => item.LaunchSettings!.GpuMode == "layer" && item.LaunchSettings.GpuSplit == "1,1");

        var directPlan = servingPlan with
        {
            ExecutionMode = BenchmarkExecutionMode.LlamaBench,
            GenerationSizes = [],
            Serving = new BenchmarkServingOptions()
        };
        var direct = new BenchmarkPlanService().Preview(directPlan, [model], [profile], [runtime]);

        Assert.True(direct.IsValid, string.Join(Environment.NewLine, direct.Errors));
        Assert.Equal(2, direct.WorkItems.Count);
        Assert.Contains(direct.WorkItems, item => item.Options.SplitModes.SequenceEqual(["tensor"]) && item.Options.TensorSplits.SequenceEqual(["1,1"]));
        Assert.Contains(direct.WorkItems, item => item.Options.SplitModes.SequenceEqual(["layer"]) && item.Options.TensorSplits.Count == 0);
        Assert.All(direct.WorkItems, item => Assert.Equal(1, item.ExpectedResultRows));
    }

    [Fact]
    public void PairedSpeculativeConfigurationsRemainExactAndRejectLegacyCrossProducts()
    {
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetTempPath()), "runtime") with
        {
            SpeculativeType = "draft-simple",
            SpecDraftModelPath = @"D:\models\saved-draft.gguf"
        };
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile("profile", model.Id, "Default", settings, DateTimeOffset.UtcNow, true);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var plan = new BenchmarkPlan
        {
            ExecutionMode = BenchmarkExecutionMode.ProfileServing,
            AllModels = true,
            AllProfiles = true,
            PromptSizes = [512],
            GenerationSizes = [128],
            Serving = new BenchmarkServingOptions
            {
                SpeculativeConfigurations =
                [
                    new BenchmarkSpeculativeConfiguration("draft-simple", "profile"),
                    new BenchmarkSpeculativeConfiguration("atomic-mtp", "auto")
                ],
                Concurrencies = [1]
            }
        };

        var preview = new BenchmarkPlanService().Preview(plan, [model], [profile], [runtime]);

        Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.Errors));
        Assert.Equal(2, preview.WorkItems.Count);
        Assert.Contains(preview.WorkItems, item => item.LaunchSettings!.SpeculativeType == "draft-simple"
                                                  && item.LaunchSettings.SpecDraftModelPath == settings.SpecDraftModelPath);
        Assert.Contains(preview.WorkItems, item => item.LaunchSettings!.SpeculativeType == "atomic-mtp"
                                                  && string.IsNullOrEmpty(item.LaunchSettings.SpecDraftModelPath)
                                                  && string.IsNullOrEmpty(item.LaunchSettings.MtpHeadPath));

        var invalid = plan with
        {
            Serving = plan.Serving with
            {
                SpeculativeTypes = ["none"],
                SpeculativeCompanionModes = ["profile"]
            }
        };
        var validation = BenchmarkPlanService.ValidatePlan(invalid);
        Assert.Contains(validation, error => error.Contains("cannot be combined with legacy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PairedGpuConfigurationsRejectInvalidAndMismatchedSplits()
    {
        var invalidPlan = new BenchmarkPlan
        {
            PromptSizes = [512],
            Options = new BenchmarkOptionSet
            {
                GpuConfigurations =
                [
                    new BenchmarkGpuConfiguration("single", "1"),
                    new BenchmarkGpuConfiguration("tensor", "1,-1"),
                    new BenchmarkGpuConfiguration("Tensor", "1,-1")
                ]
            }
        };

        var validation = BenchmarkPlanService.ValidatePlan(invalidPlan);

        Assert.Contains(validation, error => error.Contains("Single GPU", StringComparison.Ordinal));
        Assert.Contains(validation, error => error.Contains("non-negative", StringComparison.Ordinal));
        Assert.Contains(validation, error => error.Contains("Duplicate GPU configuration", StringComparison.Ordinal));

        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetTempPath()), "runtime") with
        {
            GpuDevices = "CUDA0,CUDA1"
        };
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile("profile", model.Id, "Default", settings, DateTimeOffset.UtcNow, true);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var mismatchPlan = new BenchmarkPlan
        {
            AllModels = true,
            AllProfiles = true,
            PromptSizes = [512],
            Options = new BenchmarkOptionSet
            {
                GpuConfigurations = [new BenchmarkGpuConfiguration("tensor", "1,1,1")]
            }
        };

        var mismatch = new BenchmarkPlanService().Preview(mismatchPlan, [model], [profile], [runtime]);

        Assert.False(mismatch.IsValid);
        Assert.Contains(mismatch.Errors, error => error.Contains("has 3 entries", StringComparison.Ordinal)
                                                 && error.Contains("has 2 devices", StringComparison.Ordinal));
    }

    [Fact]
    public void ExactScopePairsProfilesWithRuntimesWithoutCrossProduct()
    {
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetTempPath()), "runtime-a");
        var profiles = new[]
        {
            new NamedModelLaunchProfile("profile-a", model.Id, "A", settings, DateTimeOffset.UtcNow, true),
            new NamedModelLaunchProfile("profile-b", model.Id, "B", settings, DateTimeOffset.UtcNow)
        };
        var runtimes = new[]
        {
            new RuntimeRecord("runtime-a", "Runtime A", RuntimeMode.Native, RuntimeBackend.Cpu, "llama-server.exe", "{}", DateTimeOffset.UtcNow),
            new RuntimeRecord("runtime-b", "Runtime B", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow)
        };
        var plan = new BenchmarkPlan
        {
            ModelIds = [model.Id],
            ProfileIds = profiles.Select(profile => profile.Id).ToArray(),
            RuntimeIds = runtimes.Select(runtime => runtime.Id).ToArray(),
            ScopeSelections =
            [
                new BenchmarkScopeSelection(model.Id, profiles[0].Id, runtimes[0].Id),
                new BenchmarkScopeSelection(model.Id, profiles[1].Id, runtimes[1].Id)
            ]
        };

        var preview = new BenchmarkPlanService().Preview(plan, [model], profiles, runtimes);

        Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.Errors));
        Assert.Equal(2, preview.WorkItems.Count);
        Assert.Contains(preview.WorkItems, item => item.ProfileIds.Single() == "profile-a" && item.RuntimeId == "runtime-a");
        Assert.Contains(preview.WorkItems, item => item.ProfileIds.Single() == "profile-b" && item.RuntimeId == "runtime-b");
    }

    [Fact]
    public void LinkedCacheTypesProduceMatchingPairsAndCompanionModesAreExplicit()
    {
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(Path.GetTempPath()), "runtime") with
        {
            SpeculativeType = "draft-simple",
            SpecDraftModelPath = @"D:\models\draft.gguf"
        };
        var model = new ModelRecord("model", "Model", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profile = new NamedModelLaunchProfile("profile", model.Id, "Default", settings, DateTimeOffset.UtcNow, true);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var direct = new BenchmarkPlan
        {
            AllModels = true,
            AllProfiles = true,
            Options = new BenchmarkOptionSet { CacheTypesKv = ["f16", "q8_0"] }
        };

        var directPreview = new BenchmarkPlanService().Preview(direct, [model], [profile], [runtime]);

        Assert.True(directPreview.IsValid, string.Join(Environment.NewLine, directPreview.Errors));
        Assert.Equal(2, directPreview.WorkItems.Count);
        Assert.Equal([("f16", "f16"), ("q8_0", "q8_0")], directPreview.WorkItems
            .Select(item => (item.Options.CacheTypesK.Single(), item.Options.CacheTypesV.Single()))
            .OrderBy(pair => pair.Item1)
            .ToArray());

        var serving = direct with
        {
            ExecutionMode = BenchmarkExecutionMode.ProfileServing,
            Options = new BenchmarkOptionSet(),
            Serving = new BenchmarkServingOptions
            {
                SpeculativeTypes = ["draft-simple"],
                SpeculativeCompanionModes = ["profile", "auto"],
                Concurrencies = [1]
            }
        };
        var servingPreview = new BenchmarkPlanService().Preview(serving, [model], [profile], [runtime]);
        Assert.True(servingPreview.IsValid, string.Join(Environment.NewLine, servingPreview.Errors));
        Assert.Equal(2, servingPreview.WorkItems.Count);
        Assert.Contains(servingPreview.WorkItems, item => item.LaunchSettings!.SpecDraftModelPath == settings.SpecDraftModelPath);
        Assert.Contains(servingPreview.WorkItems, item => string.IsNullOrEmpty(item.LaunchSettings!.SpecDraftModelPath));
    }

    [Fact]
    public void ProfileServingResultParserRestoresSpeculativeMeasurements()
    {
        const string row = """{"n_prompt":512,"n_gen":128,"n_ctx":32768,"avg_ts":72.5,"stddev_ts":1.2,"execution_mode":"profile_serving","profile_id":"mtp","profile_name":"MTP","speculative_type":"draft-mtp","concurrency":2,"request_count":6,"avg_prompt_ts":900.0,"avg_latency_ms":2200.0,"stddev_latency_ms":12.0,"draft_tokens":1000,"accepted_draft_tokens":750,"draft_acceptance_percent":75.0,"speculative_metrics_observed":true}""";

        Assert.True(BenchmarkResultService.TryParse(row, "model", "profile", RuntimeMode.Native, RuntimeBackend.Cuda, out var result, out var error), error);
        Assert.Equal(BenchmarkExecutionMode.ProfileServing, result!.ExecutionMode);
        Assert.Equal("draft-mtp", result.SpeculativeType);
        Assert.Equal(2, result.Concurrency);
        Assert.Equal(32768, result.ContextSize);
        Assert.Equal(75, result.DraftAcceptancePercent);
        Assert.True(result.SpeculativeMetricsObserved);
    }

    [Fact]
    public void ControlCliBuildsBenchmarkRunWaitAndExportRequests()
    {
        var planPath = Path.Combine(Path.GetTempPath(), $"benchmark-plan-{Guid.NewGuid():N}.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(
            new BenchmarkPlan { ModelIds = ["model"], RuntimeIds = ["runtime"] },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        try
        {
            var run = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests(
                "benchmarks", "run", "--plan", planPath, "--confirm", "--wait");
            var wait = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests("benchmarks", "wait", "run-id");
            var export = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests("benchmarks", "export", "run-id", "--format", "csv");
            var compare = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests("benchmarks", "compare", "baseline", "candidate");
            var delete = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests("benchmarks", "delete", "run-id", "--confirm");
            var capabilities = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests("benchmarks", "capabilities", "runtime", "--wsl-distro", "Ubuntu");

            Assert.Equal("POST", run.Method);
            Assert.Equal("/api/v1/benchmarks/run", run.Path);
            Assert.True(run.Body?["confirm"]?.GetValue<bool>());
            Assert.Equal("model", run.Body?["plan"]?["modelIds"]?[0]?.GetValue<string>());
            Assert.Equal(("GET", "/api/v1/benchmarks/run-id", null), wait);
            Assert.Equal("/api/v1/benchmarks/run-id/export?format=csv", export.Path);
            Assert.Equal("POST", compare.Method);
            Assert.Equal("baseline", compare.Body?["baselineRunId"]?.GetValue<string>());
            Assert.Equal("candidate", compare.Body?["candidateRunId"]?.GetValue<string>());
            Assert.Equal(("DELETE", "/api/v1/benchmarks/run-id?confirm=true", null), delete);
            Assert.Equal(("GET", "/api/v1/benchmarks/capabilities?runtime=runtime&wslDistro=Ubuntu", null), capabilities);
        }
        finally { File.Delete(planPath); }
    }

    private static BenchmarkWorkItem WorkItem(BenchmarkEffectiveOptions options)
        => new("key", "model", "Model", "model.gguf", "fingerprint", ["profile"], ["Default"],
            "runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, "llama-server.exe", "", options, "signature", 1);

    private static BenchmarkEffectiveOptions EffectiveOptions()
        => new(
            Threads: [], BatchSizes: [2048], MicroBatchSizes: [512], GpuLayers: [-1], CpuMoeLayers: [],
            FlashAttention: [], CacheTypesK: [], CacheTypesV: [], KvOffload: [], SplitModes: [], MainGpus: [],
            Devices: [], TensorSplits: [], LoadModes: [], FitTargetsMiB: [], FitContexts: [], NumaModes: [],
            Priorities: [], CpuMasks: [], CpuStrict: [], PollValues: [], Embeddings: [], NoOpOffload: [],
            NoHost: [], TensorOverrides: [], AdditionalArguments: []);

    private static string ValueAfter(IReadOnlyList<string> args, string option)
    {
        var index = args.ToList().IndexOf(option);
        Assert.True(index >= 0 && index + 1 < args.Count, $"Missing {option}");
        return args[index + 1];
    }
}
