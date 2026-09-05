using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ControlOperationConfirmationTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task EveryConsequentialOperationRejectsMissingConfirmationBeforeDispatch()
    {
        var root = CreateTempRoot();
        var factory = new AppServiceFactory(root);
        await using var store = new StateStore(factory.DatabasePath);
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var catalog = factory.CreateModelCatalogService(store);
        using var downloads = factory.CreateHuggingFaceService(store, factory.CreateJobEngine(store), catalog);
        using var http = new HttpClient(new CapturingHttpHandler(_ => throw new InvalidOperationException("No network request expected.")));
        var settings = AppSettings.CreateDefault(root);
        var dispatched = new List<(string Operation, bool DryRun)>();
        var api = new LocalControlApi(new LocalControlDependencies(
            root, store, sessions, catalog, factory.CreateModelLaunchProfileService(store, sessions),
            factory.CreateRuntimeRegistryService(store), downloads,
            factory.CreateRuntimeTelemetryApplicationService(factory.CreateRuntimeMetricPollerService(http)),
            factory.CreateRuntimeLogTailService(), factory.CreateRuntimeEndpointProbeService(http), factory.CreateLogPageWorkflowService(store),
            new LocalControlActions(() => settings, (next, _) => Task.FromResult(next),
                (_, _, _, _, _, _) => throw new InvalidOperationException("No launch expected."),
                (_, _) => throw new InvalidOperationException("No stop expected."), _ => Task.CompletedTask,
                (operation, body, _) =>
                {
                    dispatched.Add((operation, body?["dryRun"]?.GetValue<bool>() ?? false));
                    return Task.FromResult<object>(new { handled = true });
                })));

        var operations = ControlOperationCatalog.All.Where(operation => operation.RequiresConfirmation).ToArray();
        Assert.NotEmpty(operations);
        foreach (var operation in operations)
        {
            dispatched.Clear();
            foreach (var body in new[] { new JsonObject(), new JsonObject { ["confirm"] = false } })
            {
                var denied = await api.HandleAsync(Request(operation.Name, body), TestContext.Current.CancellationToken);
                Assert.Equal(400, denied.StatusCode);
                Assert.Empty(dispatched);
            }

            var dryRun = await api.HandleAsync(Request(operation.Name, new JsonObject { ["dryRun"] = true }), TestContext.Current.CancellationToken);
            Assert.Equal(200, dryRun.StatusCode);
            Assert.Equal((operation.Name, true), Assert.Single(dispatched));
            dispatched.Clear();

            var confirmed = await api.HandleAsync(Request(operation.Name, new JsonObject { ["confirm"] = true }), TestContext.Current.CancellationToken);
            Assert.Equal(200, confirmed.StatusCode);
            Assert.Equal((operation.Name, false), Assert.Single(dispatched));
        }
    }

    private static LocalControlRequest Request(string operation, JsonObject body)
        => new("POST", "/api/v1/operations/" + operation, new Dictionary<string, string>(), body, new Dictionary<string, string>());
}
