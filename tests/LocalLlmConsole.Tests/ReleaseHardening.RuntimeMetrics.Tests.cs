using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void RuntimeMetricsParseAndAggregatePrometheusSamples()
    {
        const string raw = """
        # HELP llama_tokens_predicted_total Predicted tokens.
        # TYPE llama_tokens_predicted_total counter
        llama_tokens_predicted_total 12
        llama_prompt_tokens_seconds{slot="0"} 3.5
        llama_kv_cache_usage_ratio NaN
        """;

        var samples = RuntimeMetrics.ParsePrometheus(raw);

        Assert.Equal(3, samples.Count);
        Assert.Equal(12, RuntimeMetrics.Sum(samples, ["tokens", "predicted", "total"], []));
        Assert.Equal(3.5, RuntimeMetrics.First(samples, ["prompt", "tokens", "seconds"], ["total"]));
        Assert.Null(RuntimeMetrics.First(samples, ["kv", "cache", "usage"], []));
        Assert.Equal("counter", samples.Single(sample => sample.Name == "llama_tokens_predicted_total").Type);
    }

    [Fact]
    public void RuntimeTokenSummarySeparatesCachedPromptTotalsFromProcessedPromptRate()
    {
        var samples = new[]
        {
            new PrometheusSample("llamacpp:tokens_predicted_total", "", 40, "40", "counter", ""),
            new PrometheusSample("llamacpp:tokens_predicted_seconds_total", "", 4, "4", "counter", ""),
            new PrometheusSample("llamacpp:prompt_tokens_total", "", 100, "100", "counter", ""),
            new PrometheusSample("llamacpp:prompt_tokens_cached_total", "", 900, "900", "counter", ""),
            new PrometheusSample("llamacpp:prompt_seconds_total", "", 2, "2", "counter", ""),
            new PrometheusSample("llamacpp:prompt_tokens_seconds", "", 0, "0", "gauge", ""),
            new PrometheusSample("llamacpp:predicted_tokens_seconds", "", 0, "0", "gauge", "")
        };

        Assert.Equal(100, RuntimeDashboardService.PromptTokensProcessedCounter(samples));
        Assert.Equal(900, RuntimeDashboardService.PromptCachedTokenCounter(samples));
        Assert.Equal(1000, RuntimeDashboardService.PromptActivityTokenCounter(samples));

        var summary = new RuntimeMetricSummaryTracker().Apply(
            "model|runtime|8081",
            samples,
            AppSettings.CreateDefault(CreateTempRoot()),
            slotSnapshot: null,
            mtpTokenSnapshot: null,
            DateTimeOffset.Parse("2026-08-15T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal("Generated: 10.0 t/s | Total generated: 40\nPrompt: 50.0 t/s | Total prompt: 100 | Cache hit: 900", summary.Tokens);
        Assert.Equal(10, summary.GraphSample.GenerationRate);
        Assert.Equal(50, summary.GraphSample.PromptRate);
    }


    [Fact]
    public void RuntimeDashboardServiceParsesSlotsAndFormatsLabels()
    {
        const string raw = """
        [
          {
            "is_processing": true,
            "n_prompt_tokens_processed": 12,
            "n_decoded": 8,
            "n_prompt_tokens": "20",
            "n_ctx": 4096,
            "n_draft_tokens": 9,
            "n_draft_tokens_accepted": 6
          },
          {
            "next_token": [
              { "n_decoded": 5, "has_next_token": true }
            ],
            "prompt_tokens_processed": 3,
            "context_size": "2048"
          }
        ]
        """;

        var snapshot = RuntimeDashboardService.ParseSlotSnapshot(raw);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsProcessing);
        Assert.Equal(15, snapshot.PromptTokensProcessed);
        Assert.Equal(13, snapshot.GeneratedTokens);
        Assert.Equal(20, snapshot.PromptTokens);
        Assert.Equal(36, snapshot.ContextTokens);
        Assert.Equal(4096, snapshot.ContextSize);
        Assert.Equal(6144, snapshot.ContextCapacityTokens);
        Assert.Equal(9, snapshot.MtpGeneratedTokens);
        Assert.Equal(6, snapshot.MtpAcceptedTokens);
        Assert.NotNull(snapshot.SlotCounters);
        Assert.Equal(["0", "1"], snapshot.SlotCounters.Select(counter => counter.SlotId).ToArray());
        Assert.Equal([8, 5], snapshot.SlotCounters.Select(counter => counter.GeneratedTokens).ToArray());
        Assert.Equal(2, RuntimeDashboardService.DeltaRate(14, 10, 2, includeZero: false));
        Assert.Null(RuntimeDashboardService.DeltaRate(10, 10, 2, includeZero: false));
        Assert.Equal(0, RuntimeDashboardService.DeltaRate(10, 10, 2, includeZero: true));
        Assert.Equal(4, RuntimeDashboardService.WholePositiveDelta(7.9, 3.1));
        double? lifetimeCounter = 10;
        Assert.Equal(0, RuntimeDashboardService.WholePositiveDeltaAndRemember(null, ref lifetimeCounter));
        Assert.Equal(10, lifetimeCounter);
        Assert.Equal(5, RuntimeDashboardService.WholePositiveDeltaAndRemember(15.9, ref lifetimeCounter));
        Assert.Equal(15.9, lifetimeCounter);
        Assert.Equal(0, RuntimeDashboardService.WholePositiveDeltaAndRemember(2, ref lifetimeCounter));
        Assert.Equal(2, lifetimeCounter);
        Assert.True(RuntimeDashboardService.PositiveDelta(4, 3));
        Assert.Equal("Gen 13\nPrompt 15", RuntimeDashboardService.TokenSummaryLabel(13, 15));
        Assert.Equal("2.0 t/s (Gen) | 3.0 t/s (Avg) | 13 t (Total)\nUnknown (Prompt) | 15 t (Total)", RuntimeDashboardService.TokenActivitySummaryLabel(2, 3, null, null, 13, 15));
        Assert.Equal("5.0 t/s (Gen) | 20 t (Total)\n0.0 t/s (Prompt) | 10 t (Total)", RuntimeDashboardService.TokenActivitySummaryLabel(5, 0, 0, 0, 20, 10));
        Assert.Equal(
            "Active 1/1 | Queued 0\nBusy/decode 1.5",
            RuntimeDashboardService.RuntimeSlotsLabel(
            [
                new PrometheusSample("llamacpp:requests_processing", "", 1, "1", "gauge", ""),
                new PrometheusSample("llamacpp:requests_deferred", "", 0, "0", "gauge", ""),
                new PrometheusSample("llamacpp:n_busy_slots_per_decode", "", 1.5, "1.5", "gauge", "")
            ]));
        Assert.Equal(
            "Active 2/2 | Queued 0\nBusy/decode 2.0",
            RuntimeDashboardService.RuntimeSlotsLabel([], snapshot));
        Assert.Equal("2.0 t/s (Gen) | 3.0 t/s (Avg) | 9 t (Total)\n1.5 t/s (Accepted) | 2.5 t/s (Avg) | 6 t (Total)", RuntimeDashboardService.MtpTokenSummaryLabel(2, 3, 1.5, 2.5, 9, 6));
        var parsedMtpStats = RuntimeDashboardService.ParseMtpTokenStats(
            "statistics        draft-mtp: #calls(b,g,a) =  566 142602 107915, #gen drafts = 107915, #acc drafts = 103668, #gen tokens = 294686, #acc tokens = 274174, dur(b,g,a) = 0.412, 851457.082, 118.639 ms");
        Assert.NotNull(parsedMtpStats);
        Assert.Equal(294686, parsedMtpStats.GeneratedTokens);
        Assert.Equal(274174, parsedMtpStats.AcceptedTokens);
        Assert.Equal(851.457082, parsedMtpStats.GeneratedSeconds);
        Assert.Equal(851.457082, parsedMtpStats.AcceptedSeconds);
        Assert.Equal(
            new RuntimeMtpTokenSnapshot(297, 171),
            RuntimeDashboardService.ParseMtpTokenStats("draft acceptance rate = 0.57576 (  171 accepted /   297 generated)"));
        Assert.Equal("2.0 t/s (3.0 avg)", RuntimeDashboardService.RateLabel(2, 3));
        Assert.Equal("Context 6,144 total\nKV cache 50%, 28 tokens", RuntimeDashboardService.RuntimeSettingsLabel(.5, 28, 6144, 4096));
        Assert.Equal("Context 195,584 total\nSlots: 3 enabled\nKV cache 8,325 tokens", RuntimeDashboardService.RuntimeSettingsLabel(null, 8325, 586752, 195584, 3, "on"));
        Assert.Equal("Context 586,752 total\nSlots: 3 enabled\nKV cache Unknown", RuntimeDashboardService.RuntimeSettingsLabel(null, null, 195584, 195584, 3, "off"));
        Assert.Equal("Context 195,584 total\nSlots: 3 enabled\nKV cache Unknown", RuntimeDashboardService.RuntimeSettingsLabel(null, null, 195584, 0, 3));
        Assert.Equal("Used 28 t | 50%\nCapacity 6,144 t | unified", RuntimeDashboardService.RuntimeKvCacheLabel(.5, 28, 6144, "on"));
        Assert.Equal("Used 8,325 t | 4.3%\nCapacity 195,584 t | partitioned", RuntimeDashboardService.RuntimeKvCacheLabel(null, 8325, 195584, "off"));
        Assert.Equal(4.2565, RuntimeDashboardService.KvCacheUsagePercent(null, 8325, 195584)!.Value, 4);
    }


    [Fact]
    public void RuntimeDashboardServiceUsesCachedPromptTotalsForSlotContext()
    {
        const string raw = """
        [
          {
            "id": 0,
            "is_processing": false,
            "n_prompt_tokens": 0,
            "n_prompt_tokens_processed": 1109,
            "n_prompt_tokens_cache": 0,
            "next_token": [{ "has_next_token": false, "n_decoded": 3908 }],
            "n_ctx": 195584
          },
          {
            "id": 1,
            "id_task": 23124,
            "is_processing": true,
            "n_prompt_tokens": 87148,
            "n_prompt_tokens_processed": 543,
            "n_prompt_tokens_cache": 86569,
            "next_token": [{ "has_next_token": true, "n_decoded": 37 }],
            "n_ctx": 195584
          }
        ]
        """;

        var snapshot = RuntimeDashboardService.ParseSlotSnapshot(raw);

        Assert.NotNull(snapshot);
        Assert.Equal(1652, snapshot.PromptTokensProcessed);
        Assert.Equal(3945, snapshot.GeneratedTokens);
        Assert.Equal(87148, snapshot.PromptTokens);
        Assert.Equal(92202, snapshot.ContextTokens);
        Assert.Equal("23124", snapshot.SlotCounters?.Single(counter => counter.SlotId == "1").TaskId);
    }


    [Fact]
    public void RuntimeLogTailServiceBuildsMissingLiveAndSlotAwareLogText()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "runtime.log");
        var service = new RuntimeLogTailService();

        var missingLive = service.Build(new RuntimeLogTailRequest(path, IsRuntimeRunning: true, SlotSnapshot: null));
        var missingStopped = service.Build(new RuntimeLogTailRequest(path, IsRuntimeRunning: false, SlotSnapshot: null));
        Directory.CreateDirectory(root);
        File.WriteAllText(path, "start\nall slots are idle\nALL SLOTS ARE IDLE\ndone");
        var processing = service.Build(new RuntimeLogTailRequest(
            path,
            IsRuntimeRunning: true,
            new RuntimeSlotSnapshot(
                PromptTokensProcessed: 12,
                GeneratedTokens: 8,
                IsProcessing: true,
                PromptTokens: 20,
                ContextTokens: 28,
                ContextSize: 4096)));
        var idle = service.Build(new RuntimeLogTailRequest(
            path,
            IsRuntimeRunning: false,
            new RuntimeSlotSnapshot(0, 0, IsProcessing: false, null, null, null)));

        Assert.False(missingLive.HasActiveLog);
        Assert.Equal("Runtime log file has not been created yet.", missingLive.Text);
        Assert.False(missingStopped.HasActiveLog);
        Assert.Equal("No runtime log is active.", missingStopped.Text);
        Assert.True(processing.HasActiveLog);
        Assert.Contains($"Live log: {path}", processing.Text, StringComparison.Ordinal);
        Assert.Contains("Slot status: processing | Prompt 12/20 | Gen 8", processing.Text, StringComparison.Ordinal);
        Assert.Contains("start", processing.Text, StringComparison.Ordinal);
        Assert.Contains("omitted 2 repeated 'all slots are idle' lines", processing.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ALL SLOTS ARE IDLE", processing.Text, StringComparison.Ordinal);
        Assert.True(idle.HasActiveLog);
        Assert.Contains($"Last runtime log: {path}", idle.Text, StringComparison.Ordinal);
        Assert.Contains("Slot status: idle", idle.Text, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeLogTailCaptureIsReusableAndReadOffTheRenderPath()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "runtime.log");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(path, "captured line", TestContext.Current.CancellationToken);
        var service = new RuntimeLogTailService();

        var capture = await service.CaptureAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        await File.AppendAllTextAsync(path, Environment.NewLine + "later line", TestContext.Current.CancellationToken);
        var rendered = service.Build(new RuntimeLogTailRequest(path, true, null), capture);

        Assert.True(capture.Exists);
        Assert.Contains("captured line", rendered.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("later line", rendered.Text, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeOverviewStatusServiceBuildsStoppedLoadedWarmAndFailedLabels()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var selected = new ModelRecord("model-1", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var service = new RuntimeOverviewStatusService();

        var noSelection = service.Labels(new RuntimeOverviewStatusRequest(null, null, LlamaRuntimeState.Stopped, null));
        var stoppedSelection = service.Labels(new RuntimeOverviewStatusRequest(selected, null, LlamaRuntimeState.Stopped, null));
        var running = service.Labels(new RuntimeOverviewStatusRequest(selected, RuntimeSession(root, settings, LoadedModelSessionStatus.Running, true), LlamaRuntimeState.Loaded, null));
        var warm = service.Labels(new RuntimeOverviewStatusRequest(selected, RuntimeSession(root, settings, LoadedModelSessionStatus.Warm, true), LlamaRuntimeState.Loaded, null));
        var loading = service.Labels(new RuntimeOverviewStatusRequest(selected, RuntimeSession(root, settings, LoadedModelSessionStatus.Loading, true), LlamaRuntimeState.Loading, null));
        var failed = service.Labels(new RuntimeOverviewStatusRequest(selected, RuntimeSession(root, settings, LoadedModelSessionStatus.Failed, false) with { RuntimeName = "" }, LlamaRuntimeState.Failed, 17));

        Assert.Equal(new RuntimeOverviewStatusLabels("None", "Stopped"), noSelection);
        Assert.Equal(new RuntimeOverviewStatusLabels("Stopped: Qwen", "No loaded runtime"), stoppedSelection);
        Assert.Equal(new RuntimeOverviewStatusLabels("Loaded: Qwen", "Runtime"), running);
        Assert.Equal(new RuntimeOverviewStatusLabels("Loaded: Qwen", "Runtime"), warm);
        Assert.Equal(new RuntimeOverviewStatusLabels("Loading: Qwen", "Runtime"), loading);
        Assert.Equal(new RuntimeOverviewStatusLabels("Failed (17): Qwen", "Unknown runtime"), failed);
    }


    [Fact]
    public void RuntimeMetricSummaryTrackerTracksLiveRatesAndLastKnownValues()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { SpeculativeType = "draft-mtp" };
        var tracker = new RuntimeMetricSummaryTracker();
        var capturedAt = DateTimeOffset.Parse("2026-05-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var firstSamples = new[]
        {
            new PrometheusSample("llama_tokens_predicted_total", "", 10, "10", "counter", ""),
            new PrometheusSample("llama_tokens_predicted_seconds_total", "", 5, "5", "counter", ""),
            new PrometheusSample("llama_prompt_tokens_total", "", 4, "4", "counter", ""),
            new PrometheusSample("llama_prompt_seconds_total", "", 2, "2", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_generated_total", "", 6, "6", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_generated_seconds_total", "", 3, "3", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_accepted_total", "", 4, "4", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_accepted_seconds_total", "", 2, "2", "counter", ""),
            new PrometheusSample("llama_requests_processing", "", 1, "1", "gauge", ""),
            new PrometheusSample("llama_requests_deferred", "", 2, "2", "gauge", ""),
            new PrometheusSample("llama_n_busy_slots_per_decode", "", 1.25, "1.25", "gauge", "")
        };
        var secondSamples = new[]
        {
            new PrometheusSample("llama_tokens_predicted_total", "", 16, "16", "counter", ""),
            new PrometheusSample("llama_tokens_predicted_seconds_total", "", 8, "8", "counter", ""),
            new PrometheusSample("llama_prompt_tokens_total", "", 8, "8", "counter", ""),
            new PrometheusSample("llama_prompt_seconds_total", "", 4, "4", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_generated_total", "", 12, "12", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_generated_seconds_total", "", 4, "4", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_accepted_total", "", 10, "10", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_accepted_seconds_total", "", 5, "5", "counter", ""),
            new PrometheusSample("llama_requests_processing", "", 2, "2", "gauge", ""),
            new PrometheusSample("llama_requests_deferred", "", 0, "0", "gauge", ""),
            new PrometheusSample("llama_n_busy_slots_per_decode", "", 1.5, "1.5", "gauge", "")
        };

        var first = tracker.Apply("model|runtime|8081", firstSamples, settings, slotSnapshot: null, mtpTokenSnapshot: null, capturedAt);
        var second = tracker.Apply("model|runtime|8081", secondSamples, settings, slotSnapshot: null, mtpTokenSnapshot: null, capturedAt.AddSeconds(2));
        var stale = tracker.Apply("model|runtime|8081", [], settings, slotSnapshot: null, mtpTokenSnapshot: null, capturedAt.AddSeconds(5));

        Assert.False(first.UsedLastKnown);
        Assert.Equal("Gen 10\nPrompt 4", first.TotalTokens);
        Assert.Equal("Generated: 2.0 t/s | Total generated: 10\nPrompt: 2.0 t/s | Total prompt: 4 | Cache hit: ?", first.Tokens);
        Assert.False(second.UsedLastKnown);
        Assert.Equal("Gen 2.0 t/s (2.0 avg)\nPrompt 2.0 t/s (2.0 avg)", second.GenerationRate);
        Assert.Equal("Gen 16\nPrompt 8", second.TotalTokens);
        Assert.Equal("Generated: 2.0 t/s | Total generated: 16\nPrompt: 2.0 t/s | Total prompt: 8 | Cache hit: ?", second.Tokens);
        Assert.Equal("3.0 t/s (Gen) | 3.0 t/s (Avg) | 12 t (Total)\n3.0 t/s (Accepted) | 2.0 t/s (Avg) | 10 t (Total)", second.MtpTokens);
        Assert.Equal("Active 2/2 | Queued 0\nBusy/decode 1.5", second.Slots);
        Assert.True(stale.UsedLastKnown);
        Assert.Equal(capturedAt.AddSeconds(2), stale.LastKnownCapturedAt);
        Assert.Equal(second.GenerationRate, stale.GenerationRate);
        Assert.Equal(second.MtpTokens, stale.MtpTokens);
        Assert.Equal(11, tracker.LastKnownSamples("model|runtime|8081").Count);
    }

    [Fact]
    public void RuntimeMetricSummaryTrackerUsesPerSlotRatesAcrossParallelSlotResets()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var tracker = new RuntimeMetricSummaryTracker();
        var capturedAt = DateTimeOffset.Parse("2026-05-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        tracker.Apply(
            "model|runtime|8081",
            [],
            settings,
            new RuntimeSlotSnapshot(
                PromptTokensProcessed: 120,
                GeneratedTokens: 1500,
                IsProcessing: true,
                PromptTokens: null,
                ContextTokens: null,
                ContextSize: 4096,
                SlotCounters:
                [
                    new RuntimeSlotCounterSnapshot("0", "task-a", 100, 1000, true, 40, 30),
                    new RuntimeSlotCounterSnapshot("1", "task-b", 20, 500, true, 20, 10)
                ]),
            mtpTokenSnapshot: null,
            capturedAt);

        var second = tracker.Apply(
            "model|runtime|8081",
            [],
            settings,
            new RuntimeSlotSnapshot(
                PromptTokensProcessed: 55,
                GeneratedTokens: 570,
                IsProcessing: true,
                PromptTokens: null,
                ContextTokens: null,
                ContextSize: 4096,
                SlotCounters:
                [
                    new RuntimeSlotCounterSnapshot("0", "task-c", 30, 10, true, 4, 3),
                    new RuntimeSlotCounterSnapshot("1", "task-b", 25, 560, true, 25, 14)
                ]),
            mtpTokenSnapshot: null,
            capturedAt.AddSeconds(2));

        Assert.False(second.UsedLastKnown);
        Assert.Null(second.LastKnownCapturedAt);
        Assert.Equal("Generated: 35.0 t/s | Total generated: 1,570\nPrompt: 17.5 t/s | Total prompt: 155 | Cache hit: ?", second.Tokens);
        Assert.Equal("4.5 t/s (Gen) | 69 t (Total)\n3.5 t/s (Accepted) | 47 t (Total)", second.MtpTokens);
        Assert.Equal("Active 2/2 | Queued 0\nBusy/decode 2.0", second.Slots);
        Assert.Equal(35, second.GraphSample.GenerationRate);
        Assert.Equal(17.5, second.GraphSample.PromptRate);
        Assert.Equal(4.5, second.GraphSample.SpeculativeGeneratedRate);
        Assert.Equal(3.5, second.GraphSample.SpeculativeAcceptedRate);
    }

    [Fact]
    public void RuntimeMetricSummaryTrackerDerivesAggregateKvOccupancyFromAllSlots()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with
        {
            ContextSize = 8192,
            ParallelSlots = 2,
            KvUnified = "on"
        };
        var tracker = new RuntimeMetricSummaryTracker();
        var snapshot = new RuntimeSlotSnapshot(
            PromptTokensProcessed: 1200,
            GeneratedTokens: 848,
            IsProcessing: true,
            PromptTokens: 1200,
            ContextTokens: 2048,
            ContextSize: 4096,
            SlotCounters:
            [
                new RuntimeSlotCounterSnapshot("0", "task-a", 800, 448, true),
                new RuntimeSlotCounterSnapshot("1", "task-b", 400, 400, true)
            ],
            ContextCapacityTokens: 8192);

        var summary = tracker.Apply(
            "model|runtime|8081",
            [],
            settings,
            snapshot,
            mtpTokenSnapshot: null,
            DateTimeOffset.Parse("2026-07-31T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal("Used 2,048 t | 25%\nCapacity 8,192 t | unified", summary.KvCache);
        Assert.Equal(25, summary.GraphSample.KvCacheUsagePercent);
        Assert.Equal("Active 2/2 | Queued 0\nBusy/decode 2.0", summary.Slots);
    }

    [Fact]
    public void RuntimeMetricSummaryTrackerPrefersPrometheusLiveRatesWhenBothSourcesExist()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var tracker = new RuntimeMetricSummaryTracker();
        var capturedAt = DateTimeOffset.Parse("2026-05-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        tracker.Apply(
            "model|runtime|8081",
            [
                new PrometheusSample("llama_tokens_predicted_total", "", 100, "100", "counter", ""),
                new PrometheusSample("llama_prompt_tokens_total", "", 20, "20", "counter", "")
            ],
            settings,
            new RuntimeSlotSnapshot(
                PromptTokensProcessed: 100,
                GeneratedTokens: 1000,
                IsProcessing: true,
                PromptTokens: null,
                ContextTokens: null,
                ContextSize: 4096,
                SlotCounters: [new RuntimeSlotCounterSnapshot("0", "task-a", 100, 1000, true)]),
            mtpTokenSnapshot: null,
            capturedAt);

        var second = tracker.Apply(
            "model|runtime|8081",
            [
                new PrometheusSample("llama_tokens_predicted_total", "", 110, "110", "counter", ""),
                new PrometheusSample("llama_prompt_tokens_total", "", 26, "26", "counter", "")
            ],
            settings,
            new RuntimeSlotSnapshot(
                PromptTokensProcessed: 300,
                GeneratedTokens: 1400,
                IsProcessing: true,
                PromptTokens: null,
                ContextTokens: null,
                ContextSize: 4096,
                SlotCounters: [new RuntimeSlotCounterSnapshot("0", "task-a", 300, 1400, true)]),
            mtpTokenSnapshot: null,
            capturedAt.AddSeconds(2));

        Assert.Equal("Generated: 5.0 t/s | Total generated: 110\nPrompt: 3.0 t/s | Total prompt: 26 | Cache hit: ?", second.Tokens);
    }

    [Fact]
    public void RuntimeMetricSummaryTrackerShowsConfiguredMtpIdleInsteadOfBlank()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { SpeculativeType = "draft-mtp" };
        var tracker = new RuntimeMetricSummaryTracker();
        var capturedAt = DateTimeOffset.Parse("2026-05-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var summary = tracker.Apply("model|runtime|8081", [], settings, slotSnapshot: null, mtpTokenSnapshot: null, capturedAt: capturedAt);

        Assert.False(summary.UsedLastKnown);
        Assert.Equal("Unknown (Gen)\nUnknown (Accepted)", summary.MtpTokens);
    }

    [Fact]
    public void RuntimeMetricSummaryTrackerShowsConfiguredDSparkIdleInsteadOfInactive()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { SpeculativeType = "draft-dspark" };
        var tracker = new RuntimeMetricSummaryTracker();
        var capturedAt = DateTimeOffset.Parse("2026-07-28T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var summary = tracker.Apply("model|runtime|8081", [], settings, slotSnapshot: null, mtpTokenSnapshot: null, capturedAt: capturedAt);

        Assert.False(summary.UsedLastKnown);
        Assert.Equal("Unknown (Gen)\nUnknown (Accepted)", summary.MtpTokens);
    }

    [Fact]
    public void RuntimeMetricSummaryTrackerKeepsKnownTokenValuesDuringPartialOutages()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { SpeculativeType = "draft-mtp" };
        var tracker = new RuntimeMetricSummaryTracker();
        var capturedAt = DateTimeOffset.Parse("2026-05-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var samples = new[]
        {
            new PrometheusSample("llama_tokens_predicted_total", "", 10, "10", "counter", ""),
            new PrometheusSample("llama_tokens_predicted_seconds_total", "", 5, "5", "counter", ""),
            new PrometheusSample("llama_prompt_tokens_total", "", 4, "4", "counter", ""),
            new PrometheusSample("llama_prompt_seconds_total", "", 2, "2", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_generated_total", "", 6, "6", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_generated_seconds_total", "", 3, "3", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_accepted_total", "", 4, "4", "counter", ""),
            new PrometheusSample("llama_mtp_tokens_accepted_seconds_total", "", 2, "2", "counter", "")
        };

        var fresh = tracker.Apply("model|runtime|8081", samples, settings, slotSnapshot: null, mtpTokenSnapshot: null, capturedAt);
        var partial = tracker.Apply(
            "model|runtime|8081",
            [],
            settings,
            new RuntimeSlotSnapshot(0, 0, IsProcessing: false, PromptTokens: null, ContextTokens: null, ContextSize: 4096),
            mtpTokenSnapshot: null,
            capturedAt.AddSeconds(3));

        Assert.False(fresh.UsedLastKnown);
        Assert.True(partial.UsedLastKnown);
        Assert.Equal(capturedAt, partial.LastKnownCapturedAt);
        Assert.Equal("Gen 10\nPrompt 4", partial.TotalTokens);
        Assert.Equal("Generated: 2.0 t/s | Total generated: 10\nPrompt: 2.0 t/s | Total prompt: 4 | Cache hit: ?", partial.Tokens);
        Assert.Equal("Unknown (Gen) | 2.0 t/s (Avg) | 6 t (Total)\nUnknown (Accepted) | 2.0 t/s (Avg) | 4 t (Total)", partial.MtpTokens);
    }

    [Fact]
    public void RuntimeMetricSummaryTrackerReportsTimestampForSpecificStaleCounters()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var tracker = new RuntimeMetricSummaryTracker();
        var capturedAt = DateTimeOffset.Parse("2026-05-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        tracker.Apply(
            "model|runtime|8081",
            [
                new PrometheusSample("llama_tokens_predicted_total", "", 10, "10", "counter", ""),
                new PrometheusSample("llama_tokens_predicted_seconds_total", "", 5, "5", "counter", ""),
                new PrometheusSample("llama_prompt_tokens_total", "", 4, "4", "counter", ""),
                new PrometheusSample("llama_prompt_seconds_total", "", 2, "2", "counter", "")
            ],
            settings,
            slotSnapshot: null,
            mtpTokenSnapshot: null,
            capturedAt);

        var generatedFreshPromptStale = tracker.Apply(
            "model|runtime|8081",
            [
                new PrometheusSample("llama_tokens_predicted_total", "", 16, "16", "counter", ""),
                new PrometheusSample("llama_tokens_predicted_seconds_total", "", 8, "8", "counter", "")
            ],
            settings,
            slotSnapshot: null,
            mtpTokenSnapshot: null,
            capturedAt.AddSeconds(2));

        var promptFreshGeneratedStale = tracker.Apply(
            "model|runtime|8081",
            [
                new PrometheusSample("llama_prompt_tokens_total", "", 10, "10", "counter", ""),
                new PrometheusSample("llama_prompt_seconds_total", "", 5, "5", "counter", "")
            ],
            settings,
            slotSnapshot: null,
            mtpTokenSnapshot: null,
            capturedAt.AddSeconds(4));

        Assert.True(generatedFreshPromptStale.UsedLastKnown);
        Assert.Equal(capturedAt, generatedFreshPromptStale.LastKnownCapturedAt);
        Assert.True(promptFreshGeneratedStale.UsedLastKnown);
        Assert.Equal(capturedAt.AddSeconds(2), promptFreshGeneratedStale.LastKnownCapturedAt);
        Assert.Equal("Gen 16\nPrompt 10", promptFreshGeneratedStale.TotalTokens);
    }

    [Fact]
    public void RuntimeMetricSummaryTrackerKeepsPerRuntimeRateBaselines()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var tracker = new RuntimeMetricSummaryTracker();
        var capturedAt = DateTimeOffset.Parse("2026-05-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        tracker.Apply(
            "model-a|runtime|8081",
            [
                new PrometheusSample("llama_tokens_predicted_total", "", 10, "10", "counter", ""),
                new PrometheusSample("llama_tokens_predicted_seconds_total", "", 5, "5", "counter", "")
            ],
            settings,
            slotSnapshot: null,
            mtpTokenSnapshot: null,
            capturedAt);
        tracker.Apply(
            "model-b|runtime|8082",
            [
                new PrometheusSample("llama_tokens_predicted_total", "", 100, "100", "counter", ""),
                new PrometheusSample("llama_tokens_predicted_seconds_total", "", 50, "50", "counter", "")
            ],
            settings,
            slotSnapshot: null,
            mtpTokenSnapshot: null,
            capturedAt.AddSeconds(1));

        var secondA = tracker.Apply(
            "model-a|runtime|8081",
            [
                new PrometheusSample("llama_tokens_predicted_total", "", 16, "16", "counter", ""),
                new PrometheusSample("llama_tokens_predicted_seconds_total", "", 8, "8", "counter", "")
            ],
            settings,
            slotSnapshot: null,
            mtpTokenSnapshot: null,
            capturedAt.AddSeconds(2));

        Assert.Equal("Gen 2.0 t/s (2.0 avg)\nPrompt Unknown", secondA.GenerationRate);
    }

    [Fact]
    public void RuntimeMetricSummaryTrackerUsesLogMtpDurationsForAverages()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { SpeculativeType = "draft-mtp" };
        var tracker = new RuntimeMetricSummaryTracker();
        var capturedAt = DateTimeOffset.Parse("2026-05-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var firstStats = RuntimeDashboardService.ParseMtpTokenStats(
            "statistics draft-mtp: #calls(b,g,a) = 1 10 10, #gen drafts = 10, #acc drafts = 8, #gen tokens = 100, #acc tokens = 80, dur(b,g,a) = 0.001, 10000.000, 0.250 ms");
        var secondStats = RuntimeDashboardService.ParseMtpTokenStats(
            "statistics draft-mtp: #calls(b,g,a) = 2 20 20, #gen drafts = 20, #acc drafts = 13, #gen tokens = 160, #acc tokens = 130, dur(b,g,a) = 0.001, 20000.000, 0.500 ms");

        var first = tracker.Apply("model|runtime|8081", [], settings, slotSnapshot: null, mtpTokenSnapshot: firstStats, capturedAt: capturedAt);
        var second = tracker.Apply("model|runtime|8081", [], settings, slotSnapshot: null, mtpTokenSnapshot: secondStats, capturedAt: capturedAt.AddSeconds(2));
        var idle = tracker.Apply("model|runtime|8081", [], settings, slotSnapshot: null, mtpTokenSnapshot: secondStats, capturedAt: capturedAt.AddSeconds(4));
        var stale = tracker.Apply("model|runtime|8081", [], settings, slotSnapshot: null, mtpTokenSnapshot: null, capturedAt: capturedAt.AddSeconds(6));

        Assert.Equal("Unknown (Gen) | 10.0 t/s (Avg) | 100 t (Total)\nUnknown (Accepted) | 8.0 t/s (Avg) | 80 t (Total)", first.MtpTokens);
        Assert.Equal("30.0 t/s (Gen) | 8.0 t/s (Avg) | 160 t (Total)\n25.0 t/s (Accepted) | 6.5 t/s (Avg) | 130 t (Total)", second.MtpTokens);
        Assert.Equal("0.0 t/s (Gen) | 8.0 t/s (Avg) | 160 t (Total)\n0.0 t/s (Accepted) | 6.5 t/s (Avg) | 130 t (Total)", idle.MtpTokens);
        Assert.True(stale.UsedLastKnown);
        Assert.Equal(idle.MtpTokens, stale.MtpTokens);
    }

    [Fact]
    public void GpuSummaryCacheOwnsFreshnessAndFallback()
    {
        var cache = new GpuSummaryCache();
        var now = DateTimeOffset.Parse("2026-05-28T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.False(cache.TryGet(now, out var initial));
        Assert.Equal("Unavailable", initial);

        Assert.Equal("Intel Arc 24 GB free", cache.Store("Intel Arc 24 GB free", now));
        Assert.True(cache.TryGet(now.AddSeconds(9), out var fresh));
        Assert.Equal("Intel Arc 24 GB free", fresh);

        Assert.False(cache.TryGet(now.AddSeconds(10), out var expired));
        Assert.Equal("Unavailable", expired);
        Assert.Equal("Unavailable", cache.Store("", now));
        Assert.Equal("GPU 0: 76% | 62C | 12.0/24.0 GiB", cache.Store("GPU 0: 76%|62C|12.0/24.0 GiB", now));
        Assert.Equal("NVIDIA 16 GB free", cache.Store("cuda", "NVIDIA 16 GB free", now));
        Assert.False(cache.TryGet("vulkan", now.AddSeconds(1), out var wrongKey));
        Assert.Equal("Unavailable", wrongKey);

        cache.Store("NVIDIA 16 GB free", now);
        cache.Clear();

        Assert.False(cache.TryGet(now.AddSeconds(1), out var cleared));
        Assert.Equal("Unavailable", cleared);
    }

    [Fact]
    public async Task RuntimeGpuSummaryApplicationServiceChoosesProbeAndCachesByActiveSession()
    {
        var root = CreateTempRoot();
        var now = DateTimeOffset.Parse("2026-05-28T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var files = new List<string>();
        var runner = new ScriptedProcessRunner(psi =>
        {
            files.Add(psi.FileName ?? "");
            if (string.Equals(Path.GetFileName(psi.FileName), "powershell.exe", StringComparison.OrdinalIgnoreCase))
                return DecodedPowerShellScript(psi).Contains("Win32_Processor", StringComparison.Ordinal)
                    ? new ProcessRunResult(0, "{\"Name\":\"AMD Ryzen 9 7950X 16-Core Processor\",\"Utilization\":18.5,\"PhysicalCores\":16,\"LogicalProcessors\":32}", "")
                    : new ProcessRunResult(0, "[{\"Index\":0,\"Name\":\"Intel(R) Arc(TM) A770 Graphics\",\"Utilization\":42,\"MemoryUsedBytes\":4294967296,\"MemoryTotalBytes\":17179869184}]", "");
            return new ProcessRunResult(0, "[level_zero:gpu][level_zero:0] Intel(R) Arc(TM) A770 Graphics", "");
        });
        var service = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(runner, () => "sycl-ls.exe", () => "nvidia-smi.exe", () => "powershell.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");
        var nativeSycl = Session(RuntimeMode.Native, RuntimeBackend.Sycl, AppSettings.CreateDefault(root), now);

        var first = await service.SummaryAsync(nativeSycl, now, TestContext.Current.CancellationToken);
        var cached = await service.SummaryAsync(nativeSycl, now.AddSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal("GPU 0: Intel(R) Arc(TM) A770 Graphics | 42% | 4.0/16.0 GiB", first);
        Assert.Equal(first, cached);
        Assert.Equal(["powershell.exe"], files);

        files.Clear();
        var amdService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(new ScriptedProcessRunner(psi =>
            {
                files.Add(psi.FileName ?? "");
                return DecodedPowerShellScript(psi).Contains("Win32_Processor", StringComparison.Ordinal)
                    ? new ProcessRunResult(0, "{\"Name\":\"AMD Ryzen 9 7950X\",\"Utilization\":18.5,\"PhysicalCores\":16,\"LogicalProcessors\":32}", "")
                    : new ProcessRunResult(0, "[{\"Index\":0,\"Name\":\"AMD Radeon RX 7900 XTX\",\"Utilization\":53.4,\"MemoryUsedBytes\":8589934592,\"MemoryTotalBytes\":25769803776}]", "");
            }), () => "", () => "nvidia-smi.exe", () => "powershell.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");

        var amd = await amdService.SummaryAsync(Session(RuntimeMode.Native, RuntimeBackend.Vulkan, AppSettings.CreateDefault(root), now), now, TestContext.Current.CancellationToken);

        Assert.Equal("GPU 0: AMD Radeon RX 7900 XTX | 53.4% | 8.0/24.0 GiB", amd);
        Assert.Equal(["powershell.exe"], files);

        files.Clear();
        var cpuService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(new ScriptedProcessRunner(psi =>
            {
                files.Add(psi.FileName ?? "");
                return new ProcessRunResult(0, "{\"TemperatureCelsius\":58.4}", "");
            }), () => "", () => "nvidia-smi.exe", () => "powershell.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");

        var cpu = await cpuService.SummaryAsync(Session(RuntimeMode.Native, RuntimeBackend.Cpu, AppSettings.CreateDefault(root), now), now, TestContext.Current.CancellationToken);

        Assert.Equal("Telemetry: 58.4 °C thermal", cpu);
        Assert.Equal(["powershell.exe"], files);

        files.Clear();
        var cudaCpu = await cpuService.SummaryAsync(
            Session(RuntimeMode.Native, RuntimeBackend.Cuda, AppSettings.CreateDefault(root) with { GpuLayers = 0 }, now),
            now,
            TestContext.Current.CancellationToken);

        Assert.Equal("Telemetry: 58.4 °C thermal", cudaCpu);
        Assert.Equal(["powershell.exe"], files);

        files.Clear();
        var wslRunner = new ScriptedProcessRunner(psi =>
        {
            files.Add(psi.FileName ?? "");
            if (string.Equals(Path.GetFileName(psi.FileName), "powershell.exe", StringComparison.OrdinalIgnoreCase))
                return DecodedPowerShellScript(psi).Contains("Win32_Processor", StringComparison.Ordinal)
                    ? new ProcessRunResult(0, "{\"Name\":\"Intel Core Ultra 9\",\"Utilization\":11,\"PhysicalCores\":16,\"LogicalProcessors\":22}", "")
                    : new ProcessRunResult(0, "[]", "");
            return new ProcessRunResult(0, "[level_zero:gpu][level_zero:0] Intel(R) Arc(TM) A770 Graphics", "");
        });
        var wslService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(wslRunner, () => "sycl-ls.exe", () => "nvidia-smi.exe", () => "powershell.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");
        var wslSycl = Session(RuntimeMode.Wsl, RuntimeBackend.Sycl, AppSettings.CreateDefault(root) with { WslDistro = "Ubuntu-24.04" }, now);

        var wsl = await wslService.SummaryAsync(wslSycl, now, TestContext.Current.CancellationToken);

        Assert.Equal("Intel(R) Arc(TM) A770 Graphics", wsl);
        Assert.Equal(["powershell.exe", "wsl.exe"], files);
        Assert.Equal(["-d", "Ubuntu-24.04", "--", "bash", "-lc"], wslRunner.Commands.Last().Take(5).ToArray());

        var nvidiaRunner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "0, NVIDIA RTX, 76, 62, 12288, 24576", ""));
        var nvidiaService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(nvidiaRunner, () => "", () => "nvidia-smi.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");

        var nvidia = await nvidiaService.SummaryAsync(Session(RuntimeMode.Native, RuntimeBackend.Cuda, AppSettings.CreateDefault(root), now), now, TestContext.Current.CancellationToken);

        Assert.Equal("GPU 0: NVIDIA RTX | 76% | 62C | 12.0/24.0 GiB", nvidia);

        var processRunner = new ScriptedProcessRunner(psi =>
        {
            var command = string.Join(' ', psi.ArgumentList);
            if (command.Contains("--query-compute-apps=", StringComparison.Ordinal))
            {
                return new ProcessRunResult(
                    0,
                    "GPU-a, 1111\nGPU-a, 4242\nGPU-b, 4242\nGPU-c, 3333",
                    "");
            }

            return new ProcessRunResult(
                0,
                "GPU-a, 0, NVIDIA RTX 3090, 76, 62, 12288, 24576\n"
                + "GPU-b, 1, NVIDIA RTX 3090, 74, 60, 12000, 24576\n"
                + "GPU-c, 2, NVIDIA RTX 4060, 10, 40, 1000, 8192",
                "");
        });
        var processService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(processRunner, () => "", () => "nvidia-smi.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");
        var processSession = Session(RuntimeMode.Native, RuntimeBackend.Cuda, AppSettings.CreateDefault(root), now) with { ProcessId = 4242 };

        var processHardware = await processService.SummaryAsync(processSession, now, TestContext.Current.CancellationToken);

        Assert.Equal(
            $"GPU 0: NVIDIA RTX 3090 | 76% | 62C | 12.0/24.0 GiB{Environment.NewLine}"
            + "GPU 1: NVIDIA RTX 3090 | 74% | 60C | 11.7/24.0 GiB",
            processHardware);
        Assert.DoesNotContain("CPU", processHardware, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GPU 2", processHardware, StringComparison.Ordinal);

        var selectedRunner = new ScriptedProcessRunner(_ => new ProcessRunResult(
            0,
            "0, NVIDIA RTX 3090, 76, 62, 12288, 24576\n1, NVIDIA RTX 3090, 74, 60, 12000, 24576",
            ""));
        var selectedService = new RuntimeGpuSummaryApplicationService(
            new GpuStatusProbeService(selectedRunner, () => "", () => "nvidia-smi.exe"),
            new GpuSummaryCache(),
            () => "wsl.exe");
        var selectedSettings = AppSettings.CreateDefault(root) with { GpuMode = "single", GpuDevices = "CUDA1" };

        var selectedHardware = await selectedService.SummaryAsync(
            Session(RuntimeMode.Native, RuntimeBackend.Cuda, selectedSettings, now),
            now,
            TestContext.Current.CancellationToken);

        Assert.Equal("GPU 1: NVIDIA RTX 3090 | 74% | 60C | 11.7/24.0 GiB", selectedHardware);

        static LoadedModelSessionSnapshot Session(RuntimeMode mode, RuntimeBackend backend, AppSettings settings, DateTimeOffset startedAt)
            => new(
                "session",
                "model",
                "Model",
                "runtime",
                "Runtime",
                mode,
                backend,
                settings,
                "",
                startedAt,
                "",
                0,
                LoadedModelSessionStatus.Running,
                IsRunning: true,
                IsSelected: true);
    }

    private static string DecodedPowerShellScript(ProcessStartInfo startInfo)
    {
        var encodedIndex = startInfo.ArgumentList.IndexOf("-EncodedCommand");
        return encodedIndex >= 0 && encodedIndex + 1 < startInfo.ArgumentList.Count
            ? System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(startInfo.ArgumentList[encodedIndex + 1]))
            : "";
    }


    [Fact]
    public void RuntimeLifetimeCounterTrackerTracksRuntimeKeysAndUsesSlotFallback()
    {
        var tracker = new RuntimeLifetimeCounterTracker();
        var firstKey = "model-a|runtime-a|8081";
        var secondKey = "model-b|runtime-b|8082";

        Assert.False(tracker.Observe(firstKey, "model-a", "Model A", generatedCounter: 10, promptCounter: 5, slotSnapshot: null).HasTokens);
        var firstDelta = tracker.Observe(firstKey, "model-a", "Model A", generatedCounter: 14, promptCounter: 9, slotSnapshot: null);

        Assert.Equal("model-a", firstDelta.ModelId);
        Assert.Equal(4, firstDelta.GeneratedTokens);
        Assert.Equal(4, firstDelta.PromptTokens);

        Assert.False(tracker.Observe(secondKey, "model-b", "Model B", generatedCounter: null, promptCounter: null, new RuntimeSlotSnapshot(20, 50, false, null, null, null)).HasTokens);
        var secondDelta = tracker.Observe(secondKey, "model-b", "Model B", generatedCounter: null, promptCounter: null, new RuntimeSlotSnapshot(26, 63, false, null, null, null));

        Assert.Equal("model-b", secondDelta.ModelId);
        Assert.Equal(13, secondDelta.GeneratedTokens);
        Assert.Equal(6, secondDelta.PromptTokens);

        tracker.RetainRuntimeKeys([secondKey]);
        Assert.Equal(1, tracker.Count);
        Assert.False(tracker.Observe(firstKey, "model-a", "Model A", generatedCounter: 100, promptCounter: 100, slotSnapshot: null).HasTokens);
    }

    [Fact]
    public void RuntimeLifetimeCounterTrackerAggregatesParallelSlotResetsWithoutDoubleCountingSourceChanges()
    {
        var tracker = new RuntimeLifetimeCounterTracker();
        const string key = "model-a|runtime-a|8081";
        var first = new RuntimeSlotSnapshot(
            120,
            1500,
            true,
            null,
            null,
            4096,
            SlotCounters:
            [
                new RuntimeSlotCounterSnapshot("0", "task-a", 100, 1000, true),
                new RuntimeSlotCounterSnapshot("1", "task-b", 20, 500, true)
            ]);
        var second = new RuntimeSlotSnapshot(
            55,
            570,
            true,
            null,
            null,
            4096,
            SlotCounters:
            [
                new RuntimeSlotCounterSnapshot("0", "task-c", 30, 10, true),
                new RuntimeSlotCounterSnapshot("1", "task-b", 25, 560, true)
            ]);

        Assert.False(tracker.Observe(key, "model-a", "Model A", null, null, first).HasTokens);
        var slotDelta = tracker.Observe(key, "model-a", "Model A", null, null, second);
        Assert.Equal(35, slotDelta.PromptTokens);
        Assert.Equal(70, slotDelta.GeneratedTokens);

        Assert.False(tracker.Observe(key, "model-a", "Model A", 2000, 500, second).HasTokens);
        var prometheusDelta = tracker.Observe(key, "model-a", "Model A", 2020, 508, second);
        Assert.Equal(8, prometheusDelta.PromptTokens);
        Assert.Equal(20, prometheusDelta.GeneratedTokens);
    }


    [Fact]
    public void RuntimeIdleUnloadTrackerTracksEachRuntimeKeyIndependently()
    {
        var tracker = new RuntimeIdleUnloadTracker();
        var now = DateTimeOffset.Parse("2026-05-27T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var firstKey = "model-a|runtime-a|8081";
        var secondKey = "model-b|runtime-b|8082";

        Assert.False(tracker.Observe(firstKey, new RuntimeSlotSnapshot(0, 0, false, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now));
        Assert.False(tracker.Observe(secondKey, new RuntimeSlotSnapshot(0, 0, false, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now));

        Assert.True(tracker.Observe(firstKey, new RuntimeSlotSnapshot(0, 0, false, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now.AddSeconds(61)));
        Assert.False(tracker.Observe(secondKey, new RuntimeSlotSnapshot(0, 0, true, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now.AddSeconds(61)));
        Assert.False(tracker.Observe(secondKey, new RuntimeSlotSnapshot(0, 0, false, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now.AddSeconds(90)));
        Assert.True(tracker.Observe(secondKey, new RuntimeSlotSnapshot(0, 0, false, null, null, null), generatedCounter: null, promptCounter: null, idleMinutes: 1, now.AddSeconds(122)));

        tracker.RetainRuntimeKeys([secondKey]);
        Assert.Equal(1, tracker.Count);
        tracker.Reset(secondKey);
        Assert.Equal(0, tracker.Count);
    }


    [Fact]
    public async Task RuntimeIdleUnloadPolicyServiceOwnsReentrancyAndUnloadSelection()
    {
        var service = new RuntimeIdleUnloadPolicyService();
        var root = CreateTempRoot();
        var now = DateTimeOffset.Parse("2026-05-27T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var first = PollResult(root, "model-a", "Model A", 8081, new RuntimeSlotSnapshot(0, 0, false, null, null, null));
        var second = PollResult(root, "model-b", "Model B", 8082, new RuntimeSlotSnapshot(0, 0, false, null, null, null));
        var unloaded = new List<string>();

        var firstPass = await service.ApplyAsync(
            [first, second],
            idleMinutes: 1,
            now: now,
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, firstPass);
        Assert.Equal(2, service.TrackedRuntimeCount);

        var secondPass = await service.ApplyAsync(
            [first, second],
            idleMinutes: 1,
            now: now.AddSeconds(61),
            async (idle, token) =>
            {
                unloaded.Add(idle.Session.ModelId);
                var nested = await service.ApplyAsync([idle], 1, now.AddSeconds(62), (_, _) => Task.CompletedTask, token);
                Assert.Equal(0, nested);
                Assert.True(service.IsApplying);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, secondPass);
        Assert.Equal(["model-a", "model-b"], unloaded);
        Assert.False(service.IsApplying);

        service.Reset(first.RuntimeKey);
        Assert.Equal(1, service.TrackedRuntimeCount);

        var resetPass = await service.ApplyAsync(
            [],
            idleMinutes: 1,
            now: now.AddMinutes(2),
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, resetPass);
        Assert.Equal(0, service.TrackedRuntimeCount);

        static RuntimeMetricPollResult PollResult(string root, string modelId, string modelName, int port, RuntimeSlotSnapshot slot)
        {
            var settings = AppSettings.CreateDefault(root) with { Port = port };
            var session = new LoadedModelSessionSnapshot(
                $"session-{modelId}",
                modelId,
                modelName,
                $"runtime-{port}",
                $"Runtime {port}",
                RuntimeMode.Native,
                RuntimeBackend.Cpu,
                settings,
                Path.Combine(root, $"{modelId}.log"),
                DateTimeOffset.UtcNow,
                "",
                0,
                LoadedModelSessionStatus.Running,
                IsRunning: true,
                IsSelected: false);

            return new RuntimeMetricPollResult(
                session,
                RuntimeMetricPollerService.RuntimeKey(session),
                [],
                slot,
                "");
        }
    }


    [Fact]
    public void RuntimeDashboardRefreshCoordinatorOwnsAdmissionGateAndPollSelection()
    {
        var coordinator = new RuntimeDashboardRefreshCoordinator();
        var source = ReadMainWindowSources();
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var running = RuntimeSession(root, settings with { Port = 8081 }, LoadedModelSessionStatus.Running, isRunning: true);
        var warm = RuntimeSession(root, settings with { Port = 8082 }, LoadedModelSessionStatus.Warm, isRunning: true) with { SessionId = "session-2" };
        var loading = RuntimeSession(root, settings with { Port = 8083 }, LoadedModelSessionStatus.Loading, isRunning: true) with { SessionId = "session-3" };
        var stopped = RuntimeSession(root, settings with { Port = 8084 }, LoadedModelSessionStatus.Running, isRunning: false) with { SessionId = "session-4" };
        var unreachable = RuntimeSession(root, settings with { Port = 8085 }, LoadedModelSessionStatus.Unreachable, isRunning: true) with { SessionId = "session-5" };
        var stoppedUnreachable = RuntimeSession(root, settings with { Port = 8086 }, LoadedModelSessionStatus.Unreachable, isRunning: false) with { SessionId = "session-6" };

        Assert.True(coordinator.ShouldRunTimer("Overview", hasRunningSessions: false));
        Assert.True(coordinator.ShouldRunTimer("Models", hasRunningSessions: true));
        Assert.False(coordinator.ShouldRunTimer("Models", hasRunningSessions: false));
        Assert.Null(coordinator.TryBeginRefresh(new RuntimeDashboardRefreshTarget(false, false, false, false)));

        using (var refresh = coordinator.TryBeginRefresh(new RuntimeDashboardRefreshTarget(false, true, false, false)))
        {
            Assert.NotNull(refresh);
            Assert.Null(coordinator.TryBeginRefresh(new RuntimeDashboardRefreshTarget(true, false, false, false)));
        }

        using var nextRefresh = coordinator.TryBeginRefresh(new RuntimeDashboardRefreshTarget(true, false, false, false));
        Assert.NotNull(nextRefresh);

        var pollable = coordinator.PollableSessions([running, warm, loading, stopped, unreachable, stoppedUnreachable]);
        Assert.Equal(["session-1", "session-2", "session-5"], pollable.Select(session => session.SessionId).ToArray());
        Assert.Contains("_coreServices.Ui.RuntimeDashboardRefreshTimer.Start(", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.RuntimeDashboardRefreshTimer.Stop()", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeDashboardTimerRefreshAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeDashboardTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeDashboardTimer_Tick", source, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeTelemetryApplicationServiceOwnsPollingAndCounters()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { EnableMetrics = false };
        var running = RuntimeSession(root, settings with { Port = 8081 }, LoadedModelSessionStatus.Running, isRunning: true);
        var warm = RuntimeSession(root, settings with { Port = 8082 }, LoadedModelSessionStatus.Warm, isRunning: true) with { SessionId = "session-2" };
        var loading = RuntimeSession(root, settings with { Port = 8083 }, LoadedModelSessionStatus.Loading, isRunning: true) with { SessionId = "session-3" };
        var stopped = RuntimeSession(root, settings with { Port = 8084 }, LoadedModelSessionStatus.Running, isRunning: false) with { SessionId = "session-4" };

        using var http = new HttpClient(new CapturingHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"is_processing":false,"n_prompt_tokens_processed":0,"n_decoded":0,"n_ctx":4096}]""")
        }));
        var service = new RuntimeTelemetryApplicationService(
            new RuntimeMetricPollerService(http),
            new RuntimeDashboardRefreshCoordinator(),
            new RuntimeMetricSummaryTracker(),
            new RuntimeLifetimeCounterTracker(),
            new RuntimeIdleUnloadPolicyService());

        Assert.True(service.ShouldRunRefreshTimer("Overview", hasRunningSessions: false));
        using var refresh = service.TryBeginRefresh(new RuntimeDashboardRefreshTarget(false, true, false, false));
        Assert.NotNull(refresh);

        var results = await service.PollSessionsAsync([running, warm, loading, stopped], TestContext.Current.CancellationToken);
        Assert.Equal(["session-1", "session-2"], results.Select(result => result.Session.SessionId).ToArray());

        var first = service.ObserveLifetimeTokenDeltas([CounterResult(generated: 10, prompt: 4, cachedPrompt: 100)]);
        var second = service.ObserveLifetimeTokenDeltas([CounterResult(generated: 16, prompt: 8, cachedPrompt: 900)]);

        Assert.Empty(first);
        var delta = Assert.Single(second);
        Assert.Equal(4, delta.PromptTokens);
        Assert.Equal(6, delta.GeneratedTokens);

        RuntimeMetricPollResult CounterResult(int generated, int prompt, int cachedPrompt)
        {
            var session = RuntimeSession(root, settings with { Port = 8081 }, LoadedModelSessionStatus.Running, isRunning: true);
            return new RuntimeMetricPollResult(
                session,
                RuntimeMetricPollerService.RuntimeKey(session),
                [
                    new PrometheusSample("llama_tokens_predicted_total", "", generated, generated.ToString(System.Globalization.CultureInfo.InvariantCulture), "counter", ""),
                    new PrometheusSample("llama_prompt_tokens_total", "", prompt, prompt.ToString(System.Globalization.CultureInfo.InvariantCulture), "counter", ""),
                    new PrometheusSample("llama_prompt_tokens_cached_total", "", cachedPrompt, cachedPrompt.ToString(System.Globalization.CultureInfo.InvariantCulture), "counter", "")
                ],
                null,
                "");
        }
    }


    [Fact]
    public async Task RuntimeTelemetryApplicationServiceOwnsIdleUnloadActions()
    {
        var root = CreateTempRoot();
        var now = DateTimeOffset.Parse("2026-05-27T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", now);
        var settings = AppSettings.CreateDefault(root) with { Port = 8081 };
        var session = RuntimeSession(root, settings, LoadedModelSessionStatus.Running, isRunning: true) with
        {
            ModelId = model.Id,
            ModelName = model.Name
        };
        var result = new RuntimeMetricPollResult(
            session,
            RuntimeMetricPollerService.RuntimeKey(session),
            [],
            new RuntimeSlotSnapshot(0, 0, false, null, null, null),
            "");
        var statuses = new List<string>();
        var stopped = new List<string>();
        var actions = new RuntimeIdleUnloadApplicationActions(
            id => Task.FromResult<ModelRecord?>(id == model.Id ? model : null),
            unloaded =>
            {
                stopped.Add(unloaded.Id);
                return Task.CompletedTask;
            },
            statuses.Add);
        var service = new RuntimeTelemetryApplicationService(
            new RuntimeMetricPollerService(new HttpClient(new CapturingHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)))),
            new RuntimeDashboardRefreshCoordinator(),
            new RuntimeMetricSummaryTracker(),
            new RuntimeLifetimeCounterTracker(),
            new RuntimeIdleUnloadPolicyService());

        var firstPass = await service.ApplyIdleUnloadPoliciesAsync(
            [result],
            idleMinutes: 1,
            now,
            actions,
            TestContext.Current.CancellationToken);
        var secondPass = await service.ApplyIdleUnloadPoliciesAsync(
            [result],
            idleMinutes: 1,
            now.AddSeconds(61),
            actions,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, firstPass);
        Assert.Equal(1, secondPass);
        Assert.Equal(["Auto-unloading Model A after 1 idle minute."], statuses);
        Assert.Equal([model.Id], stopped);
    }


    [Fact]
    public void RuntimeDashboardSelectionServiceChoosesRenderedSessionAndRuntimeKey()
    {
        var root = CreateTempRoot();
        var service = new RuntimeDashboardSelectionService();
        var defaults = AppSettings.CreateDefault(root) with { Port = 8081 };
        var activeSettings = defaults with { Port = 8091 };
        var selectedSettings = defaults with { Port = 8099 };
        var selectedModel = new ModelRecord("model-1", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var selectedSession = RuntimeSession(root, selectedSettings, LoadedModelSessionStatus.Running, isRunning: true);

        var selected = service.Select(new RuntimeDashboardSelectionRequest(
            selectedModel,
            SelectedOverviewModelIsActive: false,
            SelectedOverviewModelIsLoaded: true,
            selectedSession,
            SelectedSession: null,
            ActiveSessionSettings: activeSettings,
            ActiveRuntimeSettings: defaults,
            defaults,
            ActiveModelId: "active-model",
            ActiveRuntimeId: "active-runtime"));
        var fallback = service.Select(new RuntimeDashboardSelectionRequest(
            SelectedOverviewModel: null,
            SelectedOverviewModelIsActive: false,
            SelectedOverviewModelIsLoaded: false,
            SelectedOverviewModelSession: null,
            SelectedSession: null,
            ActiveSessionSettings: null,
            ActiveRuntimeSettings: activeSettings,
            defaults,
            ActiveModelId: "active-model",
            ActiveRuntimeId: "active-runtime"));

        Assert.True(selected.SelectSelectedOverviewModel);
        Assert.False(selected.SelectedOverviewModelHasNoRunningSession);
        Assert.Same(selectedSession, selected.Session);
        Assert.Equal(selectedSettings.Port, selected.MetricsSettings.Port);
        Assert.Equal(RuntimeMetricPollerService.RuntimeKey(selectedSession), selected.RuntimeKey);
        Assert.False(fallback.SelectSelectedOverviewModel);
        Assert.False(fallback.SelectedOverviewModelHasNoRunningSession);
        Assert.Null(fallback.Session);
        Assert.Equal(activeSettings.Port, fallback.MetricsSettings.Port);
        Assert.Equal("active-model|active-runtime|8091", fallback.RuntimeKey);

        var stoppedSelected = service.Select(new RuntimeDashboardSelectionRequest(
            selectedModel,
            SelectedOverviewModelIsActive: false,
            SelectedOverviewModelIsLoaded: false,
            selectedSession with { IsRunning = false },
            SelectedSession: null,
            ActiveSessionSettings: activeSettings,
            ActiveRuntimeSettings: defaults,
            defaults,
            ActiveModelId: "active-model",
            ActiveRuntimeId: "active-runtime"));

        Assert.True(stoppedSelected.SelectedOverviewModelHasNoRunningSession);
    }


    [Fact]
    public void RuntimeDashboardRenderDecisionServiceChoosesMetricRenderBranch()
    {
        var root = CreateTempRoot();
        var service = new RuntimeDashboardRenderDecisionService();
        var settings = AppSettings.CreateDefault(root) with { EnableMetrics = true };
        var session = RuntimeSession(root, settings, LoadedModelSessionStatus.Running, isRunning: true);
        var slot = new RuntimeSlotSnapshot(4, 8, false, 2, 16, 4096);
        var sample = new PrometheusSample("llama_tokens_predicted_total", "", 7, "7", "counter", "");
        var freshResult = new RuntimeMetricPollResult(session, RuntimeMetricPollerService.RuntimeKey(session), [sample], slot, "");
        var errorResult = new RuntimeMetricPollResult(session, RuntimeMetricPollerService.RuntimeKey(session), [], slot, "temporarily unavailable");

        var noRuntime = service.Decide(new RuntimeDashboardRenderDecisionRequest(
            SelectedSession: null,
            settings,
            SelectedPollResult: null));
        var metricsDisabled = service.Decide(new RuntimeDashboardRenderDecisionRequest(
            session,
            settings with { EnableMetrics = false },
            freshResult));
        var fresh = service.Decide(new RuntimeDashboardRenderDecisionRequest(
            session,
            settings,
            freshResult));
        var unavailable = service.Decide(new RuntimeDashboardRenderDecisionRequest(
            session,
            settings,
            errorResult));
        var noResponse = service.Decide(new RuntimeDashboardRenderDecisionRequest(
            session,
            settings,
            SelectedPollResult: null));

        Assert.Equal(RuntimeDashboardRenderDecisionKind.NoRuntime, noRuntime.Kind);
        Assert.Equal(RuntimeDashboardRenderDecisionKind.MetricsDisabled, metricsDisabled.Kind);
        Assert.Equal(slot, metricsDisabled.SlotSnapshot);
        Assert.Equal(RuntimeDashboardRenderDecisionKind.FreshMetrics, fresh.Kind);
        Assert.Equal([sample], fresh.Samples);
        Assert.Equal(RuntimeDashboardRenderDecisionKind.MetricsUnavailable, unavailable.Kind);
        Assert.Equal("temporarily unavailable", unavailable.Error);
        Assert.Equal("No metrics response.", noResponse.Error);
    }

    [Fact]
    public void RuntimeMetricRowsRenderServiceBuildsLastKnownAndErrorRows()
    {
        var service = new RuntimeMetricRowsRenderService();
        var sample = new PrometheusSample("llama_tokens_predicted_total", "", 7, "7", "counter", "");

        var fromSamples = service.FromSamples([sample]);
        Assert.Equal([sample], fromSamples.Samples);
        Assert.Null(fromSamples.LeadingRow);

        var lastKnown = service.Unavailable("temporarily unavailable", [sample]);
        Assert.Equal([sample], lastKnown.Samples);
        Assert.NotNull(lastKnown.LeadingRow);
        Assert.Equal("metrics_status", lastKnown.LeadingRow.C1);
        Assert.Equal("Last known values; refresh paused (temporarily unavailable)", lastKnown.LeadingRow.C3);

        var missing = service.Unavailable("No metrics response.", []);
        Assert.Null(missing.LeadingRow);
        Assert.Single(missing.Samples);
        Assert.Equal("metrics_error", missing.Samples[0].Name);
        Assert.Equal("No metrics response.", missing.Samples[0].RawValue);
    }

    [Fact]
    public async Task RuntimeDashboardMetricsApplicationServiceOwnsRenderBranchSideEffects()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { EnableMetrics = true };
        var session = RuntimeSession(root, settings, LoadedModelSessionStatus.Running, isRunning: true);
        var runtimeKey = RuntimeMetricPollerService.RuntimeKey(session);
        var slot = new RuntimeSlotSnapshot(4, 8, false, 2, 16, 4096);
        var sample = new PrometheusSample("llama_tokens_predicted_total", "", 7, "7", "counter", "");
        var freshResult = new RuntimeMetricPollResult(session, runtimeKey, [sample], slot, "");
        var unavailableResult = new RuntimeMetricPollResult(session, runtimeKey, [], null, "temporarily unavailable");
        var service = new RuntimeDashboardMetricsApplicationService(
            new RuntimeTelemetryApplicationService(
                new RuntimeMetricPollerService(new HttpClient()),
                new RuntimeDashboardRefreshCoordinator(),
                new RuntimeMetricSummaryTracker(),
                new RuntimeLifetimeCounterTracker(),
                new RuntimeIdleUnloadPolicyService()),
            new RuntimeDashboardRenderDecisionService(),
            new RuntimeMetricRowsRenderService());
        var calls = new List<string>();
        var rows = new List<RuntimeMetricRowsRenderPlan>();
        var summaries = new List<RuntimeMetricSummaryPresentation>();

        var fresh = await service.ApplyAsync(
            new RuntimeDashboardMetricsApplicationRequest(true, session, settings, freshResult, runtimeKey),
            Actions());
        var freshCalls = calls.ToArray();
        var freshRows = rows.ToArray();
        var freshSummaries = summaries.ToArray();
        Clear();

        var unavailable = await service.ApplyAsync(
            new RuntimeDashboardMetricsApplicationRequest(true, session, settings, unavailableResult, runtimeKey),
            Actions());
        var unavailableRows = rows.ToArray();
        var unavailableSummary = summaries.Single();
        Clear();

        var offOverview = await service.ApplyAsync(
            new RuntimeDashboardMetricsApplicationRequest(false, session, settings, freshResult, runtimeKey),
            Actions());
        var offOverviewCalls = calls.ToArray();
        Clear();

        var noRuntime = await service.ApplyAsync(
            new RuntimeDashboardMetricsApplicationRequest(true, null, settings, null, runtimeKey),
            Actions());

        Assert.Equal(RuntimeDashboardRenderDecisionKind.FreshMetrics, fresh);
        Assert.Contains("log:slot", freshCalls);
        Assert.Equal([sample], freshRows.Single().Samples);
        Assert.Null(freshSummaries.Single().LastKnownCapturedAt);

        Assert.Equal(RuntimeDashboardRenderDecisionKind.MetricsUnavailable, unavailable);
        Assert.Equal("metrics_status", unavailableRows.Single().LeadingRow?.C1);
        Assert.NotNull(unavailableSummary.LastKnownCapturedAt);

        Assert.Equal(RuntimeDashboardRenderDecisionKind.FreshMetrics, offOverview);
        Assert.DoesNotContain(offOverviewCalls, call => call.StartsWith("log:", StringComparison.Ordinal));
        Assert.DoesNotContain(offOverviewCalls, call => call.StartsWith("rows:", StringComparison.Ordinal));
        Assert.DoesNotContain(offOverviewCalls, call => call.StartsWith("summary:", StringComparison.Ordinal));

        Assert.Equal(RuntimeDashboardRenderDecisionKind.NoRuntime, noRuntime);
        Assert.Equal(RuntimeMetricSummaryPresentation.NoRuntime, summaries.Single());

        RuntimeDashboardMetricsApplicationActions Actions()
            => new(
                slotSnapshot =>
                {
                    calls.Add(slotSnapshot is null ? "log:none" : "log:slot");
                    return Task.FromResult<RuntimeMtpTokenSnapshot?>(null);
                },
                plan =>
                {
                    rows.Add(plan);
                    calls.Add($"rows:{plan.Samples.Count}:{plan.LeadingRow?.C1 ?? ""}");
                },
                summary =>
                {
                    summaries.Add(summary);
                    calls.Add($"summary:{summary.Tokens}");
                });

        void Clear()
        {
            calls.Clear();
            rows.Clear();
            summaries.Clear();
        }
    }


    [Fact]
    public async Task RuntimeDashboardRefreshApplicationServiceOwnsRefreshSequence()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081, EnableMetrics = true };
        var model = new ModelRecord("model-1", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var session = RuntimeSession(root, settings, LoadedModelSessionStatus.Running, isRunning: true) with
        {
            ModelId = model.Id,
            ModelName = model.Name
        };
        using var handler = new CapturingHttpHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/metrics")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("llama_tokens_predicted_total 7\n")
                };
            }

            if (request.RequestUri?.AbsolutePath == "/slots")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""[{"is_processing":true,"n_prompt_tokens_processed":4,"n_decoded":7,"n_ctx":4096}]""")
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        var telemetry = new RuntimeTelemetryApplicationService(
            new RuntimeMetricPollerService(http),
            new RuntimeDashboardRefreshCoordinator(),
            new RuntimeMetricSummaryTracker(),
            new RuntimeLifetimeCounterTracker(),
            new RuntimeIdleUnloadPolicyService());
        var service = new RuntimeDashboardRefreshApplicationService(
            telemetry,
            new RuntimeDashboardSelectionService(),
            new RuntimeDashboardMetricsApplicationService(
                telemetry,
                new RuntimeDashboardRenderDecisionService(),
                new RuntimeMetricRowsRenderService()));
        var calls = new List<string>();
        AppSettings? activeRuntimeSettings = null;

        var outcome = await service.RefreshAsync(
            new RuntimeDashboardRefreshApplicationRequest(
                new RuntimeDashboardRefreshTarget(true, true, true, true),
                true,
                settings,
                "",
                "",
                LlamaRuntimeState.Loaded,
                true),
            new RuntimeDashboardRefreshApplicationActions(
                () =>
                {
                    calls.Add("mark");
                    return Task.CompletedTask;
                },
                () => calls.Add("overview"),
                () => [session],
                results =>
                {
                    calls.Add($"health:{results.Count}");
                    return Task.CompletedTask;
                },
                results =>
                {
                    calls.Add($"lifetime:{results.Count}");
                    return Task.CompletedTask;
                },
                results =>
                {
                    calls.Add($"idle:{results.Count}");
                    return Task.CompletedTask;
                },
                () => model,
                _ => false,
                _ => true,
                _ => session,
                () => null,
                () => null,
                () => activeRuntimeSettings,
                selectedModelId =>
                {
                    calls.Add($"select:{selectedModelId}");
                    return new RuntimeSessionSelectResult(true, settings);
                },
                selectedSettings =>
                {
                    activeRuntimeSettings = selectedSettings;
                    calls.Add($"active:{selectedSettings?.Port}");
                },
                () =>
                {
                    calls.Add("labels");
                    return Task.FromResult(("Model label", "Runtime label"));
                },
                modelStatus => calls.Add($"model:{modelStatus}"),
                () =>
                {
                    calls.Add("save");
                    return Task.CompletedTask;
                },
                () => calls.Add("progress"),
                () =>
                {
                    calls.Add("gpu-read");
                    return Task.FromResult("GPU summary");
                },
                gpu => calls.Add($"gpu:{gpu}"),
                (_, _) =>
                {
                    calls.Add("stopped");
                    return Task.CompletedTask;
                },
                new RuntimeDashboardMetricsApplicationActions(
                    _ =>
                    {
                        calls.Add("metrics-log");
                        return Task.FromResult<RuntimeMtpTokenSnapshot?>(null);
                    },
                    _ => calls.Add("metrics-rows"),
                    _ => calls.Add("metrics-summary")),
                () => calls.Add("actions")),
            TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeDashboardRefreshApplicationOutcome.Applied, outcome);
        Assert.DoesNotContain("stopped", calls);
        Assert.DoesNotContain("save", calls);
        Assert.Equal(
            [
                "mark",
                "overview",
                "health:1",
                "lifetime:1",
                "idle:1",
                $"select:{model.Id}",
                "active:8081",
                "labels",
                "model:Model label",
                "progress",
                "gpu-read",
                "gpu:GPU summary",
                "metrics-log",
                "metrics-rows",
                "metrics-summary",
                "actions"
            ],
            calls);
    }


    [Fact]
    public void RuntimeDashboardPollsAllLoadedSessionsForLifetimeMetricsBeforeRenderingSelection()
    {
        var dashboard = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimeDashboard.cs"));
        var counters = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimeMetricCounters.cs"));
        var metrics = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimeMetrics.cs"));
        var refreshApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeDashboardRefreshApplicationService.cs"));
        var selection = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeDashboardSelectionService.cs"));
        var renderDecisions = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeDashboardRenderDecisionService.cs"));
        var metricRows = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeMetricRowsRenderService.cs"));
        var session = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimeSession.cs"));
        var poller = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeMetricPollerService.cs"));
        var refreshCoordinator = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeDashboardRefreshCoordinator.cs"));
        var telemetry = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeTelemetryApplicationService.cs"));
        var logTail = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeLogTailService.cs"));
        var overviewStatus = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeOverviewStatusService.cs"));
        var overviewModelSelection = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "OverviewModelSelectionApplicationService.cs"));
        var overviewSelection = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.OverviewSelection.cs"));

        Assert.Contains("_coreServices.Runtime.RuntimeDashboardRefreshApplication.RefreshAsync(", dashboard, StringComparison.Ordinal);
        Assert.Contains("await _telemetry.PollSessionsAsync(actions.SessionSnapshots()", refreshApplication, StringComparison.Ordinal);
        Assert.Contains("var pollableSessions = _refreshCoordinator.PollableSessions(sessions)", telemetry, StringComparison.Ordinal);
        Assert.Contains("_poller.PollSessionsAsync(pollableSessions, cancellationToken)", telemetry, StringComparison.Ordinal);
        Assert.Contains("or LoadedModelSessionStatus.Unreachable", refreshCoordinator, StringComparison.Ordinal);
        Assert.Contains("await actions.TrackLifetimeTokenDeltasAsync(pollResults)", refreshApplication, StringComparison.Ordinal);
        Assert.True(
            refreshApplication.IndexOf("await actions.TrackLifetimeTokenDeltasAsync(pollResults)", StringComparison.Ordinal)
            < refreshApplication.IndexOf("var selectedOverviewModel = actions.SelectedOverviewModel()", StringComparison.Ordinal));
        Assert.DoesNotContain("ResetLifetimeCounters();", dashboard, StringComparison.Ordinal);
        var lifetimeStart = counters.IndexOf("private async Task TrackLifetimeTokenDeltasAsync", StringComparison.Ordinal);
        var lifetimeEnd = counters.IndexOf("private void ResetLifetimeCounters()", lifetimeStart, StringComparison.Ordinal);
        Assert.DoesNotContain("_llama.ActiveModelId", counters[lifetimeStart..lifetimeEnd], StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeTelemetryApplication.ObserveLifetimeTokenDeltas(pollResults)", counters, StringComparison.Ordinal);
        Assert.Contains("var lifetimeMetrics = AppServices.LifetimeMetricsApplication", counters, StringComparison.Ordinal);
        Assert.Contains("await lifetimeMetrics.AddUsageAsync(delta)", counters, StringComparison.Ordinal);
        Assert.DoesNotContain("_stateStore.AddTokenUsageAsync", counters, StringComparison.Ordinal);
        Assert.Contains("RuntimeDashboardService.GeneratedTokenCounter(result.Samples)", telemetry, StringComparison.Ordinal);
        Assert.Contains("RuntimeDashboardService.PromptTokensProcessedCounter(result.Samples)", telemetry, StringComparison.Ordinal);
        Assert.Contains("result.SlotSnapshot", telemetry, StringComparison.Ordinal);
        Assert.Contains("_selection.Select(new RuntimeDashboardSelectionRequest(", refreshApplication, StringComparison.Ordinal);
        Assert.Contains("await _metricsApplication.ApplyAsync(", refreshApplication, StringComparison.Ordinal);
        Assert.Contains("RuntimeDashboardRenderDecisionKind.MetricsUnavailable", renderDecisions, StringComparison.Ordinal);
        var metricsApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeDashboardMetricsApplicationService.cs"));
        Assert.Contains("_renderDecisions.Decide(new RuntimeDashboardRenderDecisionRequest(", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("_rowsRender.Unavailable(", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("_telemetry.ResetMetricCounters()", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("Last known values; refresh paused", metricRows, StringComparison.Ordinal);
        Assert.DoesNotContain("Last known values; refresh paused", metrics, StringComparison.Ordinal);
        Assert.Contains("RuntimeMetricPollerService.RuntimeKey(session)", selection, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeMetricKey(LoadedModelSessionSnapshot session)", metrics, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeLogTail.CaptureAsync(logPath)", metrics, StringComparison.Ordinal);
        Assert.Contains("LogFileService.Tail(logPath", logTail, StringComparison.Ordinal);
        Assert.Contains("Slot status: processing", logTail, StringComparison.Ordinal);
        Assert.DoesNotContain("LogFileService.Tail(_llama.LogPath", metrics, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeOverviewStatus.Labels(new RuntimeOverviewStatusRequest(", overviewSelection, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.OverviewModelSelectionApplication.SelectAsync(", overviewSelection, StringComparison.Ordinal);
        Assert.Contains("OverviewModelSelectionActions()", overviewSelection, StringComparison.Ordinal);
        Assert.Contains("Load it to expose an OpenAI-compatible endpoint.", overviewModelSelection, StringComparison.Ordinal);
        Assert.DoesNotContain("Load it to expose an OpenAI-compatible endpoint.", overviewSelection, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedLoadedModel && !IsModelActive", overviewSelection, StringComparison.Ordinal);
        Assert.Contains("LoadedModelSessionStatus.Warm => \"Loaded\"", overviewStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadedModelSessionStatus.Warm => \"Loaded\"", overviewSelection, StringComparison.Ordinal);
        Assert.Contains("RuntimeMetrics.ParsePrometheus(raw)", poller, StringComparison.Ordinal);
        Assert.Contains("RuntimeDashboardService.ParseSlotSnapshot(raw)", poller, StringComparison.Ordinal);
        Assert.DoesNotContain("PollRuntimeMetricsForSessionAsync", dashboard, StringComparison.Ordinal);
        var refreshStart = refreshApplication.IndexOf("public async Task<RuntimeDashboardRefreshApplicationOutcome> RefreshAsync", StringComparison.Ordinal);
        var readinessIndex = refreshApplication.IndexOf("await actions.MarkLoadedSessionsIfReadyAsync();", refreshStart, StringComparison.Ordinal);
        var sessionRowsIndex = refreshApplication.IndexOf("actions.RefreshOverviewSessionRows();", readinessIndex, StringComparison.Ordinal);
        Assert.True(readinessIndex >= 0 && sessionRowsIndex > readinessIndex);
        Assert.Contains("ReplaceSessionsIfChanged", session, StringComparison.Ordinal);
    }


    [Fact]
    public async Task OverviewModelSelectionApplicationServiceOwnsLoadedInactiveAndStoppedSelection()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord(
            "model-1",
            "Qwen",
            Path.Combine(root, "models", "qwen.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var activeSettings = AppSettings.CreateDefault(root) with { Port = 8084 };
        var service = new OverviewModelSelectionApplicationService();
        var calls = new List<string>();

        OverviewModelSelectionApplicationActions Actions(bool selectSucceeds = true)
            => new(
                modelId =>
                {
                    calls.Add($"select:{modelId}");
                    return new RuntimeSessionSelectResult(selectSucceeds, selectSucceeds ? activeSettings : null);
                },
                settings => calls.Add($"active:{settings?.Port}"),
                () =>
                {
                    calls.Add("save");
                    return Task.CompletedTask;
                },
                () =>
                {
                    calls.Add("metrics");
                    return Task.CompletedTask;
                },
                status => calls.Add($"status:{status}"));

        var ignored = await service.SelectAsync(
            new OverviewModelSelectionApplicationRequest(null, IsLoaded: false, IsActive: false),
            Actions(),
            TestContext.Current.CancellationToken);
        var stopped = await service.SelectAsync(
            new OverviewModelSelectionApplicationRequest(model, IsLoaded: false, IsActive: false),
            Actions(),
            TestContext.Current.CancellationToken);
        var active = await service.SelectAsync(
            new OverviewModelSelectionApplicationRequest(model, IsLoaded: true, IsActive: true),
            Actions(),
            TestContext.Current.CancellationToken);
        var switched = await service.SelectAsync(
            new OverviewModelSelectionApplicationRequest(model, IsLoaded: true, IsActive: false),
            Actions(),
            TestContext.Current.CancellationToken);
        var staleLoaded = await service.SelectAsync(
            new OverviewModelSelectionApplicationRequest(model, IsLoaded: true, IsActive: false),
            Actions(selectSucceeds: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(OverviewModelSelectionOutcome.Ignored, ignored);
        Assert.Equal(OverviewModelSelectionOutcome.NotLoaded, stopped);
        Assert.Equal(OverviewModelSelectionOutcome.ActiveLoaded, active);
        Assert.Equal(OverviewModelSelectionOutcome.SwitchedLoaded, switched);
        Assert.Equal(OverviewModelSelectionOutcome.NotLoaded, staleLoaded);
        Assert.Equal([
            "status:Qwen is not loaded. Load it to expose an OpenAI-compatible endpoint.",
            "metrics",
            "metrics",
            $"select:{model.Id}",
            "active:8084",
            "save",
            "metrics",
            $"select:{model.Id}",
            "status:Selected model is no longer loaded.",
            "metrics"
        ], calls);
    }


    [Fact]
    public async Task OverviewLoadedSessionSelectionApplicationServiceOwnsModelLookupRefreshAndRuntimeSelection()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord(
            "model-1",
            "Qwen",
            Path.Combine(root, "models", "qwen.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var activeSettings = AppSettings.CreateDefault(root) with { Port = 8085 };
        var service = new OverviewLoadedSessionSelectionApplicationService();
        var calls = new List<string>();
        var knownModels = new List<ModelRecord>();
        var selectSucceeds = true;

        var actions = new OverviewLoadedSessionSelectionApplicationActions(
            modelId =>
            {
                calls.Add($"find:{modelId}");
                return knownModels.FirstOrDefault(item => string.Equals(item.Id, modelId, StringComparison.OrdinalIgnoreCase));
            },
            () =>
            {
                calls.Add("refresh-selector");
                knownModels.Add(model);
                return Task.CompletedTask;
            },
            modelId => calls.Add($"select-ui:{modelId}"),
            modelId =>
            {
                calls.Add($"select-runtime:{modelId}");
                return new RuntimeSessionSelectResult(selectSucceeds, selectSucceeds ? activeSettings : null);
            },
            settings => calls.Add($"active:{settings?.Port}"),
            () =>
            {
                calls.Add("save");
                return Task.CompletedTask;
            },
            () =>
            {
                calls.Add("metrics");
                return Task.CompletedTask;
            },
            () => calls.Add("actions"),
            status => calls.Add($"status:{status}"));

        var ignored = await service.SelectAsync("", actions, TestContext.Current.CancellationToken);
        var selectedAfterRefresh = await service.SelectAsync(model.Id, actions, TestContext.Current.CancellationToken);
        knownModels.Clear();
        selectSucceeds = false;
        var stale = await service.SelectAsync(model.Id, actions, TestContext.Current.CancellationToken);

        Assert.Equal(OverviewLoadedSessionSelectionOutcome.Ignored, ignored);
        Assert.Equal(OverviewLoadedSessionSelectionOutcome.Selected, selectedAfterRefresh);
        Assert.Equal(OverviewLoadedSessionSelectionOutcome.Stale, stale);
        Assert.Equal([
            $"find:{model.Id}",
            "refresh-selector",
            $"find:{model.Id}",
            $"select-ui:{model.Id}",
            $"select-runtime:{model.Id}",
            "active:8085",
            "save",
            "metrics",
            "actions",
            "status:Selected loaded model Qwen.",
            $"find:{model.Id}",
            "refresh-selector",
            $"find:{model.Id}",
            $"select-ui:{model.Id}",
            $"select-runtime:{model.Id}",
            "status:Selected session is no longer loaded."
        ], calls);
    }


    [Fact]
    public async Task RuntimeMetricPollerServicePollsMetricsAndSlotsForRunningSessions()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081, EnableMetrics = true };
        var session = RuntimeMetricSession(root, settings);
        var paths = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var handler = new CapturingHttpHandler(request =>
        {
            paths.Enqueue(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath switch
            {
                "/metrics" => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    llama_tokens_predicted_total 42
                    llama_prompt_tokens_total 9
                    """)
                },
                "/slots" => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""[{"is_processing":true,"n_prompt_tokens_processed":9,"n_decoded":4,"n_prompt_tokens":12,"n_ctx":4096}]""")
                },
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            };
        });
        using var http = new HttpClient(handler);
        var service = new RuntimeMetricPollerService(http);

        var results = await service.PollSessionsAsync([session], TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal("model-1|runtime-1|8081", result.RuntimeKey);
        Assert.Empty(result.Error);
        Assert.Contains(result.Samples, sample => sample.Name == "llama_tokens_predicted_total" && sample.Value == 42);
        Assert.Equal(9, result.SlotSnapshot?.PromptTokensProcessed);
        Assert.Equal(4, result.SlotSnapshot?.GeneratedTokens);
        Assert.Contains("/metrics", paths);
        Assert.Contains("/slots", paths);
    }


    [Fact]
    public async Task RuntimeMetricPollerServiceSkipsMetricsWhenDisabledButKeepsSlots()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081, EnableMetrics = false };
        var session = RuntimeMetricSession(root, settings);
        var paths = new List<string>();
        using var handler = new CapturingHttpHandler(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath == "/slots"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""[{"is_processing":false,"n_prompt_tokens_processed":5,"n_decoded":2}]""")
                }
                : new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
        });
        using var http = new HttpClient(handler);
        var service = new RuntimeMetricPollerService(http);

        var result = Assert.Single(await service.PollSessionsAsync([session], TestContext.Current.CancellationToken));

        Assert.Empty(result.Samples);
        Assert.Empty(result.Error);
        Assert.Equal(5, result.SlotSnapshot?.PromptTokensProcessed);
        Assert.Equal(["/slots"], paths);
    }


    [Fact]
    public async Task RuntimeMetricPollerServiceReturnsErrorWhenMetricsFail()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081, EnableMetrics = true };
        var session = RuntimeMetricSession(root, settings);
        using var handler = new CapturingHttpHandler(request =>
            request.RequestUri!.AbsolutePath == "/slots"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") }
                : new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler);
        var service = new RuntimeMetricPollerService(http);

        var result = Assert.Single(await service.PollSessionsAsync([session], TestContext.Current.CancellationToken));

        Assert.Empty(result.Samples);
        Assert.Contains("503", result.Error, StringComparison.Ordinal);
        Assert.NotNull(result.SlotSnapshot);
    }


}
