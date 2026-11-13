using System.Net;
using System.Text;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class GatewayBrowserOriginTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData("local", null, true)]
    [InlineData("local", "http://127.0.0.1:3000", true)]
    [InlineData("local", "http://localhost:5173", true)]
    [InlineData("local", "https://[::1]:8443", true)]
    [InlineData("local", "https://evil.example", false)]
    [InlineData("local", "http://192.168.1.20:3000", false)]
    [InlineData("local", "null", false)]
    [InlineData("local", "file:///C:/page.html", false)]
    [InlineData("local", "not an origin", false)]
    [InlineData("gateway", "http://192.168.1.20:3000", true)]
    [InlineData("gateway", "https://chat.example", true)]
    [InlineData("gateway", "null", false)]
    [InlineData("gateway", "chrome-extension://abc", false)]
    public void GatewayOriginPolicyAllowsOnlyDocumentedBrowserOrigins(string accessMode, string? origin, bool expected)
    {
        var policy = new ModelGatewayRequestAccessPolicy(new ModelGatewayOptions(
            true,
            accessMode,
            8082,
            new string('k', 32),
            true,
            ModelGatewaySwapPolicy.KeepLoaded));

        Assert.Equal(expected, policy.IsRequestOriginAllowed(origin));
    }

    [Theory]
    [InlineData("Access-Control-Allow-Origin", true)]
    [InlineData("access-control-allow-credentials", true)]
    [InlineData("Vary", true)]
    [InlineData("Cache-Control", true)]
    [InlineData("X-Content-Type-Options", true)]
    [InlineData("X-Request-Id", false)]
    [InlineData("Content-Type", false)]
    public void GatewayOwnedResponseHeadersAreRecognized(string header, bool expected)
        => Assert.Equal(expected, ModelGatewayRequestAccessPolicy.IsGatewayOwnedResponseHeader(header));

    [Fact]
    public async Task UnauthenticatedLocalGatewayRejectsForeignBrowserOriginsBeforeLoadingModels()
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var model = new ModelRecord("qwen-id", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var runtime = new GatewayIntegrationRuntimeController([model], AppSettings.CreateDefault(root));
        using var upstream = new GatewayProxyHandler();
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, "", false, ModelGatewaySwapPolicy.SingleActive),
            runtime,
            new ModelGatewayUpstreamProxy(new HttpClient(upstream)));
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };

        using var foreignPost = await SendAsync(client, HttpMethod.Post, "https://evil.example", """{"model":"qwen-id"}""");
        using var foreignPreflight = await SendAsync(client, HttpMethod.Options, "https://evil.example", null);
        using var opaquePost = await SendAsync(client, HttpMethod.Post, "null", """{"model":"qwen-id"}""");
        using var localPreflight = await SendAsync(client, HttpMethod.Options, "http://localhost:5173", null);
        using var localPost = await SendAsync(client, HttpMethod.Post, "http://localhost:5173", """{"model":"qwen-id"}""");

        Assert.Equal(HttpStatusCode.Forbidden, foreignPost.StatusCode);
        Assert.Contains("forbidden", await foreignPost.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.False(foreignPost.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(HttpStatusCode.Forbidden, foreignPreflight.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, opaquePost.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, localPreflight.StatusCode);
        Assert.Equal("http://localhost:5173", Assert.Single(localPreflight.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(HttpStatusCode.Accepted, localPost.StatusCode);
        // Only the allowed local request reached the runtime.
        Assert.Equal(1, runtime.EnsureLoadedCount);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task UpstreamResponseCannotOverwriteGatewayCorsAndCacheHeaders()
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var model = new ModelRecord("qwen-id", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var runtime = new GatewayIntegrationRuntimeController([model], AppSettings.CreateDefault(root));
        using var upstream = new CapturingHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            };
            response.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", "*");
            response.Headers.TryAddWithoutValidation("Access-Control-Allow-Credentials", "true");
            response.Headers.TryAddWithoutValidation("Vary", "Accept-Encoding");
            response.Headers.TryAddWithoutValidation("Cache-Control", "public, max-age=600");
            response.Headers.TryAddWithoutValidation("X-Upstream-Trace", "kept");
            return response;
        });
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, "", false, ModelGatewaySwapPolicy.KeepLoaded),
            runtime,
            new ModelGatewayUpstreamProxy(new HttpClient(upstream)));
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };

        using var response = await SendAsync(client, HttpMethod.Post, "http://127.0.0.1:3000", """{"model":"qwen-id"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://127.0.0.1:3000", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Equal("Origin", string.Join(",", response.Headers.GetValues("Vary")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("kept", Assert.Single(response.Headers.GetValues("X-Upstream-Trace")));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string? origin, string? json)
    {
        using var request = new HttpRequestMessage(method, "v1/chat/completions");
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
