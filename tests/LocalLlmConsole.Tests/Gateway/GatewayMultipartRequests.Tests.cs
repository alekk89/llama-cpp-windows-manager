using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class GatewayMultipartRequestsTests : ManagerRegressionTestBase
{
    private const string Boundary = "gateway-test-boundary";

    [Fact]
    public void MultipartReaderFindsModelFieldAfterBinaryFilePart()
    {
        var body = MultipartBody(
            ("file", "audio.wav", [0x0d, 0x0a, 0x2d, 0x2d, 0x00, 0xff, 0x0d, 0x0a]),
            ("model", null, Encoding.UTF8.GetBytes("  whisper-large  ")),
            ("language", null, Encoding.UTF8.GetBytes("en")));

        Assert.Equal("whisper-large", ModelGatewayRequestResolver.ExtractRequestedModel(body, $"multipart/form-data; boundary={Boundary}"));
        Assert.Equal("whisper-large", ModelGatewayRequestResolver.ExtractRequestedModel(body, $"Multipart/Form-Data; boundary=\"{Boundary}\""));
        Assert.Equal("en", ModelGatewayMultipartFormReader.ReadTextField(body, Boundary, "language"));
    }

    [Fact]
    public void MultipartReaderIgnoresFilePartsMissingFieldsAndOversizedValues()
    {
        var fileNamedModel = MultipartBody(("model", "model.bin", Encoding.UTF8.GetBytes("qwen")));
        var oversized = MultipartBody(("model", null, Encoding.UTF8.GetBytes(new string('m', ModelGatewayMultipartFormReader.MaxFieldBytes + 1))));
        var missing = MultipartBody(("prompt", null, Encoding.UTF8.GetBytes("edit this")));
        var truncated = Encoding.ASCII.GetBytes($"--{Boundary}\r\nContent-Disposition: form-data; name=\"model\"\r\n\r\nqwen");
        var contentType = $"multipart/form-data; boundary={Boundary}";

        Assert.Equal("", ModelGatewayRequestResolver.ExtractRequestedModel(fileNamedModel, contentType));
        Assert.Equal("", ModelGatewayRequestResolver.ExtractRequestedModel(oversized, contentType));
        Assert.Equal("", ModelGatewayRequestResolver.ExtractRequestedModel(missing, contentType));
        Assert.Equal("", ModelGatewayRequestResolver.ExtractRequestedModel(truncated, contentType));
        Assert.Equal("", ModelGatewayRequestResolver.ExtractRequestedModel(missing, "multipart/form-data"));
        Assert.Equal("qwen", ModelGatewayRequestResolver.ExtractRequestedModel(Encoding.UTF8.GetBytes("""{"model":"qwen"}"""), "application/json"));
        Assert.Equal("qwen", ModelGatewayRequestResolver.ExtractRequestedModel(Encoding.UTF8.GetBytes("""{"model":"qwen"}"""), null));
    }

    [Theory]
    [InlineData("v1/audio/transcriptions")]
    [InlineData("v1/images/edits")]
    public async Task GatewayRoutesMultipartRequestsAndForwardsOriginalBytes(string path)
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var model = new ModelRecord("whisper-id", "Whisper", Path.Combine(root, "whisper.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        // An alias would rewrite a JSON body; multipart bodies must stay byte-identical.
        var launchSettings = AppSettings.CreateDefault(root) with { CustomParameters = "--alias whisper-runtime" };
        var runtime = new GatewayIntegrationRuntimeController([model], launchSettings);
        using var upstream = new RawCapturingHandler();
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, "", false, ModelGatewaySwapPolicy.KeepLoaded),
            runtime,
            new ModelGatewayUpstreamProxy(new HttpClient(upstream)));
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };
        using var content = new MultipartFormDataContent(Boundary);
        var file = new ByteArrayContent([0x52, 0x49, 0x46, 0x46, 0x0d, 0x0a, 0x00, 0xff]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", "input.bin");
        content.Add(new StringContent("whisper-runtime"), "model");
        var expectedBody = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        using var response = await client.PostAsync(path, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, runtime.EnsureLoadedCount);
        Assert.Equal("/" + path, upstream.PathAndQuery);
        Assert.Equal(expectedBody, upstream.Body);
        Assert.StartsWith("multipart/form-data", upstream.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Boundary, upstream.ContentType, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GatewayRejectsMultipartRequestWithoutModelField()
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var model = new ModelRecord("whisper-id", "Whisper", Path.Combine(root, "whisper.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var runtime = new GatewayIntegrationRuntimeController([model], AppSettings.CreateDefault(root));
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, "", false, ModelGatewaySwapPolicy.KeepLoaded),
            runtime);
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };
        using var content = new MultipartFormDataContent(Boundary) { { new ByteArrayContent([1, 2, 3]), "file", "a.wav" } };

        using var response = await client.PostAsync("v1/audio/transcriptions", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, runtime.EnsureLoadedCount);
    }

    private static byte[] MultipartBody(params (string Name, string? FileName, byte[] Value)[] parts)
    {
        using var stream = new MemoryStream();
        void Write(string text) => stream.Write(Encoding.ASCII.GetBytes(text));
        Write("preamble\r\n");
        foreach (var part in parts)
        {
            Write($"--{Boundary}\r\nContent-Disposition: form-data; name=\"{part.Name}\"");
            if (part.FileName is not null) Write($"; filename=\"{part.FileName}\"");
            Write("\r\n\r\n");
            stream.Write(part.Value);
            Write("\r\n");
        }
        Write($"--{Boundary}--\r\n");
        return stream.ToArray();
    }

    private sealed class RawCapturingHandler : HttpMessageHandler
    {
        public string PathAndQuery { get; private set; } = "";
        public string ContentType { get; private set; } = "";
        public byte[] Body { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            PathAndQuery = request.RequestUri?.PathAndQuery ?? "";
            ContentType = request.Content?.Headers.ContentType?.ToString() ?? "";
            Body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\":\"ok\"}", Encoding.UTF8, "application/json")
            };
        }
    }
}
