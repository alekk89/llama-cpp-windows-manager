using System.Net;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class GatewayStartupPermissionTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task GatewayStartsWithFreshListenerAfterPermissionClosesTheOriginalListener()
    {
        var port = ReserveLoopbackPort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var failedListener = new HttpListener();
        failedListener.Prefixes.Add(prefix);
        // Windows HttpListener.Start permanently closes the instance on access denied.
        failedListener.Close();

        using var started = ModelGatewayService.StartListenerAfterPermission(failedListener, prefix);
        Assert.NotSame(failedListener, started);
        Assert.True(started.IsListening);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var request = client.GetAsync(prefix, TestContext.Current.CancellationToken);
        var context = await started.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        context.Response.StatusCode = 204;
        context.Response.Close();
        using var response = await request;
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
