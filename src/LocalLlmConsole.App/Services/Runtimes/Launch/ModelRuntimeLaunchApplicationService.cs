namespace LocalLlmConsole.Services;

public sealed record ModelRuntimeLaunchApplicationRequest(
    RuntimeRecord Runtime,
    ModelRecord Model,
    AppSettings LaunchSettings,
    bool InteractivePrompts,
    bool AutoLoadGatewayEnabled,
    int AutoLoadGatewayPort,
    string LaunchProfileId = "",
    string LaunchProfileName = "");

public sealed record ModelRuntimeLaunchApplicationActions(
    ModelRuntimeApiKeyEnsurer EnsureApiKeyAsync,
    RuntimeEndpointRespondingProbe EndpointRespondingAsync,
    RuntimeLaunchAdmissionConfirmation ConfirmAdmissionAsync,
    RuntimeLaunchMemoryReader ReadMemoryAsync,
    Action<ModelRecord, AppSettings> StartLoadingStatus,
    Action StopLoadingStatus,
    Action<AppSettings> SetActiveRuntimeSettings,
    Func<Task> SaveActiveRuntimeSessionsAsync,
    Action<ModelRecord, AppSettings> StartReadinessMonitor,
    Action StartRuntimeDashboardRefresh,
    Action UpdateLoadingStatus,
    Func<Task> RefreshOverviewAsync,
    Func<Task> RefreshOverviewModelSelectorAsync,
    Func<TimeSpan, Task> DelayAsync,
    Func<Task> RefreshRuntimeMetricsAsync,
    Func<LlamaRuntimeState> RuntimeState,
    Func<bool> IsOverviewPage,
    Action StopRuntimeDashboardRefresh,
    Action UpdateActionButtons,
    Action<string> SetStatus);

public sealed record ModelRuntimeLaunchApplicationResult(
    bool Launched,
    LoadedModelSessionSnapshot? Session,
    AppSettings LaunchSettings);

public sealed class ModelRuntimeLaunchApplicationService
{
    private readonly ModelRuntimeLaunchPreparationService _preparation;
    private readonly RuntimeSessionCommandService _commands;
    private readonly ModelRuntimeStartFollowupService _followup;
    private readonly ModelRuntimeStartFollowupApplicationService _followupApplication;

    public ModelRuntimeLaunchApplicationService(
        ModelRuntimeLaunchPreparationService preparation,
        RuntimeSessionCommandService commands,
        ModelRuntimeStartFollowupService followup,
        ModelRuntimeStartFollowupApplicationService followupApplication)
    {
        _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _followup = followup ?? throw new ArgumentNullException(nameof(followup));
        _followupApplication = followupApplication ?? throw new ArgumentNullException(nameof(followupApplication));
    }

    public async Task<ModelRuntimeLaunchApplicationResult> LaunchAsync(
        ModelRuntimeLaunchApplicationRequest request,
        ModelRuntimeLaunchApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);

        try
        {
            var preparation = await _preparation.PrepareAsync(new ModelRuntimeLaunchPreparationRequest(
                request.Runtime,
                request.Model,
                request.LaunchSettings,
                request.InteractivePrompts,
                request.AutoLoadGatewayEnabled,
                request.AutoLoadGatewayPort,
                actions.EnsureApiKeyAsync,
                actions.EndpointRespondingAsync,
                actions.ConfirmAdmissionAsync,
                actions.ReadMemoryAsync), cancellationToken);
            if (!preparation.CanLaunch)
                return new ModelRuntimeLaunchApplicationResult(false, null, preparation.LaunchSettings);

            var launchSettings = preparation.LaunchSettings;
            if (!string.IsNullOrWhiteSpace(preparation.StatusMessage))
                actions.SetStatus(preparation.StatusMessage);

            actions.StartLoadingStatus(request.Model, launchSettings);
            var snapshot = await _commands.StartModelAsync(
                request.Runtime,
                request.Model,
                launchSettings,
                request.LaunchProfileId,
                request.LaunchProfileName);
            actions.SetActiveRuntimeSettings(snapshot.LaunchSettings);

            await _followupApplication.ApplyAfterSessionStartedAsync(
                _followup.AfterSessionStarted(),
                new ModelRuntimeStartSessionActions(
                    actions.SaveActiveRuntimeSessionsAsync,
                    () => actions.StartReadinessMonitor(request.Model, launchSettings),
                    actions.StartRuntimeDashboardRefresh,
                    actions.UpdateLoadingStatus,
                    actions.RefreshOverviewAsync,
                    actions.RefreshOverviewModelSelectorAsync,
                    actions.DelayAsync,
                    actions.RefreshRuntimeMetricsAsync));

            await _followupApplication.ApplyAfterInitialMetricsAsync(
                _followup.AfterInitialMetrics(request.Model.Name, actions.RuntimeState(), actions.IsOverviewPage()),
                new ModelRuntimeStartInitialMetricsActions(
                    actions.StopRuntimeDashboardRefresh,
                    actions.UpdateActionButtons,
                    actions.StopLoadingStatus,
                    actions.SaveActiveRuntimeSessionsAsync,
                    actions.SetStatus,
                    actions.UpdateLoadingStatus));

            return new ModelRuntimeLaunchApplicationResult(true, snapshot, launchSettings);
        }
        catch
        {
            actions.StopLoadingStatus();
            throw;
        }
    }
}
