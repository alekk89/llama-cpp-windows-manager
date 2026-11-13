using System.Net.Http.Headers;

namespace LocalLlmConsole.Services;

// Reads small text fields from an already-buffered multipart/form-data body.
// The body itself is bounded by the gateway request limit; this reader never
// copies file parts and caps the decoded field length.
public static class ModelGatewayMultipartFormReader
{
    public const int MaxFieldBytes = 4096;
    private const int MaxBoundaryLength = 70;
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    public static bool TryGetBoundary(string? contentType, out string boundary)
    {
        boundary = "";
        if (string.IsNullOrWhiteSpace(contentType)
            || !MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
            || !string.Equals(mediaType.MediaType, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return false;

        var value = mediaType.Parameters
            .FirstOrDefault(parameter => string.Equals(parameter.Name, "boundary", StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim('"') ?? "";
        if (value.Length is 0 or > MaxBoundaryLength || value.Any(character => character > 0x7e || character < 0x20))
            return false;
        boundary = value;
        return true;
    }

    public static bool IsMultipartFormData(string? contentType)
        => TryGetBoundary(contentType, out _);

    public static string ReadTextField(ReadOnlySpan<byte> body, string boundary, string fieldName)
    {
        ArgumentException.ThrowIfNullOrEmpty(boundary);
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        var partDelimiter = Encoding.ASCII.GetBytes("\r\n--" + boundary);

        var first = body.IndexOf(delimiter);
        if (first < 0) return "";
        var position = first + delimiter.Length;
        while (position <= body.Length)
        {
            var rest = body[position..];
            if (rest.StartsWith("--"u8)) return "";
            var lineEnd = rest.IndexOf("\r\n"u8);
            if (lineEnd < 0) return "";
            var headersStart = position + lineEnd + 2;
            var headerLength = body[headersStart..].IndexOf(HeaderTerminator);
            if (headerLength < 0) return "";
            var contentStart = headersStart + headerLength + HeaderTerminator.Length;
            var contentLength = body[contentStart..].IndexOf(partDelimiter);
            if (contentLength < 0) return "";

            if (IsNamedTextField(body.Slice(headersStart, headerLength), fieldName))
            {
                return contentLength > MaxFieldBytes
                    ? ""
                    : Encoding.UTF8.GetString(body.Slice(contentStart, contentLength)).Trim();
            }

            position = contentStart + contentLength + partDelimiter.Length;
        }

        return "";
    }

    private static bool IsNamedTextField(ReadOnlySpan<byte> headerBytes, string fieldName)
    {
        foreach (var line in Encoding.UTF8.GetString(headerBytes).Split("\r\n"))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0
                || !line[..separator].Trim().Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase)
                || !ContentDispositionHeaderValue.TryParse(line[(separator + 1)..].Trim(), out var disposition))
                continue;

            return string.Equals(disposition.DispositionType, "form-data", StringComparison.OrdinalIgnoreCase)
                && disposition.FileName is null
                && disposition.FileNameStar is null
                && string.Equals(disposition.Name?.Trim('"'), fieldName, StringComparison.Ordinal);
        }

        return false;
    }
}
