using System.Text;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.Tests;

public sealed class OptionalApiKeyTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task LocalOnlyAuthenticationOptOutPersistsAndUnsafeLanStateMigratesBackToProtected()
    {
        var root = CreateTempRoot();
        var apiKey = new string('k', 32);
        await using var store = new StateStore(Path.Combine(root, "state", "settings.db"));
        await store.InitializeAsync();
        var localOpen = AppSettings.CreateDefault(root) with
        {
            RequireApiKeyAuth = false,
            ModelApiKey = "",
            ModelApiKeyBackup = apiKey
        };

        await store.SaveAppSettingsAsync(localOpen);
        var reloadedOpen = await store.GetAppSettingsAsync(root);

        Assert.False(reloadedOpen.RequireApiKeyAuth);
        Assert.Equal("", reloadedOpen.ModelApiKey);
        Assert.Equal(apiKey, reloadedOpen.ModelApiKeyBackup);

        await store.SaveAppSettingsAsync(localOpen with { ModelAccessMode = "both", Host = "0.0.0.0" });
        var migratedProtected = await store.GetAppSettingsAsync(root);

        Assert.True(migratedProtected.RequireApiKeyAuth);
        Assert.True(ApiSecurity.IsStrongBearerSecret(migratedProtected.ModelApiKey));
        Assert.Equal(migratedProtected.ModelApiKey, migratedProtected.ModelApiKeyBackup);
    }

    [Fact]
    public void SettingsRowsKeepTheOpenLocalKeyEmptyAndSynchronizeTheRestoredKey()
    {
        var backup = new string('r', 32);
        var openSettings = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            RequireApiKeyAuth = false,
            ModelApiKey = "",
            ModelApiKeyBackup = backup
        };
        var viewModel = new SettingsPageViewModel();
        viewModel.ReplaceRows(new SettingsPageDefinitionService().BuildRows(openSettings));
        var accessRow = viewModel.Rows.Single(row => row.Key == "modelAccessMode");
        var authenticationRow = viewModel.Rows.Single(row => row.Key == "requireApiKeyAuth");
        var keyRow = viewModel.Rows.Single(row => row.Key == "modelApiKey");

        Assert.Equal("", keyRow.Value);

        viewModel.ApplyPersistedSettings(openSettings with
        {
            RequireApiKeyAuth = true,
            ModelApiKey = backup
        });
        Assert.Equal(AppPreferenceService.EnableDisableLabel(true), authenticationRow.Value);
        Assert.Equal(backup, keyRow.Value);

        accessRow.Value = AppPreferenceService.ModelAccessModeLabel("both");
        authenticationRow.Value = AppPreferenceService.EnableDisableLabel(false);
        Assert.Equal(AppPreferenceService.ModelAccessModeLabel("local"), accessRow.Value);
        Assert.Equal("", keyRow.Value);

        accessRow.Value = AppPreferenceService.ModelAccessModeLabel("both");
        Assert.Equal(AppPreferenceService.EnableDisableLabel(true), authenticationRow.Value);
    }

    [Fact]
    public void RuntimeLaunchFactoryAllowsEmptyKeyOnlyForUnauthenticatedLoopback()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            RequireApiKeyAuth = false,
            ModelApiKey = "",
            ModelApiKeyBackup = new string('b', 32)
        };
        var request = RuntimeLaunchRequestFactory.Create(settings, new RuntimeLaunchRequestContext(
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            "llama-server.exe",
            "model.gguf",
            "127.0.0.1",
            AllowNetworkAccess: false));

        Assert.True(LlamaCppLaunchValidator.Validate(request).Ok);
        Assert.False(request.RequireApiKeyAuth);
        Assert.Equal("", request.ApiKey);

        var unsafeNetwork = request with { Host = "0.0.0.0", AllowNetworkAccess = true };
        var validation = LlamaCppLaunchValidator.Validate(unsafeNetwork);
        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, error => error.Contains("localhost", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RuntimeAuthenticationProbeAcceptsConfiguredUnauthenticatedEndpoint()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            RequireApiKeyAuth = false,
            ModelApiKey = ""
        };
        using var handler = new AuthenticationCaptureHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.MethodNotAllowed));
        using var http = new HttpClient(handler);

        var result = await new RuntimeEndpointProbeService(http)
            .VerifyAuthenticationAsync(settings, TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeAuthenticationProbeStatus.Verified, result.Status);
        Assert.Contains("as configured", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.All(handler.AuthorizationHeaders, header => Assert.Equal("", header));
    }

    [Fact]
    public async Task LocalGatewayCanServeAndProxyWithoutApiKeyButLanCannotStartOpen()
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var model = new ModelRecord("qwen-id", "Friendly Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var launchSettings = AppSettings.CreateDefault(root) with
        {
            Port = 8123,
            RequireApiKeyAuth = false,
            ModelApiKey = "",
            ModelApiKeyBackup = new string('b', 32)
        };
        var runtime = new GatewayIntegrationRuntimeController([model], launchSettings);
        using var upstreamHandler = new GatewayProxyHandler();
        var proxy = new ModelGatewayUpstreamProxy(new HttpClient(upstreamHandler));
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, "", false, ModelGatewaySwapPolicy.KeepLoaded),
            runtime,
            proxy);
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };

        using var models = await client.GetAsync("v1/models", TestContext.Current.CancellationToken);
        using var proxied = await client.PostAsync(
            "v1/chat/completions",
            new StringContent("{" + "\"model\":\"qwen-id\",\"messages\":[]}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, models.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, proxied.StatusCode);
        Assert.Equal("", Assert.Single(upstreamHandler.Requests).Authorization);

        await using var unsafeLanGateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "gateway", ReserveLoopbackPort(), "", false, ModelGatewaySwapPolicy.KeepLoaded),
            runtime);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unsafeLanGateway.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("local-only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AuthenticationCaptureHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> AuthorizationHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString() ?? "");
            return Task.FromResult(respond(request));
        }
    }
}
