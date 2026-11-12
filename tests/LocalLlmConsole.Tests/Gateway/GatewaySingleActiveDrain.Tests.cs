using System.Net;
using System.Text;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class GatewaySingleActiveDrainTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task SingleActiveSwapWaitsForActiveResponseOnOtherModelBeforeStoppingIt()
    {
        var scenario = await SwapScenario.StartAsync(drainTimeoutSeconds: 30);
        await using var gateway = scenario.Gateway;

        var first = await scenario.StartStreamingFirstModelAsync();
        var second = scenario.Client.PostAsync("v1/chat/completions", JsonContent("""{"model":"model-b"}"""), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.False(second.IsCompleted);
        Assert.Equal(["load:model-a"], scenario.Runtime.Events);

        scenario.Upstream.FinishFirstStream();
        var firstBody = await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var secondResponse = await second;

        Assert.EndsWith("data: [DONE]\n\n", firstBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(["load:model-a", "stop:model-a", "load:model-b"], scenario.Runtime.Events);
        Assert.False(scenario.Upstream.StreamInterrupted);
        first.Dispose();
    }

    [Fact]
    public async Task SingleActiveSwapFailsInsteadOfInterruptingWhenDrainTimesOut()
    {
        var scenario = await SwapScenario.StartAsync(drainTimeoutSeconds: 1);
        await using var gateway = scenario.Gateway;

        using var first = await scenario.StartStreamingFirstModelAsync();
        using var second = await scenario.Client.PostAsync("v1/chat/completions", JsonContent("""{"model":"model-b"}"""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Contains("did not finish", await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(["load:model-a"], scenario.Runtime.Events);
        scenario.Upstream.FinishFirstStream();
        Assert.EndsWith("data: [DONE]\n\n", await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpstreamActivityBlocksWaitForActiveRequestsAndHoldNewEntries()
    {
        var activity = new ModelGatewayUpstreamActivity();
        var generation = activity.BlockGeneration;
        var (aLease, aStopped) = await activity.EnterAsync("a", "p", generation, TestContext.Current.CancellationToken);

        Assert.False(aStopped);
        Assert.Equal(1, activity.ActiveRequestCount("a"));
        Assert.Null(activity.TryBlockIdle("a", "p"));
        using (var otherProfile = activity.TryBlockIdle("a", "other"))
            Assert.NotNull(otherProfile);

        var drain = activity.BlockAndDrainAsync((model, _) => model == "a", TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var blockedEntry = activity.EnterAsync("a", "p", activity.BlockGeneration, TestContext.Current.CancellationToken);
        var (bLease, _) = await activity.EnterAsync("b", "p", activity.BlockGeneration, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(drain.IsCompleted);
        Assert.False(blockedEntry.IsCompleted);

        aLease.Dispose();
        var block = await drain.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(blockedEntry.IsCompleted);
        block.Dispose();
        var (reentered, mayHaveStopped) = await blockedEntry.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(mayHaveStopped);
        Assert.NotEqual(generation, activity.BlockGeneration);
        reentered.Dispose();
        bLease.Dispose();
        Assert.Equal(0, activity.ActiveRequestCount("a"));
        Assert.Equal(0, activity.ActiveRequestCount("b"));
    }

    [Fact]
    public async Task IdleUnloadSkipsSessionWithActiveGatewayRequestAndReservesItWhileStopping()
    {
        var root = CreateTempRoot();
        var now = DateTimeOffset.Parse("2026-05-27T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", now);
        var session = RuntimeSession(root, AppSettings.CreateDefault(root) with { Port = 8081 }, LoadedModelSessionStatus.Running, isRunning: true) with
        {
            ModelId = model.Id,
            ModelName = model.Name,
            LaunchProfileId = "default:model-a"
        };
        var poll = new RuntimeMetricPollResult(
            session,
            RuntimeMetricPollerService.RuntimeKey(session),
            [],
            new RuntimeSlotSnapshot(0, 0, false, null, null, null),
            "");
        var activity = new ModelGatewayUpstreamActivity();
        var stopped = new List<string>();
        Task<IDisposable>? entryDuringStop = null;
        var actions = new RuntimeIdleUnloadApplicationActions(
            id => Task.FromResult<ModelRecord?>(id == model.Id ? model : null),
            unloaded =>
            {
                stopped.Add(unloaded.ModelId);
                entryDuringStop = EnterLeaseAsync(activity, unloaded);
                return Task.CompletedTask;
            },
            _ => { },
            TryReserveIdleUnload: unloaded => activity.TryBlockIdle(unloaded.ModelId, unloaded.LaunchProfileId));
        var service = new RuntimeTelemetryApplicationService(
            new RuntimeMetricPollerService(new HttpClient(new CapturingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))),
            new RuntimeDashboardRefreshCoordinator(),
            new RuntimeMetricSummaryTracker(),
            new RuntimeLifetimeCounterTracker(),
            new RuntimeIdleUnloadPolicyService());
        var (activeRequest, _) = await activity.EnterAsync(model.Id, session.LaunchProfileId, activity.BlockGeneration, TestContext.Current.CancellationToken);

        await service.ApplyIdleUnloadPoliciesAsync([poll], 1, now, actions, TestContext.Current.CancellationToken);
        await service.ApplyIdleUnloadPoliciesAsync([poll], 1, now.AddSeconds(61), actions, TestContext.Current.CancellationToken);
        Assert.Empty(stopped);

        activeRequest.Dispose();
        await service.ApplyIdleUnloadPoliciesAsync([poll], 1, now.AddSeconds(62), actions, TestContext.Current.CancellationToken);
        await service.ApplyIdleUnloadPoliciesAsync([poll], 1, now.AddSeconds(123), actions, TestContext.Current.CancellationToken);

        Assert.Equal([model.Id], stopped);
        // A request arriving while the session stopped waited for the reservation.
        var lease = await entryDuringStop!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        lease.Dispose();
    }

    private static async Task<IDisposable> EnterLeaseAsync(ModelGatewayUpstreamActivity activity, LoadedModelSessionSnapshot session)
    {
        // Runs synchronously inside the stop callback, while the reservation is held.
        var entry = activity.EnterAsync(session.ModelId, session.LaunchProfileId, activity.BlockGeneration);
        Assert.False(entry.IsCompleted);
        var (lease, mayHaveStopped) = await entry;
        Assert.True(mayHaveStopped);
        return lease;
    }

    private sealed class SwapScenario
    {
        public required ModelGatewayService Gateway { get; init; }
        public required HttpClient Client { get; init; }
        public required SingleActiveRuntime Runtime { get; init; }
        public required HeldStreamHandler Upstream { get; init; }

        public static async Task<SwapScenario> StartAsync(int drainTimeoutSeconds)
        {
            var root = CreateTempRoot();
            var port = ReserveLoopbackPort();
            var defaults = AppSettings.CreateDefault(root);
            var runtime = new SingleActiveRuntime(
            [
                (new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow), defaults with { Port = 9101 }),
                (new ModelRecord("model-b", "Model B", Path.Combine(root, "b.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow), defaults with { Port = 9102 })
            ]);
            var upstream = new HeldStreamHandler(firstModelPort: 9101);
            var gateway = new ModelGatewayService(
                new ModelGatewayOptions(true, "local", port, "", false, ModelGatewaySwapPolicy.SingleActive, SwapDrainTimeoutSeconds: drainTimeoutSeconds),
                runtime,
                new ModelGatewayUpstreamProxy(new HttpClient(upstream)));
            await gateway.StartAsync(TestContext.Current.CancellationToken);
            return new SwapScenario
            {
                Gateway = gateway,
                Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(20) },
                Runtime = runtime,
                Upstream = upstream
            };
        }

        public async Task<HttpResponseMessage> StartStreamingFirstModelAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = JsonContent("""{"model":"model-a","stream":true}""")
            };
            var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await Upstream.FirstStreamStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            return response;
        }
    }

    // Simulates the Manager's Single active workflow: loading one model stops the others.
    private sealed class SingleActiveRuntime(IReadOnlyList<(ModelRecord Model, AppSettings Settings)> models) : IModelGatewayRuntimeController
    {
        private readonly object _sync = new();
        private readonly List<LoadedModelSessionSnapshot> _sessions = [];

        public List<string> Events { get; } = [];

        public Task<IReadOnlyList<ModelGatewayModelRoute>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelGatewayModelRoute>>(models.Select(entry => new ModelGatewayModelRoute(
                entry.Model,
                new NamedModelLaunchProfile($"default:{entry.Model.Id}", entry.Model.Id, "Default", ModelLaunchSettings.FromAppSettings(entry.Settings), entry.Model.UpdatedAt, true))).ToArray());

        public Task<IReadOnlyList<LoadedModelSessionSnapshot>> RunningSessionsAsync(CancellationToken cancellationToken = default)
        {
            lock (_sync)
                return Task.FromResult<IReadOnlyList<LoadedModelSessionSnapshot>>(_sessions.ToArray());
        }

        public Task<LoadedModelSessionSnapshot> EnsureModelLoadedAsync(ModelGatewayModelRoute route, ModelGatewaySwapPolicy policy, CancellationToken cancellationToken = default)
        {
            var settings = models.Single(entry => entry.Model.Id == route.Model.Id).Settings;
            lock (_sync)
            {
                foreach (var other in _sessions.ToArray())
                {
                    Events.Add($"stop:{other.ModelId}");
                    _sessions.Remove(other);
                }
                Events.Add($"load:{route.Model.Id}");
                var session = new LoadedModelSessionSnapshot(
                    $"session-{route.Model.Id}", route.Model.Id, route.Model.Name, "runtime", "CPU", RuntimeMode.Native, RuntimeBackend.Cpu,
                    settings, "runtime.log", DateTimeOffset.UtcNow, "", 1, LoadedModelSessionStatus.Running, IsRunning: true, IsSelected: true,
                    LaunchProfileId: route.Profile.Id, LaunchProfileName: route.Profile.Name);
                _sessions.Add(session);
                return Task.FromResult(session);
            }
        }
    }

    // Holds the first model's streamed response open until the test releases it.
    private sealed class HeldStreamHandler(int firstModelPort) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstStreamStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool StreamInterrupted { get; private set; }

        public void FinishFirstStream() => _finish.TrySetResult();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Port != firstModelPort)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
                });
            }

            var content = new PushStreamContent(async (stream, token) =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("data: {\"x\":1}\n\n"), token);
                await stream.FlushAsync(token);
                FirstStreamStarted.TrySetResult();
                try
                {
                    await _finish.Task.WaitAsync(token);
                }
                catch (OperationCanceledException)
                {
                    StreamInterrupted = true;
                    throw;
                }
                await stream.WriteAsync(Encoding.UTF8.GetBytes("data: [DONE]\n\n"), token);
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class PushStreamContent(Func<Stream, CancellationToken, Task> write) : HttpContent
    {
        private readonly System.IO.Pipelines.Pipe _pipe = new();

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            var writer = _pipe.Writer.AsStream();
            _ = Task.Run(async () =>
            {
                try
                {
                    await write(writer, CancellationToken.None);
                }
                finally
                {
                    await writer.DisposeAsync();
                }
            });
            return Task.FromResult(_pipe.Reader.AsStream());
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
