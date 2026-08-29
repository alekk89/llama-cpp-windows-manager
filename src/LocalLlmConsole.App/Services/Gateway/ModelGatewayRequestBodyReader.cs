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
        var buffer = new byte[BufferSize];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > maxBytes)
                throw new ModelGatewayRequestBodyTooLargeException($"Gateway request body is too large. Limit is {DisplayFormatService.Bytes(maxBytes)}.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return memory.ToArray();
    }
}
