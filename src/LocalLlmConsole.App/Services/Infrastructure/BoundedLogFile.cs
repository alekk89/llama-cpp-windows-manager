using System.Collections.Concurrent;

namespace LocalLlmConsole.Services;

public sealed class BoundedLogWriter : IDisposable
{
    private const int FlushThresholdBytes = 64 * 1024;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private readonly FileStream _stream;
    private readonly long _maxBytes;
    private readonly object _gate = new();
    private readonly System.Threading.Timer _flushTimer;
    private int _pendingBytes;
    private bool _disposed;

    public BoundedLogWriter(string path, long maxBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete);
        _maxBytes = Math.Max(0, maxBytes);
        _flushTimer = new System.Threading.Timer(
            _ => FlushFromTimer(),
            null,
            FlushInterval,
            FlushInterval);
    }

    public void WriteLine(string line)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var text = line + Environment.NewLine;
            BoundedLogFile.WriteToStream(_stream, text, _maxBytes, flush: false);
            _pendingBytes += Encoding.UTF8.GetByteCount(text);
            if (_pendingBytes >= FlushThresholdBytes)
                FlushCore();
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            FlushCore();
        }
    }

    public void Dispose()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _flushTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                try
                {
                    FlushCore();
                }
                finally
                {
                    _stream.Dispose();
                }
            }
        }
        finally
        {
            _flushTimer.Dispose();
        }
    }

    private void FlushFromTimer()
    {
        lock (_gate)
        {
            if (_disposed || _pendingBytes == 0) return;
            try
            {
                FlushCore();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Could not flush runtime log buffer: {ex.Message}");
            }
        }
    }

    private void FlushCore()
    {
        if (_pendingBytes == 0) return;
        _stream.Flush();
        _pendingBytes = 0;
    }
}

public static class BoundedLogFile
{
    private const string ResetMarker = "[log limit reached; overwriting from beginning]";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static long MegabytesToBytes(int megabytes)
        => megabytes <= 0 ? 0 : megabytes * 1024L * 1024L;

    public static async Task AppendAsync(string path, string text, long maxBytes)
    {
        await Task.Run(() => Append(path, text, maxBytes));
    }

    public static void Append(string path, string text, long maxBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var gate = Locks.GetOrAdd(Path.GetFullPath(path), _ => new object());
        lock (gate)
        {
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            WriteToStream(stream, text, Math.Max(0, maxBytes));
        }
    }

    internal static void WriteToStream(FileStream stream, string text, long maxBytes, bool flush = true)
    {
        if (maxBytes <= 0)
        {
            stream.Seek(0, SeekOrigin.End);
            WriteUtf8(stream, text, flush);
            return;
        }

        var bytes = Utf8.GetBytes(text);
        if (bytes.LongLength > maxBytes)
        {
            stream.SetLength(0);
            stream.Position = 0;
            WriteBytes(stream, TailBytes(text, maxBytes), flush);
            return;
        }

        if (stream.Length + bytes.LongLength > maxBytes)
        {
            var resetText = ResetMarker + Environment.NewLine + text;
            var resetBytes = Utf8.GetByteCount(resetText) <= maxBytes
                ? Utf8.GetBytes(resetText)
                : TailBytes(resetText, maxBytes);
            stream.SetLength(0);
            stream.Position = 0;
            WriteBytes(stream, resetBytes, flush);
            return;
        }

        stream.Seek(0, SeekOrigin.End);
        WriteBytes(stream, bytes, flush);
    }

    private static byte[] TailBytes(string text, long maxBytes)
    {
        var capped = (int)Math.Min(maxBytes, int.MaxValue);
        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (Utf8.GetByteCount(text.AsSpan(mid)) > capped)
                low = mid + 1;
            else
                high = mid;
        }
        return Utf8.GetBytes(text[low..]);
    }

    private static void WriteUtf8(FileStream stream, string text, bool flush)
        => WriteBytes(stream, Utf8.GetBytes(text), flush);

    private static void WriteBytes(FileStream stream, byte[] bytes, bool flush)
    {
        stream.Write(bytes, 0, bytes.Length);
        if (flush)
            stream.Flush();
    }
}
