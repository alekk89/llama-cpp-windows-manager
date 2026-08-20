namespace LocalLlmConsole.Services;

public sealed partial class RuntimeDeletionPlanner
{
    public RuntimeSourceDeletionPlan PlanRuntimeSourceDeletion(RuntimeSourceEntry source, string runtimeRoot)
    {
        if (!RuntimeFileService.IsSafeRuntimeFolder(runtimeRoot, source.SourceDir))
        {
            return new RuntimeSourceDeletionPlan(
                RuntimeSourceDeletionPlanKind.Blocked,
                "Only downloaded sources inside the configured runtimes folder can be deleted from here.",
                source,
                source.SourceDir);
        }

        return new RuntimeSourceDeletionPlan(
            RuntimeSourceDeletionPlanKind.DeleteSourceFolder,
            "",
            source,
            source.SourceDir);
    }

    public Task<RuntimeSourceDeletionPlan> PlanRuntimeSourceDeletionAsync(
        RuntimeSourceEntry source,
        string runtimeRoot,
        CancellationToken cancellationToken = default)
        => Task.Run(() => PlanRuntimeSourceDeletion(source, runtimeRoot), cancellationToken);
}
