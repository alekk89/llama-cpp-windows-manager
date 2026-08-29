namespace LocalLlmConsole.Services;

public sealed record RuntimeOverviewStatusRequest(
    ModelRecord? SelectedModel,
    LoadedModelSessionSnapshot? Session,
    LlamaRuntimeState ActiveRuntimeState,
    int? LastExitCode);

public sealed record RuntimeOverviewStatusLabels(
    string Model,
    string Runtime);

public sealed class RuntimeOverviewStatusService
{
    public RuntimeOverviewStatusLabels Labels(RuntimeOverviewStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SelectedModel is not null && request.Session is null)
            return new RuntimeOverviewStatusLabels($"Stopped: {request.SelectedModel.Name}", "No loaded runtime");
        if (request.Session is null)
            return new RuntimeOverviewStatusLabels("None", "Stopped");

        var status = SessionStatusLabel(request.Session.Status);
        if (request.ActiveRuntimeState == LlamaRuntimeState.Failed && request.LastExitCode is int exitCode)
            status = $"Failed ({exitCode})";

        var runtime = string.IsNullOrWhiteSpace(request.Session.RuntimeName)
            ? "Unknown runtime"
            : request.Session.RuntimeName;

        return new RuntimeOverviewStatusLabels($"{status}: {request.Session.ModelName}", runtime);
    }

    private static string SessionStatusLabel(LoadedModelSessionStatus status)
        => status switch
        {
            LoadedModelSessionStatus.Running => "Loaded",
            LoadedModelSessionStatus.Warm => "Loaded",
            LoadedModelSessionStatus.Loading => "Loading",
            LoadedModelSessionStatus.Failed => "Failed",
            _ => "Stopped"
        };
}
