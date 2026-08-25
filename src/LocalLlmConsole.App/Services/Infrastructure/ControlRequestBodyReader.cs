namespace LocalLlmConsole.Services;

public static class ControlRequestBodyReader
{
    public static async Task<JsonObject?> ReadJsonObjectAsync(
        Stream input,
        Encoding encoding,
        long contentLength,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(encoding);
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (contentLength > maximumBytes)
            throw BodyTooLarge();

        var initialCapacity = contentLength is > 0 and <= int.MaxValue
            ? (int)Math.Min(contentLength, maximumBytes)
            : 0;
        using var body = initialCapacity > 0 ? new MemoryStream(initialCapacity) : new MemoryStream();
        var buffer = new byte[8192];
        var totalBytes = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0) break;
            totalBytes = checked(totalBytes + count);
            if (totalBytes > maximumBytes)
                throw BodyTooLarge();
            await body.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        body.Position = 0;
        using var reader = new StreamReader(
            body,
            encoding,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return JsonNode.Parse(text) as JsonObject
               ?? throw new InvalidOperationException("Control API request body must be a JSON object.");
    }

    private static InvalidOperationException BodyTooLarge()
        => new("Control API request bodies are limited to 1 MiB.");
}
