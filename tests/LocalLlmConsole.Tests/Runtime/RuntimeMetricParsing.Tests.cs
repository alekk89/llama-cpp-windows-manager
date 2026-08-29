using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeMetricParsingTests : ManagerRegressionTestBase
{
    [Fact]
    public void PrometheusMatchingCachesNormalizedNamesWithoutChangingJson()
    {
        var sample = new PrometheusSample(
            "LLAMACPP::Tokens-Predicted Total",
            "",
            42,
            "42",
            "counter",
            "");

        Assert.Equal(42, RuntimeMetrics.Sum([sample], ["tokens", "predicted", "total"], []));
        Assert.DoesNotContain(
            "NormalizedName",
            System.Text.Json.JsonSerializer.Serialize(sample),
            StringComparison.OrdinalIgnoreCase);
    }

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
            new PrometheusSample("llamacpp:predicted_tokens_seconds", "", 0, "0", "gauge", ""),
            new PrometheusSample("llamacpp:requests_completed_total", "", 7, "7", "counter", ""),
            new PrometheusSample("llamacpp:requests_failed_total", "", 1, "1", "counter", ""),
            new PrometheusSample("llamacpp:requests_processing", "", 3, "3", "gauge", "")
        };

        Assert.Equal(100, RuntimeDashboardService.PromptTokensProcessedCounter(samples));
        Assert.Equal(900, RuntimeDashboardService.PromptCachedTokenCounter(samples));
        Assert.Equal(1000, RuntimeDashboardService.PromptActivityTokenCounter(samples));
        Assert.Equal(2, RuntimeDashboardService.PromptSecondsCounter(samples));
        Assert.Equal(4, RuntimeDashboardService.GeneratedSecondsCounter(samples));
        Assert.Equal(7, RuntimeDashboardService.CompletedRequestCounter(samples));
        Assert.Equal(1, RuntimeDashboardService.FailedRequestCounter(samples));

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
        var chronological = service.Build(new RuntimeLogTailRequest(
            path,
            IsRuntimeRunning: true,
            SlotSnapshot: null,
            NewestFirst: false));

        Assert.False(missingLive.HasActiveLog);
        Assert.Equal("Runtime log file has not been created yet.", missingLive.Text);
        Assert.False(missingStopped.HasActiveLog);
        Assert.Equal("No runtime log is active.", missingStopped.Text);
        Assert.True(processing.HasActiveLog);
        Assert.Contains($"Live log: {path}", processing.Text, StringComparison.Ordinal);
        Assert.Contains("Slot status: processing | Prompt 12/20 | Gen 8", processing.Text, StringComparison.Ordinal);
        Assert.Contains("start", processing.Text, StringComparison.Ordinal);
        Assert.True(
            processing.Text.IndexOf("done", StringComparison.Ordinal)
            < processing.Text.IndexOf("start", StringComparison.Ordinal));
        Assert.Contains("omitted 2 repeated 'all slots are idle' lines", processing.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ALL SLOTS ARE IDLE", processing.Text, StringComparison.Ordinal);
        Assert.True(idle.HasActiveLog);
        Assert.Contains($"Last runtime log: {path}", idle.Text, StringComparison.Ordinal);
        Assert.Contains("Slot status: idle", idle.Text, StringComparison.Ordinal);
        Assert.True(
            chronological.Text.IndexOf("start", StringComparison.Ordinal)
            < chronological.Text.IndexOf("done", StringComparison.Ordinal));
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

}
