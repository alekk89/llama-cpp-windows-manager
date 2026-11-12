using System.Net;
using System.Text;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class GatewayClientDisconnectTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task ClientDisconnectDuringStreamingCancelsUpstreamAndReleasesRequestCapacity()
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var model = new ModelRecord("stream-id", "Stream model", Path.Combine(root, "stream.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var runtime = new GatewayIntegrationRuntimeController([model], AppSettings.CreateDefault(root));
        using var upstream = new EndlessStreamHandler();
        var performance = new GatewayPerformanceTracker();
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, "", false, ModelGatewaySwapPolicy.KeepLoaded, MaxConcurrentRequests: 1),
            runtime,
            new ModelGatewayUpstreamProxy(new HttpClient(upstream), performance));
        await gateway.StartAsync(TestContext.Current.CancellationToken);

        // Do not drain the endless body on dispose; close the connection instead.
        using (var disconnectingClient = new HttpClient(new SocketsHttpHandler { MaxResponseDrainSize = 0 }))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v1/chat/completions")
            {
                Content = JsonContent("""{"model":"stream-id","stream":true}""")
            };
            using var response = await disconnectingClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            var buffer = new byte[64];
            Assert.True(await body.ReadAsync(buffer, TestContext.Current.CancellationToken) > 0);
        }

        await upstream.UpstreamClosed.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => gateway.ClientDisconnectCount == 1);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };
        var status = HttpStatusCode.ServiceUnavailable;
        for (var attempt = 0; attempt < 50 && status == HttpStatusCode.ServiceUnavailable; attempt++)
        {
            using var next = await client.PostAsync("v1/chat/completions", JsonContent("""{"model":"stream-id"}"""), TestContext.Current.CancellationToken);
            status = next.StatusCode;
            if (status == HttpStatusCode.ServiceUnavailable) await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, gateway.ClientDisconnectCount);
        Assert.Equal(1, performance.Snapshot().FailureCount);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not reached in time.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private sealed class EndlessStreamHandler : HttpMessageHandler
    {
        private int _requests;

        public TaskCompletionSource UpstreamClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requests) > 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
                });
            }

            var content = new StreamContent(new EndlessEventStream(this));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }

        private sealed class EndlessEventStream(EndlessStreamHandler owner) : Stream
        {
            private static readonly byte[] Event = Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"x\"}}]}\n\n");

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await Task.Delay(10, cancellationToken);
                var count = Math.Min(buffer.Length, Event.Length);
                Event.AsMemory(0, count).CopyTo(buffer);
                return count;
            }

            protected override void Dispose(bool disposing)
            {
                owner.UpstreamClosed.TrySetResult();
                base.Dispose(disposing);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
