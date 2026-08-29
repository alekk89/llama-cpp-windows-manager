using System.Buffers;

namespace LocalLlmConsole.Services;

internal static class BoundedStreamCopyService
{
    private const int BufferSize = 128 * 1024;

    public static async Task<long> CopyToAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        long initialBytes = 0,
        Func<long, CancellationToken, ValueTask>? progress = null,
        TimeSpan? readIdleTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (initialBytes < 0) throw new ArgumentOutOfRangeException(nameof(initialBytes));
        if (maximumBytes > 0 && initialBytes > maximumBytes)
            throw new InvalidDataException($"Existing content exceeds the expected size of {maximumBytes:N0} bytes.");

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var total = initialBytes;
        try
        {
            while (true)
            {
                var read = await ReadWithIdleTimeoutAsync(
                    source,
                    buffer.AsMemory(0, BufferSize),
                    readIdleTimeout,
                    cancellationToken);
                if (read == 0) return total;
                if (maximumBytes > 0 && read > maximumBytes - total)
                    throw new InvalidDataException($"Download exceeded the expected size of {maximumBytes:N0} bytes.");

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                if (progress is not null)
                    await progress(total, cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static async ValueTask<int> ReadWithIdleTimeoutAsync(
        Stream source,
        Memory<byte> buffer,
        TimeSpan? idleTimeout,
        CancellationToken cancellationToken)
    {
        var read = source.ReadAsync(buffer, cancellationToken);
        if (idleTimeout is not { } timeout) return await read;
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        try
        {
            return await read.AsTask().WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new IOException($"The download produced no data for {timeout.TotalSeconds:N0} seconds.", ex);
        }
    }
}
