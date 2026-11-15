namespace LocalLlmConsole;

public sealed partial class OverviewDashboardController
{
    private IReadOnlyList<LoadedSessionDashboardRow> _loadedSessions = [];

    public IReadOnlyList<LoadedSessionDashboardRow> LoadedSessions => _loadedSessions;

    /// <summary>
    /// Applies the per-session rows projected from the existing telemetry poll.
    /// Cards without the loaded-models list ignore the update.
    /// </summary>
    public void ApplyLoadedSessions(IReadOnlyList<LoadedSessionDashboardRow> sessions)
    {
        _loadedSessions = sessions?.ToArray() ?? [];
        var geometryChanged = false;
        foreach (var view in _cardViews.Values)
            geometryChanged |= view.ApplyLoadedSessions(_loadedSessions);
        if (!geometryChanged) return;
        _placementDirty = true;
        RequestApplyReadings(pushGraphs: false);
    }
}
