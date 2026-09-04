using System.Text;
using System.Text.Json;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class GatewayRequestAllocationTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(20)]
    public async Task ShortReadsAndInaccurateLengthsReturnOnlyReceivedBytes(long declaredLength)
    {
        byte[] expected = [1, 2, 3, 4, 5, 6, 7];
        using var stream = new ShortReadStream(expected);
        var actual = await ModelGatewayRequestBodyReader.ReadBodyBufferAsync(
            stream, declaredLength, 20, TestContext.Current.CancellationToken);
        Assert.Equal(expected, actual);
        // Subsequent pooled-buffer reuse must not mutate a previously returned body.
        using var second = new MemoryStream(new byte[100_000]);
        await ModelGatewayRequestBodyReader.ReadBodyBufferAsync(second, second.Length, second.Length, TestContext.Current.CancellationToken);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ExcessInputStillChecksTheActualLimitAndCancellation()
    {
        using var stream = new ShortReadStream(new byte[8]);
        await Assert.ThrowsAsync<ModelGatewayRequestBodyTooLargeException>(() =>
            ModelGatewayRequestBodyReader.ReadBodyBufferAsync(stream, 3, 7, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var empty = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ModelGatewayRequestBodyReader.ReadBodyBufferAsync(empty, 0, 7, cancellation.Token));
    }

    [Fact]
    public void AliasRewritePreservesDuplicatePropertiesAndEscapedContent()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with { CustomParameters = "--alias runtime" };
        var body = Encoding.UTF8.GetBytes("""{"model":"first","nested":{"model":"inner"},"model":"last","prompt":"\u263a\n\""}""");
        using var result = JsonDocument.Parse(ModelGatewayRequestResolver.BodyForRuntime(body, settings));
        Assert.Equal(2, result.RootElement.EnumerateObject().Count(p => p.Name == "model" && p.Value.GetString() == "runtime"));
        Assert.Equal("inner", result.RootElement.GetProperty("nested").GetProperty("model").GetString());
        Assert.Equal("☺\n\"", result.RootElement.GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task LargeKnownLengthRequestHasBoundedWarmAllocations()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with { CustomParameters = "--alias runtime-name" };
        var payload = Encoding.UTF8.GetBytes("{\"model\":\"gateway-name\",\"prompt\":\"" + new string('x', 4 * 1024 * 1024) + "\"}");
        long allocated = 0;
        for (var sample = 0; sample < 3; sample++)
        {
            using var input = new MemoryStream(payload);
            var before = GC.GetAllocatedBytesForCurrentThread();
            // MemoryStream completes synchronously, so the counter stays on this thread.
            var pending = ModelGatewayRequestBodyReader.ReadBodyBufferAsync(input, input.Length, 64L * 1024 * 1024, TestContext.Current.CancellationToken);
            Assert.True(pending.IsCompletedSuccessfully);
            var read = await pending;
            var afterRead = GC.GetAllocatedBytesForCurrentThread();
            var rewritten = ModelGatewayRequestResolver.BodyForRuntime(read, settings);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.Current.TestOutputHelper!.WriteLine($"Sample {sample}: input={payload.Length}, read={afterRead - before}, total={allocated} bytes");
            using var document = JsonDocument.Parse(rewritten);
            Assert.Equal("runtime-name", document.RootElement.GetProperty("model").GetString());
            Assert.Equal(4 * 1024 * 1024, document.RootElement.GetProperty("prompt").GetString()!.Length);
        }
        // A broad allocation budget catches reintroduced whole-body copies; this is
        // not a timing assertion or a claim about peak memory or runtime throughput.
        Assert.True(allocated < payload.Length * 4L, $"Warm request allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData("{\"model\":\"runtime\",\"prompt\":[}")]
    [InlineData("{\"model\":\"runtime\"} {}")]
    [InlineData("{\"model\":\"runtime\"")]
    [InlineData("[]")]
    public void AliasForwardingRejectsMalformedJsonEvenForAnAcceptedAlias(string json)
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with { CustomParameters = "--alias runtime" };
        Assert.ThrowsAny<JsonException>(() => ModelGatewayRequestResolver.BodyForRuntime(Encoding.UTF8.GetBytes(json), settings));
    }

    [Fact]
    public void EscapedModelPropertyAndLongAliasPreserveOtherBytesExactly()
    {
        var alias = new string('x', 1000);
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with { CustomParameters = "--alias " + alias };
        const string suffix = ", \"number\": 1.00e+02, \"prompt\": \"\\u263a\" }  ";
        var body = Encoding.UTF8.GetBytes("{ \"mo\\u0064el\": \"short\"" + suffix);
        var rewritten = Encoding.UTF8.GetString(ModelGatewayRequestResolver.BodyForRuntime(body, settings));
        Assert.Equal("{ \"mo\\u0064el\": \"" + alias + "\"" + suffix, rewritten);
    }

    private sealed class ShortReadStream(byte[] body) : MemoryStream(body)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken);
    }
}
