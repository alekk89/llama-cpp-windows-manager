using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeTelemetryTests : ManagerRegressionTestBase
{
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
        Assert.Equal("metrics_status", lastKnown.LeadingRow.Name);
        Assert.Equal("Last known values; refresh paused (temporarily unavailable)", lastKnown.LeadingRow.Value);

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
        Assert.Equal("metrics_status", unavailableRows.Single().LeadingRow?.Name);
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
                    calls.Add($"rows:{plan.Samples.Count}:{plan.LeadingRow?.Name ?? ""}");
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
                () =>
                {
                    calls.Add("gpu-read");
                    return Task.FromResult(HostHardwareSnapshotParser.Parse("GPU summary"));
                },
                gpu =>
                {
                    calls.Add($"gpu:{gpu.Summary}");
                    return Task.CompletedTask;
                },
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
                "gpu-read",
                "gpu:GPU summary",
                $"select:{model.Id}",
                "active:8081",
                "labels",
                "model:Model label",
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
        var dashboard = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Runtimes", "MainWindow.RuntimeDashboard.cs"));
        var counters = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Runtimes", "MainWindow.RuntimeMetricCounters.cs"));
        var metrics = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Runtimes", "MainWindow.RuntimeMetrics.cs"));
        var refreshApplication = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Telemetry", "RuntimeDashboardRefreshApplicationService.cs"));
        var selection = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.Core", "Services", "Runtimes", "RuntimeDashboardSelectionService.cs"));
        var renderDecisions = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.Core", "Services", "Runtimes", "RuntimeDashboardRenderDecisionService.cs"));
        var metricRows = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.Core", "Services", "Runtimes", "RuntimeMetricRowsRenderService.cs"));
        var session = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Runtimes", "MainWindow.RuntimeSession.cs"));
        var poller = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Telemetry", "RuntimeMetricPollerService.cs"));
        var refreshCoordinator = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Telemetry", "RuntimeDashboardRefreshCoordinator.cs"));
        var telemetry = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Telemetry", "RuntimeTelemetryApplicationService.cs"));
        var logTail = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Sessions", "RuntimeLogTailService.cs"));
        var overviewStatus = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Telemetry", "RuntimeOverviewStatusService.cs"));
        var overviewModelSelection = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "OverviewModelSelectionApplicationService.cs"));
        var overviewSelection = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Overview", "OverviewSelectionController.cs"));

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
        var metricsApplication = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Telemetry", "RuntimeDashboardMetricsApplicationService.cs"));
        Assert.Contains("_renderDecisions.Decide(new RuntimeDashboardRenderDecisionRequest(", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("_rowsRender.Unavailable(", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("_telemetry.ResetMetricCounters()", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("Last known values; refresh paused", metricRows, StringComparison.Ordinal);
        Assert.DoesNotContain("Last known values; refresh paused", metrics, StringComparison.Ordinal);
        Assert.Contains("RuntimeMetricIdentity.RuntimeKey(session)", selection, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeMetricKey(LoadedModelSessionSnapshot session)", metrics, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeLogTail.CaptureAsync(logPath)", metrics, StringComparison.Ordinal);
        Assert.Contains("LogFileService.Tail(logPath", logTail, StringComparison.Ordinal);
        Assert.Contains("Slot status: processing", logTail, StringComparison.Ordinal);
        Assert.DoesNotContain("LogFileService.Tail(_llama.LogPath", metrics, StringComparison.Ordinal);
        Assert.Contains("_runtime.RuntimeOverviewStatus.Labels(new RuntimeOverviewStatusRequest(", overviewSelection, StringComparison.Ordinal);
        Assert.Contains("_runtime.OverviewModelSelectionApplication.SelectAsync(", overviewSelection, StringComparison.Ordinal);
        Assert.Contains("new OverviewModelSelectionApplicationActions(", overviewSelection, StringComparison.Ordinal);
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
    public void InitialOverviewActivationStartsAndImmediatelyRefreshesHostTelemetry()
    {
        var pages = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Core", "MainWindow.Pages.cs"));
        var navigation = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Navigation", "MainWindow.Navigation.cs"));
        var pageReady = pages.IndexOf("PageHost.Content = _overviewPage.Scroller;", StringComparison.Ordinal);
        var timerStart = pages.IndexOf("StartRuntimeDashboardRefreshTimer();", pageReady, StringComparison.Ordinal);
        var optionalRefresh = pages.IndexOf("if (refresh)", pageReady, StringComparison.Ordinal);

        Assert.True(pageReady >= 0 && timerStart > pageReady && timerStart < optionalRefresh);
        Assert.Contains("if (_viewModel.CurrentPage == \"Overview\") await RefreshRuntimeMetricsAsync();", navigation, StringComparison.Ordinal);
    }


}
