using System.Buffers;

namespace LocalLlmConsole.Services;

public sealed class ModelGatewayRequestBodyTooLargeException : InvalidOperationException
{
    public ModelGatewayRequestBodyTooLargeException(string message) : base(message)
    {
    }
}

public sealed class ModelGatewayRequestBodyTimeoutException : TimeoutException
{
    public ModelGatewayRequestBodyTimeoutException(string message) : base(message)
    {
    }
}

public static class ModelGatewayRequestBodyReader
{
    private const int BufferSize = 81920;

    public static async Task<byte[]> ReadBodyBufferAsync(
        Stream stream,
        long contentLength,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxBytes <= 0)
            throw new InvalidOperationException("Gateway request body limit must be greater than zero.");
        if (contentLength > maxBytes)
            throw new ModelGatewayRequestBodyTooLargeException($"Gateway request body is too large. Limit is {DisplayFormatService.Bytes(maxBytes)}.");

        using var memory = contentLength is > 0 and <= int.MaxValue
            ? new MemoryStream((int)contentLength)
            : new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > maxBytes)
                    throw new ModelGatewayRequestBodyTooLargeException($"Gateway request body is too large. Limit is {DisplayFormatService.Bytes(maxBytes)}.");
                memory.Write(buffer, 0, read);
            }

            // Transfer the stream-owned array when the declared length was exact.
            // Never expose unused capacity or a pooled array to the caller.
            if (memory.TryGetBuffer(out var body) && body.Count == body.Array!.Length)
                return body.Array;
            return memory.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
