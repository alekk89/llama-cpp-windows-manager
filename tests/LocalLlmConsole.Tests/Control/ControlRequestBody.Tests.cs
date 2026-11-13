using System.Text;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ControlRequestBodyTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task ControlRequestBodyReaderAcceptsBoundedChunkedJsonWithoutCumulativeCopies()
    {
        var json = $"{{\"value\":\"{new string('x', 64 * 1024)}\"}}";
        await using var stream = new FragmentedReadStream(Encoding.UTF8.GetBytes(json), 257);

        var body = await ControlRequestBodyReader.ReadJsonObjectAsync(
            stream,
            Encoding.UTF8,
            contentLength: -1,
            maximumBytes: 1024 * 1024,
            TestContext.Current.CancellationToken);

        Assert.Equal(64 * 1024, body?["value"]?.GetValue<string>().Length);
        Assert.True(stream.ReadCount > 2);
    }

    [Fact]
    public async Task ControlRequestBodyReaderRejectsDeclaredAndStreamingOversizeBodies()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ControlRequestBodyReader.ReadJsonObjectAsync(
                Stream.Null,
                Encoding.UTF8,
                contentLength: 11,
                maximumBytes: 10,
                TestContext.Current.CancellationToken));

        await using var stream = new FragmentedReadStream(new byte[11], 3);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ControlRequestBodyReader.ReadJsonObjectAsync(
                stream,
                Encoding.UTF8,
                contentLength: -1,
                maximumBytes: 10,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("POST", "/api/v1/models/model/profiles/import", ControlRequestBodyLimits.ProfileImportBytes)]
    [InlineData("post", "/api/v1/models/owner%2Fmodel/profiles/IMPORT", ControlRequestBodyLimits.ProfileImportBytes)]
    [InlineData("GET", "/api/v1/models/model/profiles/import", ControlRequestBodyLimits.DefaultBytes)]
    [InlineData("POST", "/api/v1/models/model/profiles", ControlRequestBodyLimits.DefaultBytes)]
    [InlineData("PATCH", "/api/v1/settings", ControlRequestBodyLimits.DefaultBytes)]
    public void OnlyProfileImportReceivesTheLargerBodyLimit(string method, string path, int expected)
        => Assert.Equal(expected, ControlRequestBodyLimits.For(method, path));

    [Fact]
    public async Task ProfileImportLimitAcceptsAMaximumBundleAfterWorstCaseJsonEscaping()
    {
        // Control characters are escaped as \u00XX, the largest expansion a JSON string can apply.
        var bundle = new string('\u0001', ProfileBundleCodec.MaximumBytes);
        var json = new System.Text.Json.Nodes.JsonObject { ["text"] = bundle, ["dryRun"] = true, ["choices"] = new System.Text.Json.Nodes.JsonArray() }.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.True(bytes.Length > ControlRequestBodyLimits.DefaultBytes);

        await using var accepted = new MemoryStream(bytes);
        var body = await ControlRequestBodyReader.ReadJsonObjectAsync(accepted, Encoding.UTF8, bytes.Length,
            ControlRequestBodyLimits.For("POST", "/api/v1/models/model/profiles/import"), TestContext.Current.CancellationToken);
        Assert.Equal(bundle, body?["text"]?.GetValue<string>());

        await using var rejected = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ControlRequestBodyReader.ReadJsonObjectAsync(rejected, Encoding.UTF8,
            bytes.Length, ControlRequestBodyLimits.DefaultBytes, TestContext.Current.CancellationToken));
        Assert.Equal("This control API request body is limited to 1 MiB.", error.Message);
    }

    private sealed class FragmentedReadStream(byte[] data, int fragmentSize) : MemoryStream(data)
    {
        public int ReadCount { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, fragmentSize)], cancellationToken);
        }
    }
}
