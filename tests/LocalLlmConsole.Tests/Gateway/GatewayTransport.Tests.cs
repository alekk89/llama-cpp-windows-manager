using System.Diagnostics;
using System.Net;
using System.Text;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


[Collection(LocalizationStateTestCollection.Name)]
public sealed class GatewayTransportTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task ModelGatewayUpstreamProxyDoesNotDisposeInjectedHttpClient()
    {
        using var client = new HttpClient(new CapturingHttpHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        var proxy = new ModelGatewayUpstreamProxy(client);

        proxy.Dispose();

        using var response = await client.GetAsync("http://127.0.0.1/", TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void GatewayThroughputUsesFullDurationForNonStreamingResponses()
    {
        var duration = TimeSpan.FromSeconds(5);
        var firstData = TimeSpan.FromSeconds(4.9);

        var nonStreaming = GatewayResponseThroughputPolicy.Calculate(
            100, firstData, duration, "application/json");
        var streaming = GatewayResponseThroughputPolicy.Calculate(
            100, firstData, duration, "text/event-stream");

        Assert.Equal(20d, nonStreaming!.Value, precision: 6);
        Assert.Equal(1000d, streaming!.Value, precision: 6);
        Assert.Null(GatewayResponseThroughputPolicy.Calculate(null, firstData, duration, "application/json"));
        Assert.Null(GatewayResponseThroughputPolicy.Calculate(100, null, TimeSpan.Zero, "application/json"));
    }

    [Fact]
    public void GatewayCompletionTokenObserverParsesLatestCounterAcrossArbitraryChunks()
    {
        var observer = new ModelGatewayUpstreamProxy.GatewayCompletionTokenObserver();
        var payload = Encoding.UTF8.GetBytes("""
            data: {"predicted_n":12}

            data: {"usage":{"completion_tokens" : 24.5}}
            """);

        foreach (var value in payload)
            observer.Observe([value]);

        Assert.Equal(24.5, observer.Complete());
        Assert.Equal(24.5, observer.Complete());
    }

    [Theory]
    [InlineData("{\"completion_tokens\":0}", 0)]
    [InlineData("{\"PREDICTED_N\" : 17}", 17)]
    [InlineData("{\"completion_tokens\":8,\"predicted_n\":9}", 9)]
    public void GatewayCompletionTokenObserverPreservesSupportedResponseShapes(string json, double expected)
    {
        var observer = new ModelGatewayUpstreamProxy.GatewayCompletionTokenObserver();

        observer.Observe(Encoding.UTF8.GetBytes(json));

        Assert.Equal(expected, observer.Complete());
    }

    [Fact]
    public async Task GatewayRouteCatalogBulkLoadsProfilesAndRepairsDefaultsOnlyWhenNeeded()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "gateway-routes.db"));
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var settings = AppSettings.CreateDefault(root);
        var first = new ModelRecord("first", "First", Path.Combine(root, "first.gguf"), OwnershipKind.External, "{}", now);
        var second = new ModelRecord("second", "Second", Path.Combine(root, "second.gguf"), OwnershipKind.External, "{}", now);
        var firstProfile = new NamedModelLaunchProfile(
            "default:first", first.Id, "Default", ModelLaunchSettings.FromAppSettings(settings), now, IsDefault: true);
        var secondProfile = new NamedModelLaunchProfile(
            "tuned:second", second.Id, "Tuned", ModelLaunchSettings.FromAppSettings(settings), now, IsDefault: false);
        await store.UpsertModelAsync(first);
        await store.UpsertModelAsync(second);
        await store.SaveNamedModelLaunchProfileAsync(firstProfile);
        await store.SaveNamedModelLaunchProfileAsync(secondProfile);
        var catalog = new ModelGatewayRouteCatalogApplicationService(store);
        var repairs = 0;

        var routes = await catalog.ListAsync(new ModelGatewayRouteCatalogActions(async (models, token) =>
        {
            token.ThrowIfCancellationRequested();
            repairs++;
            var missing = Assert.Single(models);
            Assert.Equal(second.Id, missing.Id);
            await store.SaveNamedModelLaunchProfileAsync(secondProfile with { IsDefault = true, UpdatedAt = DateTimeOffset.UtcNow });
        }), TestContext.Current.CancellationToken);
        var secondRead = await catalog.ListAsync(new ModelGatewayRouteCatalogActions((_, _) =>
            Task.FromException(new InvalidOperationException("Default repair should not repeat."))), TestContext.Current.CancellationToken);

        Assert.Equal(1, repairs);
        Assert.Equal(["first", "second"], routes.Select(route => route.Model.Id).Order().ToArray());
        Assert.Equal(["first", "second"], secondRead.Select(route => route.Model.Id).Order().ToArray());
        Assert.All(routes, route => Assert.True(route.Profile.IsDefault));

        var windowSource = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Gateway", "MainWindow.Gateway.cs"));
        var catalogSource = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Gateway", "ModelGatewayRuntimeController.cs"));
        Assert.DoesNotContain("ListNamedAsync(model)", windowSource, StringComparison.Ordinal);
        Assert.Contains("ListNamedModelLaunchProfilesAsync()", catalogSource, StringComparison.Ordinal);
        Assert.Contains("GroupBy(profile => profile.ModelId", catalogSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelGatewayHostFactoryServiceOwnsGatewayHostAndControllerCreation()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with
        {
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8091
        };
        var calls = new List<string>();
        var expectedController = new FakeModelGatewayRuntimeController();
        var expectedHost = new FakeModelGatewayHost();
        var service = new ModelGatewayHostFactoryService(
            actions =>
            {
                calls.Add("controller");
                Assert.NotNull(actions.ListModelsAsync);
                Assert.NotNull(actions.RunningSessionsAsync);
                Assert.NotNull(actions.EnsureModelLoadedAsync);
                return expectedController;
            },
            (options, runtime) =>
            {
                calls.Add($"host:{options.Port}");
                Assert.Same(expectedController, runtime);
                return expectedHost;
            });
        var actions = new ModelGatewayRuntimeControllerActions(
            _ => Task.FromResult<IReadOnlyList<ModelGatewayModelRoute>>([]),
            _ => Task.FromResult<IReadOnlyList<LoadedModelSessionSnapshot>>([]),
            (_, _, _) => Task.FromException<LoadedModelSessionSnapshot>(new NotSupportedException()));

        var controller = service.CreateRuntimeController(actions);
        var host = service.CreateGatewayHost(ModelGatewayOptions.FromSettings(settings), controller);

        Assert.Same(expectedController, controller);
        Assert.Same(expectedHost, host);
        Assert.Equal(["controller", "host:8091"], calls);
        Assert.Throws<ArgumentNullException>(() => service.CreateRuntimeController(null!));
        Assert.Throws<ArgumentNullException>(() => service.CreateGatewayHost(null!, controller));
        Assert.Throws<ArgumentNullException>(() => service.CreateGatewayHost(ModelGatewayOptions.FromSettings(settings), null!));
    }


    [Fact]
    public async Task ModelGatewayRequestBodyReaderRejectsOversizedAndCancelledBodies()
    {
        var small = System.Text.Encoding.UTF8.GetBytes("""{"model":"qwen"}""");
        var tooLarge = System.Text.Encoding.UTF8.GetBytes("""{"model":"qwen","messages":["0123456789"]}""");

        var read = await ModelGatewayRequestBodyReader.ReadBodyBufferAsync(
            new MemoryStream(small),
            small.Length,
            small.Length,
            TestContext.Current.CancellationToken);
        var declared = await Assert.ThrowsAsync<ModelGatewayRequestBodyTooLargeException>(() =>
            ModelGatewayRequestBodyReader.ReadBodyBufferAsync(
                new MemoryStream(small),
                small.Length + 1,
                small.Length,
                TestContext.Current.CancellationToken));
        var streamed = await Assert.ThrowsAsync<ModelGatewayRequestBodyTooLargeException>(() =>
            ModelGatewayRequestBodyReader.ReadBodyBufferAsync(
                new MemoryStream(tooLarge),
                -1,
                small.Length,
                TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ModelGatewayRequestBodyReader.ReadBodyBufferAsync(
                new BlockingReadStream(),
                -1,
                small.Length,
                cancellation.Token));

        Assert.Equal(small, read);
        Assert.Contains("too large", declared.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("too large", streamed.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void ModelGatewayResponseContractsExposeStableOpenAiDataAndSafeErrors()
    {
        var root = CreateTempRoot();
        var now = new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);
        var settings = AppSettings.CreateDefault(root) with { Port = 8093, ContextSize = 32_768 };
        var first = new ModelRecord("z-model", "Zulu", Path.Combine(root, "zulu.gguf"), OwnershipKind.External, "{}", now);
        var second = new ModelRecord(
            "a-model",
            "Alpha",
            Path.Combine(root, "alpha.gguf"),
            OwnershipKind.External,
            """{"ggufContextLength":65536,"ggufParameterCount":32000000000}""",
            now.AddMinutes(1));
        WriteMinimalGguf(first.ModelPath);
        File.WriteAllBytes(second.ModelPath, new byte[1234]);
        var firstProfile = new NamedModelLaunchProfile("default:z-model", first.Id, "Default", ModelLaunchSettings.FromAppSettings(settings), now, true);
        var secondProfile = new NamedModelLaunchProfile(
            "default:a-model",
            second.Id,
            "Default",
            ModelLaunchSettings.FromAppSettings(settings with { ContextSize = 131_072 }),
            now.AddMinutes(1),
            true);
        var running = new LoadedModelSessionSnapshot(
            "session-1",
            first.Id,
            first.Name,
            "runtime-1",
            "CPU runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            settings,
            Path.Combine(root, "runtime.log"),
            now,
            "",
            123,
            LoadedModelSessionStatus.Running,
            IsRunning: true,
            IsSelected: true);
        var stopped = running with
        {
            SessionId = "session-2",
            ModelId = second.Id,
            ModelName = second.Name,
            Status = LoadedModelSessionStatus.Stopped,
            IsRunning = false
        };

        using var modelsJson = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            ModelGatewayResponseWriter.ModelsResponse([new(first, firstProfile), new(second, secondProfile)])));
        using var runningJson = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            ModelGatewayResponseWriter.RunningModelRows([stopped, running])));
        using var errorJson = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            ModelGatewayResponseWriter.GatewayError("upstream offline", "upstream_unavailable", "upstream_unavailable")));

        Assert.Equal("list", modelsJson.RootElement.GetProperty("object").GetString());
        var modelRows = modelsJson.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(["a-model", "z-model"], modelRows.Select(row => row.GetProperty("id").GetString()!).ToArray());
        Assert.Equal("local-llm-console", modelRows[0].GetProperty("owned_by").GetString());
        Assert.Equal(131_072, modelRows[0].GetProperty("context_length").GetInt32());
        Assert.Equal(32_768, modelRows[1].GetProperty("context_length").GetInt32());
        Assert.Equal(65_536, modelRows[0].GetProperty("meta").GetProperty("n_ctx_train").GetInt64());
        Assert.Equal(32_000_000_000, modelRows[0].GetProperty("meta").GetProperty("n_params").GetInt64());
        Assert.Equal(1234, modelRows[0].GetProperty("meta").GetProperty("size").GetInt64());
        Assert.Equal(32_768, modelRows[1].GetProperty("meta").GetProperty("n_ctx_train").GetInt64());
        Assert.Equal(7_000_000_000, modelRows[1].GetProperty("meta").GetProperty("n_params").GetInt64());
        Assert.Equal(new FileInfo(first.ModelPath).Length, modelRows[1].GetProperty("meta").GetProperty("size").GetInt64());
        var runningRow = Assert.Single(runningJson.RootElement.EnumerateArray());
        Assert.Equal(first.Id, runningRow.GetProperty("id").GetString());
        Assert.Equal("http://127.0.0.1:8093/v1", runningRow.GetProperty("endpoint").GetString());
        Assert.Equal("upstream_unavailable", errorJson.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("Friendly name -> model-id", ModelGatewayResponseWriter.GatewayClientLoadError(
            new ModelGatewayModelRoute(first with { Id = "model-id" }, firstProfile with { ModelId = "model-id" }),
            "Friendly name",
            new InvalidOperationException("runtime unavailable")), StringComparison.Ordinal);
        Assert.Equal("socket offline", ModelGatewayResponseWriter.InnermostMessage(
            new InvalidOperationException("outer", new IOException("socket offline"))));
        Assert.Equal("http://127.0.0.1:8093/", GatewayUrlReservationService.ListenerPrefixForPort(8093, allowLan: false));
        Assert.Equal("http://+:8093/", GatewayUrlReservationService.ListenerPrefixForPort(8093, allowLan: true));
    }

    [Fact]
    public void LocalGatewayAccessRejectsNonLoopbackPeersEvenWhenHttpSysUsesAWildcardReservation()
    {
        var options = new ModelGatewayOptions(
            true,
            "local",
            8082,
            "",
            false,
            ModelGatewaySwapPolicy.KeepLoaded);
        var policy = new ModelGatewayRequestAccessPolicy(options);

        Assert.True(policy.IsRemoteEndpointAllowed(IPAddress.Loopback));
        Assert.True(policy.IsRemoteEndpointAllowed(IPAddress.IPv6Loopback));
        Assert.False(policy.IsRemoteEndpointAllowed(IPAddress.Parse("192.168.1.50")));
        Assert.False(policy.IsRemoteEndpointAllowed(null));
    }


    [Fact]
    public async Task ModelGatewayListenerAuthenticatesListsLoadsAndProxiesOpenAiRequestsEndToEnd()
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var apiKey = new string('g', 32);
        var model = new ModelRecord("qwen-id", "Friendly Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var launchSettings = AppSettings.CreateDefault(root) with { Port = 8123, ModelApiKey = "direct-runtime-key" };
        var runtime = new GatewayIntegrationRuntimeController([model], launchSettings);
        using var upstreamHandler = new GatewayProxyHandler();
        var proxy = new ModelGatewayUpstreamProxy(new HttpClient(upstreamHandler));
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, apiKey, true, ModelGatewaySwapPolicy.KeepLoaded),
            runtime,
            proxy);
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };

        using var unauthorized = await client.GetAsync("v1/models", TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        using var health = await client.GetAsync("health", TestContext.Current.CancellationToken);
        using var models = await client.GetAsync("v1/models", TestContext.Current.CancellationToken);
        using var proxied = await client.PostAsync(
            "v1/chat/completions?trace=1",
            new StringContent("""{"model":"Friendly Qwen","messages":[{"role":"user","content":"hello"}]}""", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        using var running = await client.GetAsync("running", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("qwen-id", await models.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, proxied.StatusCode);
        Assert.Equal("{" + "\"proxied\":true}", await proxied.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("qwen-id", await running.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(1, runtime.EnsureLoadedCount);
        var forwarded = Assert.Single(upstreamHandler.Requests);
        Assert.Equal("/v1/chat/completions?trace=1", forwarded.PathAndQuery);
        Assert.Equal("Bearer direct-runtime-key", forwarded.Authorization);
        Assert.Contains("\"model\":\"Friendly Qwen\"", forwarded.Body, StringComparison.Ordinal);
    }


    [Fact]
    public async Task ModelGatewayListenerReturnsStableClientErrorsForRejectedRequestsEndToEnd()
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var apiKey = new string('h', 32);
        var model = new ModelRecord("qwen-id", "Friendly Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var runtime = new GatewayIntegrationRuntimeController([model], AppSettings.CreateDefault(root))
        {
            LoadFailure = new InvalidOperationException("runtime unavailable")
        };
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, apiKey, true, ModelGatewaySwapPolicy.SingleActive, MaxRequestBodyBytes: 96),
            runtime);
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var invalid = await client.PostAsync("v1/chat/completions", JsonContent("{" + "\"messages\":[]}"), TestContext.Current.CancellationToken);
        using var unknown = await client.PostAsync("v1/chat/completions", JsonContent("{" + "\"model\":\"unknown\"}"), TestContext.Current.CancellationToken);
        using var loadFailed = await client.PostAsync("v1/chat/completions", JsonContent("{" + "\"model\":\"qwen-id\"}"), TestContext.Current.CancellationToken);
        using var oversized = await client.PostAsync("v1/chat/completions", JsonContent("{" + "\"model\":\"qwen-id\",\"padding\":\"" + new string('x', 120) + "\"}"), TestContext.Current.CancellationToken);
        using var rejectedHostRequest = new HttpRequestMessage(HttpMethod.Get, "health");
        rejectedHostRequest.Headers.Host = $"example.com:{port}";
        using var rejectedHost = await client.SendAsync(rejectedHostRequest, TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("invalid_request_error", await invalid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("model_not_found", await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, loadFailed.StatusCode);
        Assert.Contains("model_load_failed", await loadFailed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Contains("request_too_large", await oversized.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, rejectedHost.StatusCode);
    }


    [Fact]
    public async Task ModelGatewayRejectsRequestsBeyondConfiguredConcurrency()
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var apiKey = new string('c', 32);
        var model = new ModelRecord("qwen-id", "Friendly Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new GatewayIntegrationRuntimeController([model], AppSettings.CreateDefault(root))
        {
            LoadStarted = loadStarted,
            ContinueLoad = continueLoad
        };
        using var upstreamHandler = new GatewayProxyHandler();
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(
                true,
                "local",
                port,
                apiKey,
                true,
                ModelGatewaySwapPolicy.KeepLoaded,
                MaxConcurrentRequests: 1),
            runtime,
            new ModelGatewayUpstreamProxy(new HttpClient(upstreamHandler)));
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var firstRequest = client.PostAsync(
            "v1/chat/completions",
            JsonContent("{" + "\"model\":\"qwen-id\"}"),
            TestContext.Current.CancellationToken);
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        using var overloaded = await client.GetAsync("health", TestContext.Current.CancellationToken);
        continueLoad.TrySetResult();
        using var firstResponse = await firstRequest;

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, overloaded.StatusCode);
        Assert.Contains("gateway_overloaded", await overloaded.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, firstResponse.StatusCode);
    }

    [Fact]
    public async Task ModelGatewayDefersOwnedResourceDisposalUntilLateHandlersFinish()
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var apiKey = new string('d', 32);
        var model = new ModelRecord("late-model", "Late model", Path.Combine(root, "late.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new GatewayIntegrationRuntimeController([model], AppSettings.CreateDefault(root))
        {
            LoadStarted = loadStarted,
            ContinueLoad = continueLoad,
            IgnoreLoadCancellation = true
        };
        using var upstreamHandler = new GatewayProxyHandler();
        var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, apiKey, true, ModelGatewaySwapPolicy.KeepLoaded),
            runtime,
            new ModelGatewayUpstreamProxy(new HttpClient(upstreamHandler)));
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        var request = client.PostAsync(
            "v1/chat/completions",
            JsonContent("{" + "\"model\":\"late-model\"}"),
            TestContext.Current.CancellationToken);
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await gateway.DisposeAsync();

        Assert.False(gateway.OwnedResourcesDisposed);
        Assert.False(gateway.OwnedResourceDisposalCompletion.IsCompleted);
        continueLoad.TrySetResult();
        try
        {
            using var response = await request;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Disposing the listener aborts the client response while the handler winds down.
        }
        await gateway.OwnedResourceDisposalCompletion.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.True(gateway.OwnedResourcesDisposed);
    }


    [Fact]
    public void GatewayActivityStatusTrackerOwnsGatewayStatusText()
    {
        Loc.LoadLanguage("en");
        var tracker = new GatewayActivityStatusTracker();
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082,
            AutoLoadGatewayPolicy = "singleActive"
        };
        var model = new ModelRecord("model", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;

        var disabled = tracker.Build(settings with { AutoLoadGatewayEnabled = false }, gatewayListening: false, now);
        var listening = tracker.Build(settings, gatewayListening: true, now);
        tracker.Start(model, "switching to", now);
        var activity = tracker.Build(settings, gatewayListening: true, now + TimeSpan.FromSeconds(5));
        tracker.SetPhase("loading");
        var loading = tracker.Build(settings, gatewayListening: true, now + TimeSpan.FromSeconds(6));
        tracker.Fail("not enough VRAM");
        var failed = tracker.Build(settings, gatewayListening: true, now + TimeSpan.FromSeconds(7));
        tracker.Complete();
        var completed = tracker.Build(settings, gatewayListening: false, now + TimeSpan.FromSeconds(8));

        Assert.Contains("disabled", disabled.Line, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GatewayStatusVisualKind.Normal, disabled.VisualKind);
        Assert.Contains("listening at http://127.0.0.1:8082", listening.Line, StringComparison.Ordinal);
        Assert.Contains(Loc.T("Pref.SingleActiveModel"), listening.Line, StringComparison.Ordinal);
        Assert.Equal(GatewayStatusVisualKind.Activity, activity.VisualKind);
        Assert.Contains("switching to Qwen", activity.Line, StringComparison.Ordinal);
        Assert.Contains("loading Qwen", loading.Line, StringComparison.Ordinal);
        Assert.Equal(GatewayStatusVisualKind.Warning, failed.VisualKind);
        Assert.Contains("not enough VRAM", failed.Line, StringComparison.Ordinal);
        Assert.Equal(GatewayStatusVisualKind.Normal, completed.VisualKind);
        Assert.Contains("not listening", completed.Line, StringComparison.Ordinal);
    }


    [Fact]
    public void GatewayActivityStatusControllerOwnsActivityTimer()
    {
        var source = ReadMainWindowSources();
        var state = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Core", "MainWindow.State.cs"));
        var controllerSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "GatewayActivityStatusController.cs"));
        var factorySource = ReadAppServiceFactorySources();
        var timerFactory = new ManualUiTimerFactory();
        var controller = new GatewayActivityStatusController(new GatewayActivityStatusTracker(), timerFactory);
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082
        };
        var model = new ModelRecord("model", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var tickCount = 0;

        controller.Start(model, "switching to", DateTimeOffset.UtcNow, () => tickCount++);

        Assert.True(controller.HasActivityTimer);
        Assert.Equal(1, tickCount);
        Assert.Single(timerFactory.Timers);
        Assert.Equal(TimeSpan.FromSeconds(1), timerFactory.Timers[0].Interval);
        Assert.True(timerFactory.Timers[0].Started);
        Assert.Contains("switching to Qwen", controller.Build(settings, gatewayListening: true, DateTimeOffset.UtcNow).Line, StringComparison.Ordinal);

        timerFactory.Timers[0].Fire();
        Assert.Equal(2, tickCount);

        controller.SetPhase("loading", () => tickCount++);
        Assert.Equal(3, tickCount);
        Assert.Contains("loading Qwen", controller.Build(settings, gatewayListening: true, DateTimeOffset.UtcNow).Line, StringComparison.Ordinal);

        controller.Fail("not enough VRAM", () => tickCount++);
        Assert.False(controller.HasActivityTimer);
        Assert.False(timerFactory.Timers[0].Started);
        Assert.Equal(4, tickCount);
        Assert.Contains("not enough VRAM", controller.Build(settings, gatewayListening: true, DateTimeOffset.UtcNow).Line, StringComparison.Ordinal);

        controller.Start(model, "loading", DateTimeOffset.UtcNow, () => tickCount++);
        Assert.True(controller.HasActivityTimer);
        Assert.Equal(2, timerFactory.Timers.Count);
        controller.Complete(() => tickCount++);
        Assert.False(controller.HasActivityTimer);
        Assert.False(timerFactory.Timers[1].Started);
        Assert.Contains("listening", controller.Build(settings, gatewayListening: true, DateTimeOffset.UtcNow).Line, StringComparison.Ordinal);
        Assert.Contains("DispatcherUiTimerFactory", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherUiTimerFactory", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new GatewayActivityStatusController()", state, StringComparison.Ordinal);
        Assert.DoesNotContain("_gatewayActivity = _coreServices.GatewayActivity", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.GatewayActivity.Start(model, phase, DateTimeOffset.Now, UpdateGatewayStatusText)", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.GatewayActivity.Complete(UpdateGatewayStatusText)", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.GatewayActivity.Fail(message, UpdateGatewayStatusText)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_coreServices.Ui.GatewayActivityTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GatewayActivityTimer_Tick", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopGatewayActivityTimer", source, StringComparison.Ordinal);
    }


}
