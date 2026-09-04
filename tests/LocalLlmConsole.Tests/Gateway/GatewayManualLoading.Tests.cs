using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class GatewayManualLoadingTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData(ModelGatewaySwapPolicy.KeepLoaded)]
    [InlineData(ModelGatewaySwapPolicy.SingleActive)]
    public async Task ManualModeListsAndServesOnlyExactLoadedProfilesWithoutInvokingLifecycle(ModelGatewaySwapPolicy policy)
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { CustomParameters = "--alias qwen" };
        var model = new ModelRecord("model-id", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profiles = Enumerable.Range(0, 3).Select(index => new NamedModelLaunchProfile(
            $"profile-{index}", model.Id, index == 0 ? "Default" : $"CUDA {index}",
            ModelLaunchSettings.FromAppSettings(settings with { Port = 8200 + index }),
            DateTimeOffset.UtcNow, IsDefault: index == 0)).ToArray();
        var routes = ModelGatewayRouteId.EnsureUnique(profiles.Select(profile => new ModelGatewayModelRoute(model, profile)).ToArray());
        var sessions = profiles.Skip(1).Select(profile => RuntimeMetricSession(root, profile.Settings.ApplyTo(settings)) with
        {
            SessionId = profile.Id,
            ModelId = model.Id,
            LaunchProfileId = profile.Id
        }).ToList();
        var lifecycleCalls = 0;
        var runtime = new ModelGatewayRuntimeController(new ModelGatewayRuntimeControllerActions(
            _ => Task.FromResult(routes),
            _ => Task.FromResult<IReadOnlyList<LoadedModelSessionSnapshot>>(sessions.ToArray()),
            (_, _, _) =>
            {
                lifecycleCalls++;
                throw new InvalidOperationException("Manual mode must never invoke load or swap workflows.");
            }));
        using var handler = new GatewayProxyHandler();
        using var upstream = new HttpClient(handler);
        var port = ReserveLoopbackPort();
        var key = new string('m', 32);
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, key, true, policy, AutoLoadModels: false),
            runtime, new ModelGatewayUpstreamProxy(upstream));
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };

        using var unauthorized = await client.GetAsync("v1/models", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(["qwen:2", "qwen:3"], await ListedIdsAsync(client));

        using var health = await client.GetAsync("health", TestContext.Current.CancellationToken);
        using var healthBody = JsonDocument.Parse(await health.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(healthBody.RootElement.GetProperty("autoLoadModels").GetBoolean());
        foreach (var id in new[] { "qwen:2", "qwen:3", routes[1].LegacyId })
        {
            using var response = await RequestAsync(client, id);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        Assert.Equal(2, sessions.Count);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("qwen", ModelGatewayRequestResolver.ExtractRequestedModel(System.Text.Encoding.UTF8.GetBytes(request.Body))));

        // A loaded custom profile must not make the unloaded default route eligible.
        using var missingDefault = await RequestAsync(client, "qwen");
        await AssertNotLoadedAsync(missingDefault);
        using var unknown = await RequestAsync(client, "unknown");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // A stopped session may still appear in a snapshot; it must not be advertised or reloaded.
        sessions[0] = sessions[0] with { IsRunning = false, Status = LoadedModelSessionStatus.Stopped };
        Assert.Equal(["qwen:3"], await ListedIdsAsync(client));
        using var stale = await RequestAsync(client, "qwen:2");
        await AssertNotLoadedAsync(stale);
        sessions.Clear();
        Assert.Empty(await ListedIdsAsync(client));
        using var empty = await RequestAsync(client, "qwen:3");
        await AssertNotLoadedAsync(empty);
        Assert.Equal(0, lifecycleCalls);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutoLoadSettingControlsDiscoveryAndWhetherAnUnloadedProfileCanStart(bool autoLoad)
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { GatewayAutoLoadModels = autoLoad, RequireApiKeyAuth = false, AutoLoadGatewayPort = ReserveLoopbackPort() };
        var model = new ModelRecord("qwen", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var runtime = new GatewayIntegrationRuntimeController([model], settings);
        using var handler = new GatewayProxyHandler();
        using var upstream = new HttpClient(handler);
        await using var gateway = new ModelGatewayService(ModelGatewayOptions.FromSettings(settings), runtime, new ModelGatewayUpstreamProxy(upstream));
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.AutoLoadGatewayPort}/"), Timeout = TimeSpan.FromSeconds(10) };

        Assert.Equal(autoLoad ? new[] { "qwen" } : [], await ListedIdsAsync(client));
        using var response = await RequestAsync(client, "qwen");
        Assert.Equal(autoLoad ? HttpStatusCode.Accepted : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(autoLoad ? 1 : 0, runtime.EnsureLoadedCount);
    }

    [Fact]
    public async Task OlderSettingsKeepAutoLoadingAndManualModePersistsAcrossReopening()
    {
        var root = CreateTempRoot();
        var database = Path.Combine(root, "state", "gateway-mode.db");
        await using (var store = new StateStore(database))
        {
            await store.InitializeAsync();
            Assert.DoesNotContain("gatewayAutoLoadModels", (await store.ListSettingsAsync()).Keys);
            var oldSettings = await store.GetAppSettingsAsync(root);
            Assert.True(oldSettings.GatewayAutoLoadModels);
            Assert.True(ModelGatewayOptions.FromSettings(oldSettings).AutoLoadModels);
            await store.SaveAppSettingsAsync(oldSettings with { GatewayAutoLoadModels = false });
        }
        await using var reopened = new StateStore(database);
        await reopened.InitializeAsync();
        var reloaded = await reopened.GetAppSettingsAsync(root);
        Assert.False(reloaded.GatewayAutoLoadModels);
        Assert.True(reloaded.AutoLoadGatewayEnabled);
        Assert.False(ModelGatewayOptions.FromSettings(reloaded).AutoLoadModels);
    }

    [Fact]
    public void SettingsAndControlPatchExposeTheModeAndRequireGatewayReconfiguration()
    {
        var root = CreateTempRoot();
        var current = AppSettings.CreateDefault(root);
        var service = new AppSettingsUpdateService();
        var request = new AppSettingsUpdateRequest(current, root, current.ThemeMode,
            new Dictionary<string, string> { ["gatewayAutoLoadModels"] = "No" }, new HashSet<int>());
        var result = service.Build(request);
        Assert.True(result.Success);
        Assert.False(result.Settings.GatewayAutoLoadModels);
        Assert.Equal(current.AutoLoadGatewayEnabled, result.Settings.AutoLoadGatewayEnabled);
        Assert.Equal(current.AutoLoadGatewayPolicy, result.Settings.AutoLoadGatewayPolicy);
        Assert.True(AppSettingsApplicationService.GatewaySettingsChanged(current, result.Settings));
        Assert.True(AppSettingsApplicationService.GatewaySettingsChanged(result.Settings, current));
        var preserved = service.Build(request with { CurrentSettings = result.Settings, Values = new Dictionary<string, string>() });
        Assert.False(preserved.Settings.GatewayAutoLoadModels);
        Assert.False(AppSettingsApplicationService.GatewaySettingsChanged(result.Settings, preserved.Settings));

        var schema = JsonSerializer.Serialize(ControlEndpointHandler.SettingsSchema<AppSettings>());
        Assert.Contains("\"name\":\"gatewayAutoLoadModels\"", schema, StringComparison.Ordinal);
        var mutation = new ControlAppSettingsMutationService();
        var patched = mutation.Patch(current, JsonNode.Parse("""{"gatewayAutoLoadModels":false}""")!.AsObject(), []);
        Assert.False(patched.GatewayAutoLoadModels);
        Assert.False(ControlJsonPatch.RedactedAppSettings(patched)["gatewayAutoLoadModels"]!.GetValue<bool>());
        Assert.True(mutation.Patch(patched, JsonNode.Parse("""{"gatewayAutoLoadModels":true}""")!.AsObject(), []).GatewayAutoLoadModels);
        Assert.Throws<InvalidOperationException>(() => mutation.Patch(current, JsonNode.Parse("""{"gatewayAutoLoadModels":"invalid"}""")!.AsObject(), []));
    }

    [Fact]
    public void ManualModeStatusDoesNotClaimAutoLoadingOrShowStaleLoadErrors()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { GatewayAutoLoadModels = false };
        var tracker = new GatewayActivityStatusTracker();
        tracker.Fail("old load failed");
        var status = tracker.Build(settings, true, DateTimeOffset.UtcNow);
        Assert.Contains("Loaded profiles only", status.Line, StringComparison.Ordinal);
        Assert.DoesNotContain("old load failed", status.Line, StringComparison.Ordinal);
        Assert.Equal(GatewayStatusVisualKind.Normal, status.VisualKind);
        Assert.Contains("disabled", status.ToolTip, StringComparison.Ordinal);
        Assert.Contains("not listening", tracker.Build(settings, false, DateTimeOffset.UtcNow).Line, StringComparison.Ordinal);
        Assert.Contains("disabled", tracker.Build(settings with { AutoLoadGatewayEnabled = false }, false, DateTimeOffset.UtcNow).Line, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, string model)
        => client.PostAsync("v1/chat/completions", JsonContent(JsonSerializer.Serialize(new { model, messages = Array.Empty<object>() })), TestContext.Current.CancellationToken);

    private static async Task<string[]> ListedIdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("v1/models", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).Order().ToArray();
    }

    private static async Task AssertNotLoadedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("model_not_loaded", body.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("auto-loading is disabled", body.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}
