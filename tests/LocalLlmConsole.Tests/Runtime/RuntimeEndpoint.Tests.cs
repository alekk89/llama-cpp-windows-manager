using System.Diagnostics;
using System.Text;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeEndpointTests : ManagerRegressionTestBase
{
    [Fact]
    public void RuntimeEndpointServiceAddsBearerTokenWhenPresent()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { ModelApiKey = "  secret-token  " };

        using var request = RuntimeEndpointService.RuntimeGetRequest("http://127.0.0.1:8081/health", settings);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("secret-token", request.Headers.Authorization?.Parameter);
    }


    [Fact]
    public async Task RuntimeEndpointProbeServiceRequiresSuccessForAliveAndSendsAuth()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = 8081,
            ModelApiKey = "secret-token"
        };
        var requests = new List<HttpRequestMessage>();
        using var handler = new CapturingHttpHandler(request =>
        {
            requests.Add(CloneRequest(request));
            if (request.RequestUri?.AbsolutePath == "/health")
                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
            if (request.RequestUri?.AbsolutePath == "/v1/models")
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        var service = new RuntimeEndpointProbeService(http);

        var alive = await service.IsAliveAsync(settings, TestContext.Current.CancellationToken);

        Assert.True(alive);
        Assert.Equal(["/health", "/v1/models"], requests.Select(request => request.RequestUri!.AbsolutePath).ToArray());
        Assert.All(requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("secret-token", request.Headers.Authorization?.Parameter);
        });
    }


    [Fact]
    public async Task RuntimeEndpointProbeServiceTreatsAnyHttpResponseAsResponding()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081 };
        var requests = new List<HttpRequestMessage>();
        using var handler = new CapturingHttpHandler(request =>
        {
            requests.Add(CloneRequest(request));
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        });
        using var http = new HttpClient(handler);
        var service = new RuntimeEndpointProbeService(http);

        var responding = await service.IsRespondingAsync(settings, TestContext.Current.CancellationToken);

        Assert.True(responding);
        Assert.Equal(["/health"], requests.Select(request => request.RequestUri!.AbsolutePath).ToArray());
    }


    [Fact]
    public async Task RuntimeEndpointProbeServiceReadsServedModelsAndFailsClosed()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081 };
        using var handler = new CapturingHttpHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/v1/models")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":[{"id":"qwen"}],"models":["llama"]}""")
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        var service = new RuntimeEndpointProbeService(http);
        using var failingHandler = new CapturingHttpHandler(_ => throw new HttpRequestException("offline"));
        using var failingHttp = new HttpClient(failingHandler);
        var failingService = new RuntimeEndpointProbeService(failingHttp);

        var served = await service.ServedModelsAsync(settings, TestContext.Current.CancellationToken);
        var failed = await failingService.ServedModelsAsync(settings, TestContext.Current.CancellationToken);

        Assert.Equal(["qwen", "llama"], served);
        Assert.Empty(failed);
    }

    [Fact]
    public async Task RuntimeEndpointProbeServiceVerifiesEnforcementAndConfiguredCredential()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            Port = 8081,
            ModelApiKey = "secret-token"
        };
        using var enforcingHandler = new CapturingHttpHandler(request =>
            request.Headers.Authorization?.Parameter == settings.ModelApiKey
                ? new HttpResponseMessage(System.Net.HttpStatusCode.MethodNotAllowed)
                : new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
        using var enforcingHttp = new HttpClient(enforcingHandler);
        using var openHandler = new CapturingHttpHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.MethodNotAllowed));
        using var openHttp = new HttpClient(openHandler);

        var verified = await new RuntimeEndpointProbeService(enforcingHttp)
            .VerifyAuthenticationAsync(settings, TestContext.Current.CancellationToken);
        var notEnforced = await new RuntimeEndpointProbeService(openHttp)
            .VerifyAuthenticationAsync(settings, TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeAuthenticationProbeStatus.Verified, verified.Status);
        Assert.Equal(RuntimeAuthenticationProbeStatus.NotEnforced, notEnforced.Status);
        Assert.DoesNotContain(settings.ModelApiKey, verified.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(settings.ModelApiKey, notEnforced.Message, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowDelegatesRuntimeEndpointProbesToService()
    {
        var runtimeSession = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Runtimes", "MainWindow.RuntimeSession.cs"));
        var runtimeLifecycle = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Runtimes", "MainWindow.ModelRuntimeLifecycle.cs"));
        var gateway = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Gateway", "MainWindow.Gateway.cs"));

        Assert.Contains("_coreServices.Runtime.RuntimeEndpointProbe.ServedModelsAsync", runtimeSession, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeEndpointProbe.IsAliveAsync", runtimeSession, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeEndpointProbe.IsRespondingAsync", runtimeSession, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeEndpointProbe.IsRespondingAsync", runtimeLifecycle, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeEndpointProbe.VerifyAuthenticationAsync", runtimeLifecycle, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeEndpointProbe.IsAliveAsync", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("new HttpClient", runtimeSession, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeEndpointAliveAsync", runtimeSession + runtimeLifecycle + gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeEndpointRespondingAsync", runtimeSession + runtimeLifecycle + gateway, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeEndpointServiceParsesServedModelsAndMatchesRegistrations()
    {
        const string json = """
        {
          "data": [
            { "id": "registered-id" },
            { "model": "D:\\models\\Qwen3-8B.gguf" },
            { "name": "Friendly Qwen" }
          ],
          "models": [ "plain-model" ]
        }
        """;
        var now = DateTimeOffset.UtcNow;
        var model = new ModelRecord("registered-id", "Friendly Qwen", @"D:\models\Qwen3-8B.gguf", OwnershipKind.External, "{}", now);

        var served = RuntimeEndpointService.ExtractServedModelIds(json).ToArray();

        Assert.Equal(["registered-id", @"D:\models\Qwen3-8B.gguf", "Friendly Qwen", "plain-model"], served);
        Assert.True(RuntimeEndpointService.ServedModelMatches(model, "registered-id"));
        Assert.True(RuntimeEndpointService.ServedModelMatches(model, @"D:\other\Qwen3-8B.gguf"));
        Assert.True(RuntimeEndpointService.ServedModelMatches(model, "Friendly Qwen"));
        Assert.False(RuntimeEndpointService.ServedModelMatches(model, "other-model"));
    }


    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }


}
