namespace LocalLlmConsole.Services;

public sealed class RuntimeMetricPollerService
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _pollGate;

    public RuntimeMetricPollerService(HttpClient http, int maxConcurrentSessions = 4)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (maxConcurrentSessions <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentSessions));
        _pollGate = new SemaphoreSlim(maxConcurrentSessions, maxConcurrentSessions);
    }

    public async Task<RuntimeMetricPollResult[]> PollSessionsAsync(
        IReadOnlyList<LoadedModelSessionSnapshot> sessions,
        CancellationToken cancellationToken = default)
    {
        if (sessions.Count == 0) return [];
        return await Task.WhenAll(sessions.Select(session => PollSessionBoundedAsync(session, cancellationToken)));
    }

    public static string RuntimeKey(LoadedModelSessionSnapshot session)
        => RuntimeMetricIdentity.RuntimeKey(session);

    public async Task<RuntimeMetricPollResult[]> PollSessionsAsync(
        IReadOnlyList<LoadedModelSessionSnapshot> sessions,
        Func<RuntimeMetricPollResult, Task> onCompleted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onCompleted);
        using var polling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = sessions.Select(session => PollSessionBoundedAsync(session, polling.Token)).ToArray();
        var pending = tasks.ToList();
        try
        {
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                var result = await completed;
                cancellationToken.ThrowIfCancellationRequested();
                // Await callbacks serially on the caller's context. HTTP requests
                // remain concurrent, but UI and counter consumers never overlap.
                await onCompleted(result);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.WhenAll(tasks);
        }
        catch
        {
            polling.Cancel();
            // Do not release the refresh guard while abandoned polls still run.
            try { await Task.WhenAll(tasks); }
            catch { /* Preserve the original polling or callback failure. */ }
            throw;
        }
    }

    private async Task<RuntimeMetricPollResult> PollSessionBoundedAsync(
        LoadedModelSessionSnapshot session,
        CancellationToken cancellationToken)
    {
        await _pollGate.WaitAsync(cancellationToken);
        try
        {
            return await PollSessionAsync(session, cancellationToken);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private async Task<RuntimeMetricPollResult> PollSessionAsync(
        LoadedModelSessionSnapshot session,
        CancellationToken cancellationToken)
    {
        var runtimeKey = RuntimeKey(session);
        var settings = session.LaunchSettings;
        var slotTask = SlotSnapshotAsync(settings, cancellationToken);
        if (!settings.EnableMetrics)
        {
            var slot = await slotTask;
            return new RuntimeMetricPollResult(session, runtimeKey, [], slot.Snapshot, slot.Error, slot.Responded);
        }

        try
        {
            var raw = await RuntimeEndpointService.RuntimeGetStringAsync(
                _http,
                $"{RuntimeEndpointService.LocalServerBaseUrl(settings)}/metrics",
                settings,
                cancellationToken);
            var slot = await slotTask;
            return new RuntimeMetricPollResult(
                session,
                runtimeKey,
                RuntimeMetrics.ParsePrometheus(raw),
                slot.Snapshot,
                "",
                EndpointResponded: true);
        }
        catch (Exception ex)
        {
            var slot = await slotTask;
            return new RuntimeMetricPollResult(
                session,
                runtimeKey,
                [],
                slot.Snapshot,
                ex.Message,
                slot.Responded);
        }
    }

    private async Task<(RuntimeSlotSnapshot? Snapshot, bool Responded, string Error)> SlotSnapshotAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = await RuntimeEndpointService.RuntimeGetStringAsync(
                _http,
                $"{RuntimeEndpointService.LocalServerBaseUrl(settings)}/slots",
                settings,
                cancellationToken);
            return (RuntimeDashboardService.ParseSlotSnapshot(raw), true, "");
        }
        catch (Exception ex)
        {
            return (null, false, ex.Message);
        }
    }
}
