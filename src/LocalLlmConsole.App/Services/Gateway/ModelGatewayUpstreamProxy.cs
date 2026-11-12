using System.Net.Http.Headers;

namespace LocalLlmConsole.Services;

public sealed class ModelGatewayUpstreamProxy : IDisposable
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "Host",
        "Content-Length"
    };

    private static readonly HashSet<string> AllowedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept",
        "Accept-Encoding",
        "User-Agent",
        "Cache-Control",
    };

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly GatewayPerformanceTracker? _performance;
    private readonly TimeSpan _responseHeaderTimeout;

    public ModelGatewayUpstreamProxy(
        HttpClient? client = null,
        GatewayPerformanceTracker? performance = null,
        TimeSpan? responseHeaderTimeout = null)
    {
        _ownsClient = client is null;
        _client = client ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _performance = performance;
        _responseHeaderTimeout = responseHeaderTimeout ?? TimeSpan.FromMinutes(15);
    }

    public async Task ForwardAsync(
        HttpListenerContext context,
        LoadedModelSessionSnapshot session,
        byte[] body,
        CancellationToken cancellationToken,
        TimeSpan? elapsedBeforeUpstream = null)
    {
        var upstream = new Uri($"{RuntimeEndpointService.LocalServerBaseUrl(session.LaunchSettings)}{context.Request.Url?.PathAndQuery ?? "/"}");
        using var request = BuildUpstreamRequest(context.Request, upstream, body, session.LaunchSettings);
        var started = Stopwatch.StartNew();
        var priorElapsed = elapsedBeforeUpstream ?? TimeSpan.Zero;
        // Cancelled when a client write fails so the upstream generation is
        // abandoned instead of running to completion for a closed connection.
        using var clientCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            using var headerCancellation = CancellationTokenSource.CreateLinkedTokenSource(clientCancellation.Token);
            headerCancellation.CancelAfter(_responseHeaderTimeout);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerCancellation.Token);
            var observation = await CopyResponseAsync(context, response, started, clientCancellation);
            _performance?.Observe(response.IsSuccessStatusCode, priorElapsed + started.Elapsed,
                observation.TimeToFirstData is { } first ? priorElapsed + first : null,
                observation.ResponseTokensPerSecond);
        }
        catch
        {
            _performance?.Observe(false, priorElapsed + started.Elapsed, null, null);
            throw;
        }
    }

    private static HttpRequestMessage BuildUpstreamRequest(
        HttpListenerRequest source,
        Uri upstream,
        byte[] body,
        AppSettings launchSettings)
    {
        var request = new HttpRequestMessage(new HttpMethod(source.HttpMethod), upstream);
        foreach (var key in source.Headers.AllKeys)
        {
            if (string.IsNullOrWhiteSpace(key) || !AllowedRequestHeaders.Contains(key)) continue;
            var values = source.Headers.GetValues(key);
            if (values is not null)
                request.Headers.TryAddWithoutValidation(key, values);
        }

        var apiKey = RuntimeEndpointService.ModelApiKeyForClient(launchSettings);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new ByteArrayContent(body);
        if (!string.IsNullOrWhiteSpace(source.ContentType))
            request.Content.Headers.TryAddWithoutValidation("Content-Type", source.ContentType);
        return request;
    }

    private static async Task<GatewayResponseObservation> CopyResponseAsync(
        HttpListenerContext context,
        HttpResponseMessage response,
        Stopwatch started,
        CancellationTokenSource clientCancellation)
    {
        var cancellationToken = clientCancellation.Token;
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            if (!IsCopiedResponseHeader(header.Key)) continue;
            TrySetResponseHeader(context.Response, header.Key, string.Join(",", header.Value));
        }

        foreach (var header in response.Content.Headers)
        {
            if (!IsCopiedResponseHeader(header.Key)) continue;
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            TrySetResponseHeader(context.Response, header.Key, string.Join(",", header.Value));
        }

        context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        if (response.Content.Headers.ContentLength is { } contentLength)
        {
            context.Response.ContentLength64 = contentLength;
        }
        else
        {
            context.Response.SendChunked = true;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var observedOutput = new GatewayObservedResponseStream(context.Response.OutputStream, started, clientCancellation);
        await stream.CopyToAsync(observedOutput, cancellationToken);
        try
        {
            context.Response.Close();
        }
        catch (Exception ex) when (ModelGatewayClientDisconnectedException.IsClientStreamFailure(ex))
        {
            throw new ModelGatewayClientDisconnectedException(ex);
        }
        var completionTokens = observedOutput.Complete();
        var throughput = GatewayResponseThroughputPolicy.Calculate(
            completionTokens,
            observedOutput.TimeToFirstData,
            started.Elapsed,
            response.Content.Headers.ContentType?.MediaType);
        return new(observedOutput.TimeToFirstData, throughput);
    }

    // Client write failures cancel the upstream request and surface as a client
    // disconnect, distinct from upstream read failures during the same copy.
    private sealed class GatewayObservedResponseStream(
        Stream inner,
        Stopwatch started,
        CancellationTokenSource clientCancellation) : Stream
    {
        private readonly GatewayCompletionTokenObserver _tokens = new();

        public TimeSpan? TimeToFirstData { get; private set; }

        public double? Complete() => _tokens.Complete();

        public override void Write(byte[] buffer, int offset, int count)
            => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Observe(buffer);
            try
            {
                inner.Write(buffer);
            }
            catch (Exception ex) when (ModelGatewayClientDisconnectedException.IsClientStreamFailure(ex))
            {
                throw Disconnected(ex);
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Observe(buffer.Span);
            try
            {
                await inner.WriteAsync(buffer, cancellationToken);
            }
            catch (Exception ex) when (ModelGatewayClientDisconnectedException.IsClientStreamFailure(ex))
            {
                throw Disconnected(ex);
            }
        }

        private void Observe(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length == 0) return;
            TimeToFirstData ??= started.Elapsed;
            _tokens.Observe(buffer);
        }

        private ModelGatewayClientDisconnectedException Disconnected(Exception ex)
        {
            try
            {
                clientCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The request already completed; there is no upstream work left to cancel.
            }
            return new ModelGatewayClientDisconnectedException(ex);
        }

        public override void Flush()
        {
            try
            {
                inner.Flush();
            }
            catch (Exception ex) when (ModelGatewayClientDisconnectedException.IsClientStreamFailure(ex))
            {
                throw Disconnected(ex);
            }
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            try
            {
                await inner.FlushAsync(cancellationToken);
            }
            catch (Exception ex) when (ModelGatewayClientDisconnectedException.IsClientStreamFailure(ex))
            {
                throw Disconnected(ex);
            }
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    internal sealed class GatewayCompletionTokenObserver
    {
        private const string CompletionTokens = "\"completion_tokens\"";
        private const string PredictedTokens = "\"predicted_n\"";
        private ParseState _state;
        private int _completionMatch;
        private int _predictedMatch;
        private double _value;
        private double _fractionScale = 1;
        private bool _decimalPoint;
        private double? _latest;

        public void Observe(ReadOnlySpan<byte> bytes)
        {
            var offset = 0;
            while (offset < bytes.Length)
            {
                if (_state != ParseState.SeekingKey || _completionMatch > 0 || _predictedMatch > 0)
                {
                    Process(bytes[offset++]);
                    continue;
                }

                var quoteOffset = bytes[offset..].IndexOf((byte)'"');
                if (quoteOffset < 0)
                {
                    _completionMatch = 0;
                    _predictedMatch = 0;
                    return;
                }

                offset += quoteOffset;
                var remaining = bytes[offset..];
                var matchedLength = MatchingKeyLength(remaining);
                if (matchedLength > 0)
                {
                    _state = ParseState.AwaitingColon;
                    _completionMatch = 0;
                    _predictedMatch = 0;
                    offset += matchedLength;
                    continue;
                }

                // Only the end of a transport chunk needs byte-wise matching so a
                // JSON key split across chunks is retained. The common path uses
                // the runtime's vectorized span search to skip response payloads.
                if (remaining.Length < CompletionTokens.Length)
                {
                    while (offset < bytes.Length)
                        Process(bytes[offset++]);
                    return;
                }

                offset++;
            }
        }

        public double? Complete()
        {
            if (_state == ParseState.ReadingValue)
            {
                CompleteValue();
                ResetSearch();
            }
            return _latest;
        }

        private void Process(byte value)
        {
            var reprocess = true;
            while (reprocess)
            {
                reprocess = false;
                switch (_state)
                {
                    case ParseState.SeekingKey:
                        if (AdvanceKeys(value)) _state = ParseState.AwaitingColon;
                        break;
                    case ParseState.AwaitingColon:
                        if (IsWhitespace(value)) break;
                        if (value == ':')
                        {
                            _state = ParseState.AwaitingValue;
                            break;
                        }
                        ResetSearch();
                        reprocess = true;
                        break;
                    case ParseState.AwaitingValue:
                        if (IsWhitespace(value)) break;
                        if (IsDigit(value))
                        {
                            _state = ParseState.ReadingValue;
                            _value = value - '0';
                            _fractionScale = 1;
                            _decimalPoint = false;
                            break;
                        }
                        ResetSearch();
                        reprocess = true;
                        break;
                    case ParseState.ReadingValue:
                        if (IsDigit(value))
                        {
                            if (_decimalPoint)
                            {
                                _fractionScale *= 10;
                                _value += (value - '0') / _fractionScale;
                            }
                            else
                            {
                                _value = _value * 10 + value - '0';
                            }
                            break;
                        }
                        if (value == '.' && !_decimalPoint)
                        {
                            _decimalPoint = true;
                            break;
                        }
                        CompleteValue();
                        ResetSearch();
                        reprocess = true;
                        break;
                }
            }
        }

        private bool AdvanceKeys(byte value)
        {
            _completionMatch = Advance(CompletionTokens, _completionMatch, value);
            _predictedMatch = Advance(PredictedTokens, _predictedMatch, value);
            if (_completionMatch != CompletionTokens.Length && _predictedMatch != PredictedTokens.Length)
                return false;
            _completionMatch = 0;
            _predictedMatch = 0;
            return true;
        }

        private static int MatchingKeyLength(ReadOnlySpan<byte> bytes)
        {
            if (AsciiStartsWith(bytes, CompletionTokens)) return CompletionTokens.Length;
            if (AsciiStartsWith(bytes, PredictedTokens)) return PredictedTokens.Length;
            return 0;
        }

        private static bool AsciiStartsWith(ReadOnlySpan<byte> bytes, string pattern)
        {
            if (bytes.Length < pattern.Length) return false;
            for (var index = 0; index < pattern.Length; index++)
            {
                if (!AsciiEquals(pattern[index], bytes[index])) return false;
            }
            return true;
        }

        private static int Advance(string pattern, int matched, byte value)
        {
            if (AsciiEquals(pattern[matched], value)) return matched + 1;
            return AsciiEquals(pattern[0], value) ? 1 : 0;
        }

        private void CompleteValue()
        {
            _latest = _value;
            _value = 0;
            _fractionScale = 1;
            _decimalPoint = false;
        }

        private void ResetSearch()
        {
            _state = ParseState.SeekingKey;
            _completionMatch = 0;
            _predictedMatch = 0;
        }

        private static bool AsciiEquals(char expected, byte actual)
            => expected == actual
               || expected is >= 'a' and <= 'z' && expected - 32 == actual;

        private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';
        private static bool IsWhitespace(byte value) => value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

        private enum ParseState
        {
            SeekingKey,
            AwaitingColon,
            AwaitingValue,
            ReadingValue
        }
    }

    private readonly record struct GatewayResponseObservation(
        TimeSpan? TimeToFirstData,
        double? ResponseTokensPerSecond);

    // CORS, Vary, and caching headers were already set by the gateway access
    // policy; upstream values must not replace them.
    private static bool IsCopiedResponseHeader(string name)
        => !HopByHopHeaders.Contains(name)
           && !ModelGatewayRequestAccessPolicy.IsGatewayOwnedResponseHeader(name);

    private static void TrySetResponseHeader(HttpListenerResponse response, string name, string value)
    {
        try
        {
            response.Headers[name] = value;
        }
        catch
        {
            // Some framework-controlled headers, such as Date or Server, cannot be
            // copied through HttpListener. The body/status still carry the response.
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
