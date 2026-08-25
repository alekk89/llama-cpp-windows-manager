using System.Text;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed partial class ReleaseHardeningTests
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
