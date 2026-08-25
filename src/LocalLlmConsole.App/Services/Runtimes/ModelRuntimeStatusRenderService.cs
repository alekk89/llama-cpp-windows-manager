namespace LocalLlmConsole.Services;

public sealed record ModelRuntimeStatusRenderPlan(
    bool ShouldRender,
    string MetricText,
    string StatusText)
{
    public static ModelRuntimeStatusRenderPlan None { get; } = new(false, "", "");
}

public sealed class ModelRuntimeStatusRenderService
{
    public ModelRuntimeStatusRenderPlan LoadingTick(ModelRuntimeStatusDisplay? status)
        => status is null
            ? ModelRuntimeStatusRenderPlan.None
            : Render(status, includeStatusText: true);

    public ModelRuntimeStatusRenderPlan DashboardRefresh(
        ModelRuntimeStatusDisplay status,
        bool hasLoadedStatusTimer)
    {
        ArgumentNullException.ThrowIfNull(status);

        return status.Kind switch
        {
            ModelRuntimeStatusKind.Loading => Render(status, includeStatusText: true),
            ModelRuntimeStatusKind.Loaded when hasLoadedStatusTimer => Render(status, includeStatusText: false),
            _ => Render(status, includeStatusText: false)
        };
    }

    public ModelRuntimeStatusRenderPlan LoadedStatus(ModelRuntimeStatusDisplay? status)
        => status is null
            ? ModelRuntimeStatusRenderPlan.None
            : Render(status, includeStatusText: false);

    private static ModelRuntimeStatusRenderPlan Render(
        ModelRuntimeStatusDisplay status,
        bool includeStatusText)
        => new(
            ShouldRender: true,
            status.MetricText,
            includeStatusText ? status.StatusText ?? "" : "");
}
