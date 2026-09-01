using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.Tests;

public sealed class RuntimeLaunchOptionsTests
{
    [Fact]
    public async Task DiscoveryUsesCpuRuntimeHelpAndKeepsCpuSpecificOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "runtime-option-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "llama-server.exe");
        await File.WriteAllBytesAsync(executable, [0], TestContext.Current.CancellationToken);
        var runner = new RecordingProcessRunner(new ProcessRunResult(1, """
              --threads N          generation threads
              --threads-batch N    prompt processing threads
              --cpu-mask M         CPU affinity mask
              --numa TYPE          NUMA strategy
            """, ""));
        var service = new RuntimeLaunchOptionDiscoveryService(runner);
        var runtime = new RuntimeChoice("cpu", "Official CPU", RuntimeBackend.Cpu, RuntimeMode.Native, executable, "Official CPU");

        var options = await service.DiscoverAsync(runtime, "", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(options, option => option.Name == "--threads");
        Assert.Contains(options, option => option.Name == "--threads-batch");
        Assert.Contains(options, option => option.Name == "--cpu-mask");
        Assert.Contains(options, option => option.Name == "--numa");
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task DiscoveryPersistsRuntimeHelpFingerprintVersionBannerAndParseOutcome()
    {
        var root = Path.Combine(Path.GetTempPath(), "runtime-option-diagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "llama-server.exe");
        await File.WriteAllBytesAsync(executable, [0, 1, 2], TestContext.Current.CancellationToken);
        const string help = "llama-server version b9999\n  --cpu-mask M CPU affinity mask\n  --model PATH model";
        var runtime = new RuntimeChoice("official/cpu", "Official CPU", RuntimeBackend.Cpu, RuntimeMode.Native, executable, "Official CPU");
        var diagnostics = new RuntimeLaunchOptionDiagnosticsService(Path.Combine(root, "diagnostics"));
        var service = new RuntimeLaunchOptionDiscoveryService(
            new RecordingProcessRunner(new ProcessRunResult(0, help, "")),
            diagnostics);

        var options = await service.DiscoverAsync(runtime, "", TestContext.Current.CancellationToken);
        var diagnostic = System.Text.Json.JsonSerializer.Deserialize<RuntimeLaunchOptionDiagnostic>(
            await File.ReadAllTextAsync(diagnostics.DiagnosticPath(runtime), TestContext.Current.CancellationToken));

        Assert.Contains(options, option => option.Name == "--cpu-mask");
        Assert.NotNull(diagnostic);
        Assert.Equal("success", diagnostic.Status);
        Assert.Equal("llama-server version b9999", diagnostic.HelpBanner);
        Assert.Equal(2, diagnostic.ParsedOptionCount);
        Assert.Equal(1, diagnostic.RenderedOptionCount);
        Assert.Equal(64, diagnostic.HelpSha256.Length);
        Assert.Equal(3, diagnostic.ExecutableSizeBytes);
    }

    [Fact]
    public async Task DiscoveryRecordsChangedOrUnsupportedHelpFormat()
    {
        var root = Path.Combine(Path.GetTempPath(), "runtime-option-format", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "llama-server.exe");
        await File.WriteAllBytesAsync(executable, [0], TestContext.Current.CancellationToken);
        var runtime = new RuntimeChoice("cpu", "CPU", RuntimeBackend.Cpu, RuntimeMode.Native, executable, "CPU");
        var diagnostics = new RuntimeLaunchOptionDiagnosticsService(Path.Combine(root, "diagnostics"));
        var service = new RuntimeLaunchOptionDiscoveryService(
            new RecordingProcessRunner(new ProcessRunResult(0, "new help format without option markers", "")),
            diagnostics);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DiscoverAsync(runtime, "", TestContext.Current.CancellationToken));
        var diagnostic = System.Text.Json.JsonSerializer.Deserialize<RuntimeLaunchOptionDiagnostic>(
            await File.ReadAllTextAsync(diagnostics.DiagnosticPath(runtime), TestContext.Current.CancellationToken));

        Assert.Contains("help format changed or is unsupported", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(diagnostic);
        Assert.Equal("unrecognized-help", diagnostic.Status);
        Assert.Equal(0, diagnostic.ParsedOptionCount);
    }

    [Fact]
    public async Task DiscoveryReportsMissingRuntimeBeforeStartingAProcess()
    {
        var runner = new RecordingProcessRunner(new ProcessRunResult(0, "", ""));
        var service = new RuntimeLaunchOptionDiscoveryService(runner);
        var runtime = new RuntimeChoice(
            "missing",
            "Missing CPU",
            RuntimeBackend.Cpu,
            RuntimeMode.Native,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "llama-server.exe"),
            "Missing CPU");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DiscoverAsync(runtime, "", TestContext.Current.CancellationToken));

        Assert.Contains("missing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Repair or reinstall", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public void HelpParserPreservesExactAliasesAndInfersChoices()
    {
        const string help = """
              -c,    --ctx-size N            context size
                  --flash-attn [auto|on|off] flash attention mode (default: auto)
                  --slot-save-path PATH      save slots here
                  --metrics                  enable metrics (default: false)
            """;

        var options = RuntimeLaunchHelpParser.Parse(help);

        var context = Assert.Single(options, option => option.Name == "--ctx-size");
        Assert.Equal(["-c", "--ctx-size"], context.Aliases);
        Assert.Equal(RuntimeLaunchOptionValueKind.Text, context.ValueKind);
        var flash = Assert.Single(options, option => option.Name == "--flash-attn");
        Assert.Equal(RuntimeLaunchOptionValueKind.Choice, flash.ValueKind);
        Assert.Equal(["auto", "on", "off"], flash.Choices);
        Assert.Equal("auto", flash.DefaultValue);
        var metrics = Assert.Single(options, option => option.Name == "--metrics");
        Assert.Equal(RuntimeLaunchOptionValueKind.Switch, metrics.ValueKind);
        Assert.Equal("false", metrics.DefaultValue);
    }

    [Fact]
    public void HelpParserTreatsDescriptiveDefaultsAndDisableValuesAsNumericTextNotChoices()
    {
        var options = RuntimeLaunchHelpParser.Parse("""
            --dry-multiplier N       set DRY multiplier (default: 0.00, 0.0 = disabled)
            --poll <0...100>         polling level (0 - no polling, default: 50)
            --tags STRING            comma-separated (informational, not used for routing)
            """);

        Assert.All(options, option =>
        {
            Assert.Equal(RuntimeLaunchOptionValueKind.Text, option.ValueKind);
            Assert.Empty(option.Choices);
        });
        Assert.Equal("0.00", Assert.Single(options, option => option.Name == "--dry-multiplier").DefaultValue);
        Assert.Equal("50", Assert.Single(options, option => option.Name == "--poll").DefaultValue);
    }

    [Fact]
    public void HelpParserKeepsPairedAliasesAndMultilineDefaultsWithoutTreatingReferencesAsOptions()
    {
        var options = RuntimeLaunchHelpParser.Parse("""
            -ag,   --agent, -no-ag, --no-agent      whether to enable tools
                                                    (default: disabled)
            --cpu-strict-batch <0|1>                same as --cpu-strict
                                                    (default: 0)
            --spec-ngram-size-n N                   argument removed; use
                                                    --spec-ngram-*-size-n
            """);

        var agent = Assert.Single(options, option => option.Name == "--agent");
        Assert.Equal(RuntimeLaunchOptionValueKind.Switch, agent.ValueKind);
        Assert.Equal(["-ag", "--agent", "-no-ag", "--no-agent"], agent.Aliases);
        Assert.Empty(agent.ValueHint);
        Assert.Equal("disabled", agent.DefaultValue);
        var strict = Assert.Single(options, option => option.Name == "--cpu-strict-batch");
        Assert.Equal(["0", "1"], strict.Choices);
        Assert.Equal("0", strict.DefaultValue);
        Assert.DoesNotContain(options, option => option.Name.StartsWith("--spec-ngram-", StringComparison.Ordinal)
                                                  && option.Name != "--spec-ngram-size-n");
    }

    [Fact]
    public void HelpParserOnlyMakesRealEnumerationsIntoChoices()
    {
        var options = RuntimeLaunchHelpParser.Parse("""
            --fit [on|off]                       fit mode
            --pooling {none,mean,cls,last,rank} pooling mode
            --device <dev1,dev2,..>             comma-separated devices
            --numa TYPE                         NUMA mode
                                                - distribute: spread across nodes
                                                - isolate: stay on one node
                                                - numactl: use the supplied map
            --mirostat N                        mode (default: 0, 0 = disabled, 1 = Mirostat, 2 = Mirostat 2.0)
            --prio N                            priority: low(-1), normal(0), medium(1), high(2), realtime(3)
            """);

        Assert.Equal(["on", "off"], Assert.Single(options, option => option.Name == "--fit").Choices);
        Assert.Equal(["none", "mean", "cls", "last", "rank"], Assert.Single(options, option => option.Name == "--pooling").Choices);
        var device = Assert.Single(options, option => option.Name == "--device");
        Assert.Equal(RuntimeLaunchOptionValueKind.Text, device.ValueKind);
        Assert.Empty(device.Choices);
        Assert.Equal(["distribute", "isolate", "numactl"], Assert.Single(options, option => option.Name == "--numa").Choices);
        Assert.Equal(["0", "1", "2"], Assert.Single(options, option => option.Name == "--mirostat").Choices);
        Assert.Equal(["-1", "0", "1", "2", "3"], Assert.Single(options, option => option.Name == "--prio").Choices);
    }

    [Fact]
    public void HelpParserUsesFileEditorsForSingularFnameValuesButNotCompositeLists()
    {
        var options = RuntimeLaunchHelpParser.Parse("""
            --lora FNAME                  path to a LoRA adapter
            --lora-scaled FNAME:SCALE,...   adapter paths and scales
            """);

        Assert.Equal(RuntimeLaunchOptionValueKind.File, Assert.Single(options, option => option.Name == "--lora").ValueKind);
        Assert.Equal(RuntimeLaunchOptionValueKind.Text, Assert.Single(options, option => option.Name == "--lora-scaled").ValueKind);
    }

    [Theory]
    [InlineData("--agent", "whether to enable tools")]
    [InlineData("--mcp-servers-config", "MCP definitions")]
    [InlineData("--cache-list", "show list of models")]
    [InlineData("--path", "serve static files")]
    [InlineData("--api-prefix", "change the served route")]
    [InlineData("--tools-runtime", "run tools on the host")]
    [InlineData("--docker-repo", "model repository")]
    [InlineData("--old-option", "DEPRECATED in favor of another option")]
    [InlineData("--draft", "the argument has been removed")]
    [InlineData("--preset", "can download weights from the internet")]
    public void PolicyHidesSecuritySensitiveActionAndUnsupportedRuntimeOptions(string name, string description)
    {
        var option = new RuntimeLaunchOptionDefinition(name, [name], "", description, RuntimeLaunchOptionValueKind.Switch, []);

        Assert.False(RuntimeLaunchOptionPolicy.CanRender(option));
    }

    [Fact]
    public void SlotSavePathUsesDirectoryPicker()
    {
        var option = Assert.Single(RuntimeLaunchHelpParser.Parse("--slot-save-path PATH   path to save slot kv cache"));

        Assert.Equal(RuntimeLaunchOptionValueKind.Directory, option.ValueKind);
    }

    [Fact]
    public void PolicyOnlyRendersSafeUnmanagedOptions()
    {
        var parsed = RuntimeLaunchHelpParser.Parse("""
              --model PATH             model
              --host HOST              listener
              --slot-save-path PATH    slot directory
              --help                   show help
            """);

        var rendered = parsed.Where(RuntimeLaunchOptionPolicy.CanRender).Select(option => option.Name).ToArray();

        Assert.Equal(["--slot-save-path"], rendered);
    }

    [Fact]
    public void RuntimeOptionsAreGroupedIntoStablePolishedSectionsWithoutDroppingUnknownFlags()
    {
        RuntimeLaunchOptionDefinition Option(string name, string description)
            => new(name, [name], "VALUE", description, RuntimeLaunchOptionValueKind.Text, []);

        var groups = RuntimeLaunchOptionGroupingService.Group([
            Option("--cpu-mask", "CPU affinity mask"),
            Option("--samplers", "sampler sequence"),
            Option("--slot-save-path", "directory used to save slots"),
            Option("--draft-max", "maximum speculative draft tokens"),
            Option("--vendor-experimental", "vendor-specific behavior")
        ]);

        Assert.Equal([
            "Performance & Memory",
            "Generation & Sampling",
            "Speculative & Draft",
            "Server & Slots",
            "Other Runtime Options"
        ], groups.Select(group => group.Title));
        Assert.Equal(5, groups.Sum(group => group.Options.Count));
        Assert.Equal("--vendor-experimental", Assert.Single(groups[^1].Options).Name);
    }

    [Theory]
    [InlineData("--cpu-mask", "CPU Mask")]
    [InlineData("--ctx-size", "Ctx Size")]
    [InlineData("--kv-unified", "KV Unified")]
    [InlineData("--mtp-head", "MTP Head")]
    [InlineData("--rope-freq-base", "RoPE Freq Base")]
    public void RuntimeOptionLabelsAreReadableWhileExactFlagsRemainUnchanged(string flag, string expectedLabel)
    {
        Assert.Equal(expectedLabel, LaunchSettingMetadataService.RuntimeOptionLabel(flag));
        Assert.StartsWith("--", flag, StringComparison.Ordinal);
    }

    [Fact]
    public void PositiveAndNegativeRuntimeSwitchesBecomeOneHonestTriStateControl()
    {
        var normalized = RuntimeLaunchOptionSwitchService.Normalize([
            new RuntimeLaunchOptionDefinition("--log-colors", ["--log-colors"], "", "enable colors", RuntimeLaunchOptionValueKind.Switch, []),
            new RuntimeLaunchOptionDefinition("--no-log-colors", ["--no-log-colors"], "", "disable colors", RuntimeLaunchOptionValueKind.Switch, [])
        ]);

        var option = Assert.Single(normalized);
        Assert.Equal("--log-colors", option.Name);
        Assert.Equal("--log-colors", option.EnabledName);
        Assert.Equal("--no-log-colors", option.DisabledName);
        Assert.Equal(["--log-colors", "--no-log-colors"], option.Aliases);
        Assert.Equal("Log Colors", LaunchSettingMetadataService.RuntimeOptionLabel(RuntimeLaunchOptionSwitchService.DisplayFlag(option)));
    }

    [Fact]
    public void UnpairedRuntimeSwitchesExposeOnlyTheirAdvertisedDirection()
    {
        var enableOnly = Assert.Single(RuntimeLaunchOptionSwitchService.Normalize([
            new RuntimeLaunchOptionDefinition("--verbose", ["--verbose"], "", "verbose output", RuntimeLaunchOptionValueKind.Switch, [])
        ]));
        var disableOnly = Assert.Single(RuntimeLaunchOptionSwitchService.Normalize([
            new RuntimeLaunchOptionDefinition("--no-colors", ["--no-colors"], "", "disable colors", RuntimeLaunchOptionValueKind.Switch, [])
        ]));

        Assert.Equal("--verbose", enableOnly.EnabledName);
        Assert.Empty(enableOnly.DisabledName);
        Assert.Empty(disableOnly.EnabledName);
        Assert.Equal("--no-colors", disableOnly.DisabledName);
        Assert.Equal("--colors", RuntimeLaunchOptionSwitchService.DisplayFlag(disableOnly));
    }

    [Theory]
    [InlineData("--model")]
    [InlineData("--model=other.gguf")]
    [InlineData("--port")]
    [InlineData("--api-key")]
    public void ManagedArgumentsCannotBeOverridden(string argument)
    {
        var error = Assert.Throws<InvalidOperationException>(() => RuntimeLaunchOptionPolicy.ValidateCustomArguments([argument]));
        Assert.Contains("managed by the application", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreviewUsesTheSameLlamaCppLaunchAsLaunch()
    {
        var root = Path.Combine(Path.GetTempPath(), "runtime-preview-tests");
        var settings = AppSettings.CreateDefault(root) with
        {
            ContextSize = 8192,
            Temperature = 0.4,
            ModelAccessMode = "models",
            Host = "10.10.10.21",
            CustomParameters = "--slot-save-path \"C:\\slot cache\""
        };
        var runtime = new RuntimeChoice("runtime", "Runtime", RuntimeBackend.Cpu, RuntimeMode.Native, "llama-server.exe");

        var preview = RuntimeLaunchRequestFactory.Preview(settings, runtime);

        Assert.Contains("--model <model.gguf>", preview, StringComparison.Ordinal);
        Assert.Contains("--ctx-size 8192", preview, StringComparison.Ordinal);
        Assert.Contains("--temp 0.4", preview, StringComparison.Ordinal);
        Assert.Contains("--host 10.10.10.21", preview, StringComparison.Ordinal);
        Assert.Contains("--slot-save-path \"C:\\\\slot cache\"", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeLaunchRequestFallbacksUseApplicationDefaults()
    {
        var request = new RuntimeLaunchRequest
        {
            Mode = RuntimeMode.Native,
            Backend = RuntimeBackend.Cpu,
            ExecutablePath = "llama-server.exe",
            ModelPath = "model.gguf"
        };

        Assert.Equal(AppSettings.DefaultBatchSize, request.BatchSize);
        Assert.Equal(AppSettings.DefaultCacheType, request.CacheTypeK);
        Assert.Equal(AppSettings.DefaultCacheType, request.CacheTypeV);
        Assert.Equal(AppSettings.DefaultTemperature, request.Temperature);
    }

    [Fact]
    public void SharedShellTokenizerPreservesTheTwoDocumentedParsingModes()
    {
        var strict = ShellArgumentTokenizer.Tokenize(
            "--path \"D:\\Model Files\\model.gguf\" --joined\\ value ''",
            ShellTokenizationMode.StrictArguments);
        var suggestion = ShellArgumentTokenizer.Tokenize(
            "--path \"D:\\Model Files\\model.gguf\" --joined\\ value ''",
            ShellTokenizationMode.CommandSuggestion);

        Assert.Equal(["--path", "D:\\Model Files\\model.gguf", "--joined value", ""], strict);
        Assert.Equal(["--path", "D:\\Model Files\\model.gguf", "--joined", "value"], suggestion);
        Assert.Throws<InvalidOperationException>(() => ShellArgumentTokenizer.Tokenize(
            "--path \"unterminated",
            ShellTokenizationMode.StrictArguments));
        Assert.Equal(["--path", "unterminated"], ShellArgumentTokenizer.Tokenize(
            "--path \"unterminated",
            ShellTokenizationMode.CommandSuggestion));
    }

    [Fact]
    public void CuratedSchemaHasUniqueValidSettingsAndChoiceMetadata()
    {
        var definitions = LaunchSettingUiSchema.Definitions;
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(definitions, definition => Assert.NotNull(typeof(AppSettings).GetProperty(definition.Id)));
        Assert.All(definitions.Where(definition => definition.Editor == LaunchSettingEditorKind.Choice),
            definition => Assert.NotEmpty(definition.Choices ?? []));
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
