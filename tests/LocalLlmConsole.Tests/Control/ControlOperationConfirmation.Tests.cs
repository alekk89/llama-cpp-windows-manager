using System.Text.Json;
using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ControlOperationConfirmationTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task EveryConsequentialOperationRejectsMissingConfirmationBeforeDispatch()
    {
        var dispatched = new List<(string Operation, bool DryRun)>();
        await using var host = await OperationHost.CreateAsync(CreateTempRoot(), (operation, body) =>
        {
            dispatched.Add((operation, body?["dryRun"]?.GetValue<bool>() ?? false));
            return new { handled = true };
        });

        var operations = ControlOperationCatalog.All.Where(operation => operation.RequiresConfirmation).ToArray();
        Assert.NotEmpty(operations);
        foreach (var operation in operations)
        {
            dispatched.Clear();
            foreach (var body in new[] { new JsonObject(), new JsonObject { ["confirm"] = false } })
            {
                var denied = await host.Api.HandleAsync(Request(operation.Name, body), TestContext.Current.CancellationToken);
                Assert.Equal(400, denied.StatusCode);
                Assert.Empty(dispatched);
            }

            var dryRun = await host.Api.HandleAsync(Request(operation.Name, new JsonObject { ["dryRun"] = true }), TestContext.Current.CancellationToken);
            Assert.Equal(200, dryRun.StatusCode);
            Assert.Equal((operation.Name, true), Assert.Single(dispatched));
            dispatched.Clear();

            var confirmed = await host.Api.HandleAsync(Request(operation.Name, new JsonObject { ["confirm"] = true }), TestContext.Current.CancellationToken);
            Assert.Equal(200, confirmed.StatusCode);
            Assert.Equal((operation.Name, false), Assert.Single(dispatched));
        }
    }

    [Fact]
    public async Task UnsuccessfulOperationOutcomeReturnsConflictWithOkFalseAndOriginalResult()
    {
        await using var host = await OperationHost.CreateAsync(CreateTempRoot(), (operation, _) => operation == "runtime.delete"
            ? ControlOperationFailure.FromOutcome(operation, "Blocked", "Runtime is in use.", new { outcome = "Blocked", runtime = "rt" })
            : new { outcome = "Deleted" });

        var failed = await host.Api.HandleAsync(Request("runtime.delete", new JsonObject { ["confirm"] = true, ["runtime"] = "rt" }), TestContext.Current.CancellationToken);
        var succeeded = await host.Api.HandleAsync(Request("logs.delete-all", new JsonObject { ["confirm"] = true }), TestContext.Current.CancellationToken);

        Assert.Equal(409, failed.StatusCode);
        var body = JsonSerializer.SerializeToNode(failed.Body)!;
        Assert.False(body["ok"]!.GetValue<bool>());
        Assert.Equal("runtime.delete", body["operation"]!.GetValue<string>());
        Assert.Equal("Blocked", body["outcome"]!.GetValue<string>());
        Assert.Equal("Operation 'runtime.delete' did not complete (outcome: Blocked): Runtime is in use.", body["error"]!.GetValue<string>());
        Assert.Equal("rt", body["result"]!["runtime"]!.GetValue<string>());
        Assert.Equal(200, succeeded.StatusCode);
        Assert.True(JsonSerializer.SerializeToNode(succeeded.Body)!["ok"]!.GetValue<bool>());
    }

    [Fact]
    public void GatewayRestartThatLeavesTheGatewayStoppedIsAFailure()
    {
        var failed = Assert.IsType<ControlOperationFailure>(ControlOperationFailure.GatewayResult(restart: true, applied: false, enabled: false, port: 8080));
        var restarted = ControlOperationFailure.GatewayResult(restart: true, applied: true, enabled: true, port: 8080);

        Assert.Equal("NotStarted", failed.Outcome);
        Assert.Contains("gateway.restart", failed.Error, StringComparison.Ordinal);
        Assert.False(JsonSerializer.SerializeToNode(failed.Result)!["applied"]!.GetValue<bool>());
        Assert.IsNotType<ControlOperationFailure>(restarted);
        Assert.True(JsonSerializer.SerializeToNode(restarted)!["applied"]!.GetValue<bool>());
    }

    private static LocalControlRequest Request(string operation, JsonObject body)
        => new("POST", "/api/v1/operations/" + operation, new Dictionary<string, string>(), body, new Dictionary<string, string>());

    private sealed class OperationHost : IAsyncDisposable
    {
        private readonly StateStore _store;
        private readonly LoadedModelSessionManager _sessions;
        private readonly HuggingFaceService _downloads;
        private readonly HttpClient _http;

        private OperationHost(StateStore store, LoadedModelSessionManager sessions, HuggingFaceService downloads, HttpClient http, LocalControlApi api)
        {
            _store = store;
            _sessions = sessions;
            _downloads = downloads;
            _http = http;
            Api = api;
        }

        public LocalControlApi Api { get; }

        public static async Task<OperationHost> CreateAsync(string root, Func<string, JsonObject?, object> execute)
        {
            var factory = new AppServiceFactory(root);
            var store = new StateStore(factory.DatabasePath);
            await store.InitializeAsync();
            var sessions = CreateLoadedModelSessionManager();
            var catalog = factory.CreateModelCatalogService(store);
            var downloads = factory.CreateHuggingFaceService(store, factory.CreateJobEngine(store), catalog);
            var http = new HttpClient(new CapturingHttpHandler(_ => throw new InvalidOperationException("No network request expected.")));
            var settings = AppSettings.CreateDefault(root);
            var api = new LocalControlApi(new LocalControlDependencies(
                root, store, sessions, catalog, factory.CreateModelLaunchProfileService(store, sessions),
                factory.CreateRuntimeRegistryService(store), downloads,
                factory.CreateRuntimeTelemetryApplicationService(factory.CreateRuntimeMetricPollerService(http)),
                factory.CreateRuntimeLogTailService(), factory.CreateRuntimeEndpointProbeService(http), factory.CreateLogPageWorkflowService(store),
                new LocalControlActions(() => settings, (next, _) => Task.FromResult(next),
                    (_, _, _, _, _, _) => throw new InvalidOperationException("No launch expected."),
                    (_, _) => throw new InvalidOperationException("No stop expected."), _ => Task.CompletedTask,
                    (operation, body, _) => Task.FromResult(execute(operation, body)))));
            return new OperationHost(store, sessions, downloads, http, api);
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            _downloads.Dispose();
            _sessions.Dispose();
            await _store.DisposeAsync();
        }
    }
}
