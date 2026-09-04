using System.Net;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ProgressiveTelemetryTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HealthySelectionRendersBeforeSlowPeerAndGpuWithoutDuplicatingAccounting(bool changeSelection)
    {
        using var handler = new HeldEndpointHandler(port => port == 8082);
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);
        var refresh = probe.RefreshAsync();
        try
        {
            await probe.Rendered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(refresh.IsCompleted);
            Assert.Single(probe.Summaries);
            Assert.Equal(8081, Assert.Single(probe.Summaries[0].Samples).Value);
            Assert.Empty(probe.Batches);
            Assert.False(probe.GpuStarted.Task.IsCompleted);
            Assert.Equal(RuntimeDashboardRefreshApplicationOutcome.Skipped, await probe.RefreshAsync());

            if (changeSelection) probe.Selected = probe.Sessions[1];
            handler.Release.SetResult();
            await probe.GpuStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(["health:2", "lifetime:2", "idle:2"], probe.Batches);
            Assert.Equal(changeSelection ? 2 : 1, probe.Summaries.Count);
            Assert.Equal(changeSelection ? 8082 : 8081, Assert.Single(probe.Summaries[^1].Samples).Value);
            Assert.False(refresh.IsCompleted);
            probe.ReleaseGpu.SetResult();
            Assert.Equal(RuntimeDashboardRefreshApplicationOutcome.Applied, await refresh);
            Assert.Equal(1, probe.FinalUpdates);
        }
        finally
        {
            handler.Release.TrySetResult();
            probe.ReleaseGpu.TrySetResult();
            await refresh;
        }
    }

    [Fact]
    public async Task SelectionUnloadedByBatchPolicyIsRenderedAsNoRuntime()
    {
        using var handler = new HeldEndpointHandler(_ => false);
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);
        probe.UnloadDuringPolicy = true;
        probe.ReleaseGpu.SetResult();
        await probe.RefreshAsync();
        Assert.Equal(2, probe.Summaries.Count);
        Assert.Same(RuntimeMetricSummaryPresentation.NoRuntime, probe.Summaries[^1]);
        Assert.Equal(["health:2", "lifetime:2", "idle:2"], probe.Batches);
    }

    [Fact]
    public async Task ReplacementSessionDoesNotTreatThePreviousSessionSampleAsFresh()
    {
        using var handler = new HeldEndpointHandler(port => port == 8082);
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);
        var refresh = probe.RefreshAsync();
        try
        {
            await probe.Rendered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            probe.Selected = probe.Selected! with { SessionId = "replacement" };
            handler.Release.SetResult();
            probe.ReleaseGpu.SetResult();
            await refresh;
            Assert.Equal(2, probe.Summaries.Count);
            Assert.NotNull(probe.Summaries[^1].LastKnownCapturedAt);
        }
        finally
        {
            handler.Release.TrySetResult();
            probe.ReleaseGpu.TrySetResult();
            await refresh;
        }
    }

    [Fact]
    public async Task CallbackFailureCancelsAndDrainsPendingPollsAndPreservesTheError()
    {
        using var handler = new HeldEndpointHandler(port => port == 8082);
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);
        var poller = new RuntimeMetricPollerService(http);
        var expected = new InvalidOperationException("render failed");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => poller.PollSessionsAsync(
            probe.Sessions, _ => Task.FromException(expected), TestContext.Current.CancellationToken));
        Assert.Same(expected, error);
        Assert.Equal(0, handler.Active);
        Assert.Single(await poller.PollSessionsAsync([probe.Sessions[0]], _ => Task.CompletedTask, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancellationDrainsActiveAndQueuedPollsWithoutRenderingFailures()
    {
        using var handler = new HeldEndpointHandler(_ => true);
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);
        var poller = new RuntimeMetricPollerService(http, maxConcurrentSessions: 1);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var callbacks = 0;
        var pending = poller.PollSessionsAsync(probe.Sessions, _ => { callbacks++; return Task.CompletedTask; }, cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, callbacks);
        Assert.Equal(0, handler.Active);
    }

    [Fact]
    public async Task PollConcurrencyRemainsBoundedAndCallbacksAreSerial()
    {
        using var handler = new HeldEndpointHandler(_ => true);
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);
        var sessions = Enumerable.Range(0, 6).Select(index => probe.Sessions[0] with
        {
            SessionId = $"session-{index}",
            LaunchSettings = probe.Settings with { Port = 8081 + index, EnableMetrics = false }
        }).ToArray();
        var activeCallbacks = 0;
        var callbackCount = 0;
        var pending = new RuntimeMetricPollerService(http, maxConcurrentSessions: 2).PollSessionsAsync(sessions, async _ =>
        {
            Assert.Equal(1, Interlocked.Increment(ref activeCallbacks));
            await Task.Yield();
            callbackCount++;
            Interlocked.Decrement(ref activeCallbacks);
        }, TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(2, handler.Active);
            handler.Release.SetResult();
            var results = await pending;
            Assert.Equal(sessions.Select(s => s.SessionId), results.Select(r => r.Session.SessionId));
            Assert.Equal(6, callbackCount);
            Assert.InRange(handler.PeakActive, 1, 2);
        }
        finally
        {
            handler.Release.TrySetResult();
            await pending;
        }
    }

    private DashboardProbe CreateProbe(HttpClient http)
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081, EnableMetrics = true };
        var first = RuntimeMetricSession(root, settings);
        var second = RuntimeMetricSession(root, settings with { Port = 8082 }) with { SessionId = "second" };
        return new DashboardProbe(http, settings, [first, second]);
    }

    private sealed class DashboardProbe
    {
        public AppSettings Settings { get; }
        public LoadedModelSessionSnapshot[] Sessions { get; }
        public LoadedModelSessionSnapshot? Selected { get; set; }
        public List<RuntimeMetricSummaryPresentation> Summaries { get; } = [];
        public List<string> Batches { get; } = [];
        public TaskCompletionSource Rendered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource GpuStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseGpu { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool UnloadDuringPolicy { get; set; }
        public int FinalUpdates { get; private set; }
        private readonly RuntimeDashboardRefreshApplicationService _service;

        public DashboardProbe(HttpClient http, AppSettings settings, LoadedModelSessionSnapshot[] sessions)
        {
            Settings = settings;
            Sessions = sessions;
            Selected = sessions[0];
            var telemetry = new RuntimeTelemetryApplicationService(new RuntimeMetricPollerService(http),
                new RuntimeDashboardRefreshCoordinator(), new RuntimeMetricSummaryTracker(),
                new RuntimeLifetimeCounterTracker(), new RuntimeIdleUnloadPolicyService());
            _service = new RuntimeDashboardRefreshApplicationService(telemetry, new RuntimeDashboardSelectionService(),
                new RuntimeDashboardMetricsApplicationService(telemetry, new RuntimeDashboardRenderDecisionService(), new RuntimeMetricRowsRenderService()));
        }

        public Task<RuntimeDashboardRefreshApplicationOutcome> RefreshAsync()
            => _service.RefreshAsync(new RuntimeDashboardRefreshApplicationRequest(
                new RuntimeDashboardRefreshTarget(true, true, true, true), true, Settings, "", "", LlamaRuntimeState.Loaded, true),
                new RuntimeDashboardRefreshApplicationActions(
                    () => Task.CompletedTask, () => { }, () => Sessions,
                    results => Batch("health", results), results => Batch("lifetime", results),
                    results => { if (UnloadDuringPolicy) Selected = Selected! with { IsRunning = false }; return Batch("idle", results); },
                    () => null, _ => false, _ => false, _ => null, () => Selected, () => null, () => null,
                    _ => new RuntimeSessionSelectResult(false, null), _ => { },
                    () => Task.FromResult(("Model", "Runtime")), _ => { }, () => Task.CompletedTask,
                    async () =>
                    {
                        GpuStarted.TrySetResult();
                        await ReleaseGpu.Task.WaitAsync(TestContext.Current.CancellationToken);
                        return HostHardwareSnapshotParser.Parse("GPU");
                    },
                    _ => Task.CompletedTask, (_, _) => Task.CompletedTask,
                    new RuntimeDashboardMetricsApplicationActions(
                        _ => Task.FromResult<RuntimeMtpTokenSnapshot?>(null), _ => { },
                        summary => { Summaries.Add(summary); Rendered.TrySetResult(); }),
                    () => FinalUpdates++), TestContext.Current.CancellationToken);

        private Task Batch(string name, IReadOnlyList<RuntimeMetricPollResult> results)
        {
            Batches.Add($"{name}:{results.Count}");
            return Task.CompletedTask;
        }
    }

    private sealed class HeldEndpointHandler(Func<int, bool> hold) : HttpMessageHandler
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active;
        public int PeakActive;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref Active);
            int observed;
            do { observed = PeakActive; } while (active > observed && Interlocked.CompareExchange(ref PeakActive, active, observed) != observed);
            try
            {
                var port = request.RequestUri!.Port;
                if (hold(port))
                {
                    Started.TrySetResult();
                    await Release.Task.WaitAsync(cancellationToken);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(request.RequestUri.AbsolutePath == "/metrics"
                        ? $"llama_tokens_predicted_total {port}\n" : "[]")
                };
            }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
}
