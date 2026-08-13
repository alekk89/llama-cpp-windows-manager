namespace LocalLlmConsole.Services;

public enum AppShutdownApplicationOutcomeKind
{
    AllowClose,
    AlreadyInProgress,
    CancelledByUser,
    CleanupCompleted
}

public sealed record AppShutdownApplicationRequest(
    int RunningModelSessions,
    int ActiveDownloads,
    bool Confirmed = false);

public sealed record AppShutdownApplicationActions(
    Func<AppShutdownConfirmationPrompt, Task<bool>> ConfirmAsync,
    Action DisableUi,
    Action<string> SetStatus,
    Func<Task> CleanupAsync);

public sealed record AppShutdownApplicationOutcome(
    AppShutdownApplicationOutcomeKind Kind,
    bool CancelClosingEvent,
    bool RequestClose);

public sealed class AppShutdownApplicationService
{
    private readonly AppShutdownDecisionService _decisions;
    private readonly AppShutdownStateController _state;

    public AppShutdownApplicationService(
        AppShutdownDecisionService decisions,
        AppShutdownStateController state)
    {
        _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public async Task<AppShutdownApplicationOutcome> BeginShutdownAsync(
        AppShutdownApplicationRequest request,
        AppShutdownApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.ConfirmAsync);
        ArgumentNullException.ThrowIfNull(actions.DisableUi);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
        ArgumentNullException.ThrowIfNull(actions.CleanupAsync);

        var closeAdmission = _state.BeginClosing();
        if (closeAdmission == AppShutdownCloseAdmission.AllowClose)
            return new AppShutdownApplicationOutcome(
                AppShutdownApplicationOutcomeKind.AllowClose,
                CancelClosingEvent: false,
                RequestClose: false);
        if (closeAdmission == AppShutdownCloseAdmission.CancelAlreadyInProgress)
            return new AppShutdownApplicationOutcome(
                AppShutdownApplicationOutcomeKind.AlreadyInProgress,
                CancelClosingEvent: true,
                RequestClose: false);

        try
        {
            var decision = _decisions.Build(request.RunningModelSessions, request.ActiveDownloads);
            foreach (var confirmation in request.Confirmed ? [] : decision.Confirmations)
            {
                if (await actions.ConfirmAsync(confirmation))
                    continue;

                _state.ResetRequest();
                return new AppShutdownApplicationOutcome(
                    AppShutdownApplicationOutcomeKind.CancelledByUser,
                    CancelClosingEvent: true,
                    RequestClose: false);
            }

            actions.DisableUi();
            actions.SetStatus(decision.ClosingStatus);
            await actions.CleanupAsync();
            _state.MarkCleanupComplete();
            return new AppShutdownApplicationOutcome(
                AppShutdownApplicationOutcomeKind.CleanupCompleted,
                CancelClosingEvent: true,
                RequestClose: true);
        }
        catch
        {
            _state.ResetRequest();
            throw;
        }
    }
}
