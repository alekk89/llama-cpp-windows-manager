namespace LocalLlmConsole;

public sealed partial class OverviewDashboardCardView
{
    public OverviewDashboardLoadedModelsView? LoadedModels { get; private set; }

    public bool ApplyLoadedSessions(IReadOnlyList<LoadedSessionDashboardRow> sessions)
    {
        if (LoadedModels is null || !LoadedModels.Apply(sessions)) return false;
        InvalidateMinimumSize();
        return true;
    }

    private bool TryAddLoadedModels(OverviewDashboardMetricDefinition definition, Action<string>? selectSession)
    {
        if (!string.Equals(definition.Id, OverviewDashboardMetricIds.LoadedModels, StringComparison.Ordinal))
            return false;
        LoadedModels = new OverviewDashboardLoadedModelsView(definition, selectSession);
        _values.Children.Add(LoadedModels.Root);
        return true;
    }
}
