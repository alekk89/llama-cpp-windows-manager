namespace LocalLlmConsole.Services;

public sealed class GpuSummaryCache
{
    private const int MaximumEntries = 16;
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(10);
    private readonly object _gate = new();
    private readonly Dictionary<string, (HostHardwareSnapshot Snapshot, DateTimeOffset CapturedAt)> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<HostHardwareSnapshot>> _inFlight = new(StringComparer.Ordinal);

    public bool TryGet(DateTimeOffset now, out string summary)
        => TryGet("", now, out summary);

    public bool TryGet(string key, DateTimeOffset now, out string summary)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key ?? "", out var entry)
                && entry.CapturedAt != DateTimeOffset.MinValue
                && now - entry.CapturedAt < Freshness)
            {
                summary = entry.Snapshot.Summary;
                return true;
            }
        }

        summary = "Unavailable";
        return false;
    }

    public string Store(string summary, DateTimeOffset capturedAt)
        => Store("", summary, capturedAt);

    public string Store(string key, string summary, DateTimeOffset capturedAt)
        => StoreSnapshot(key, HostHardwareSnapshotParser.Parse(Normalize(summary), capturedAt), capturedAt).Summary;

    public bool TryGetSnapshot(string key, DateTimeOffset now, out HostHardwareSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key ?? "", out var entry)
                && entry.CapturedAt != DateTimeOffset.MinValue
                && now - entry.CapturedAt < Freshness)
            {
                snapshot = entry.Snapshot;
                return true;
            }
        }

        snapshot = HostHardwareSnapshot.Unavailable(now);
        return false;
    }

    public HostHardwareSnapshot StoreSnapshot(
        string key,
        HostHardwareSnapshot snapshot,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            StoreEntry(key ?? "", snapshot, capturedAt);
            return snapshot;
        }
    }

    public Task<string> GetOrCreateAsync(
        string key,
        DateTimeOffset now,
        Func<Task<string>> factory,
        CancellationToken cancellationToken = default)
        => GetStringAsync(key, now, factory, cancellationToken);

    public Task<HostHardwareSnapshot> GetOrCreateSnapshotAsync(
        string key,
        DateTimeOffset now,
        Func<Task<HostHardwareSnapshot>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        key ??= "";
        Task<HostHardwareSnapshot> shared;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry)
                && entry.CapturedAt != DateTimeOffset.MinValue
                && now - entry.CapturedAt < Freshness)
                return Task.FromResult(entry.Snapshot);
            if (!_inFlight.TryGetValue(key, out shared!))
            {
                shared = CompleteFactoryAsync(key, now, factory);
                _inFlight[key] = shared;
            }
        }
        return cancellationToken.CanBeCanceled ? shared.WaitAsync(cancellationToken) : shared;
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    private async Task<string> GetStringAsync(
        string key,
        DateTimeOffset now,
        Func<Task<string>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var snapshot = await GetOrCreateSnapshotAsync(
            key,
            now,
            async () => HostHardwareSnapshotParser.Parse(Normalize(await factory()), now),
            cancellationToken);
        return snapshot.Summary;
    }

    private async Task<HostHardwareSnapshot> CompleteFactoryAsync(
        string key,
        DateTimeOffset capturedAt,
        Func<Task<HostHardwareSnapshot>> factory)
    {
        var startedAt = Stopwatch.GetTimestamp();
        await Task.Yield();
        try
        {
            var snapshot = await factory();
            var completedAt = capturedAt.Add(Stopwatch.GetElapsedTime(startedAt));
            lock (_gate) StoreEntry(key, snapshot, completedAt);
            return snapshot;
        }
        finally
        {
            lock (_gate) _inFlight.Remove(key);
        }
    }

    private static string Normalize(string summary)
        => string.IsNullOrWhiteSpace(summary)
            ? "Unavailable"
            : GpuStatusService.NormalizeMetricSeparators(summary);

    private void StoreEntry(string key, HostHardwareSnapshot snapshot, DateTimeOffset capturedAt)
    {
        _entries[key] = (snapshot, capturedAt);
        while (_entries.Count > MaximumEntries)
        {
            var oldest = _entries
                .Where(entry => !string.Equals(entry.Key, key, StringComparison.Ordinal))
                .OrderBy(entry => entry.Value.CapturedAt)
                .Select(entry => entry.Key)
                .FirstOrDefault();
            if (oldest is null) break;
            _entries.Remove(oldest);
        }
    }
}
