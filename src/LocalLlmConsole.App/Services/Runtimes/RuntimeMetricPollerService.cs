namespace LocalLlmConsole.Services;

public sealed class RuntimeMetricPollerService
{
    private readonly HttpClient _http;

    public RuntimeMetricPollerService(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public Task<RuntimeMetricPollResult[]> PollSessionsAsync(
        IReadOnlyList<LoadedModelSessionSnapshot> sessions,
        CancellationToken cancellationToken = default)
        => sessions.Count == 0
            ? Task.FromResult(Array.Empty<RuntimeMetricPollResult>())
            : Task.WhenAll(sessions.Select(session => PollSessionAsync(session, cancellationToken)));

    public static string RuntimeKey(LoadedModelSessionSnapshot session)
        => RuntimeMetricIdentity.RuntimeKey(session);

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
