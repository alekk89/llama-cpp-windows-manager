namespace LocalLlmConsole.Services;

public enum CacheClearPromptKind
{
    Information,
    Warning
}

public enum CacheClearApplicationOutcome
{
    UnsafeRoot,
    Busy,
    Empty,
    Declined,
    Cleared
}

public sealed record CacheClearPrompt(
    string Title,
    string Message,
    CacheClearPromptKind Kind);

public sealed record CacheClearApplicationActions(
    Func<AppSettings, bool, CancellationToken, Task<CacheClearPlan>> PlanAsync,
    Func<AppSettings, CancellationToken, Task> ClearAsync,
    Func<bool> HasActiveDownloads,
    Func<bool> IsSettingsPage,
    Action ShowSettingsPage,
    Action<CacheClearPrompt> Notify,
    Func<CacheClearPrompt, bool> Confirm,
    Func<string, Func<Task>, Task> RunBusyAsync,
    Action<string> SetStatus);

public sealed class CacheClearApplicationService
{
    private static string Title => Loc.T("Cache.Clear.Title");

    public async Task<CacheClearApplicationOutcome> ClearAsync(
        AppSettings settings,
        CacheClearApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(actions);

        var plan = await actions.PlanAsync(settings, actions.HasActiveDownloads(), cancellationToken);
        switch (plan.Status)
        {
            case CacheClearPlanStatus.UnsafeRoot:
                actions.Notify(Prompt(plan, CacheClearPromptKind.Warning));
                return CacheClearApplicationOutcome.UnsafeRoot;
            case CacheClearPlanStatus.Busy:
                actions.Notify(Prompt(plan, CacheClearPromptKind.Information));
                return CacheClearApplicationOutcome.Busy;
            case CacheClearPlanStatus.Empty:
                actions.Notify(Prompt(plan, CacheClearPromptKind.Information));
                RefreshSettingsIfVisible(actions);
                return CacheClearApplicationOutcome.Empty;
            case CacheClearPlanStatus.Ready:
                break;
            default:
                throw new InvalidOperationException($"Unsupported cache clear plan: {plan.Status}.");
        }

        if (!actions.Confirm(Prompt(plan, CacheClearPromptKind.Warning)))
            return CacheClearApplicationOutcome.Declined;

        await actions.RunBusyAsync(Loc.T("Cache.Clear.Progress"), async () =>
        {
            await actions.ClearAsync(settings, cancellationToken);
            actions.SetStatus(Loc.T("Cache.Clear.Success", plan.DisplaySize));
            RefreshSettingsIfVisible(actions);
        });
        return CacheClearApplicationOutcome.Cleared;
    }

    private static CacheClearPrompt Prompt(CacheClearPlan plan, CacheClearPromptKind kind)
        => new(Title, plan.Message, kind);

    private static void RefreshSettingsIfVisible(CacheClearApplicationActions actions)
    {
        if (actions.IsSettingsPage())
            actions.ShowSettingsPage();
    }

    private static void Validate(CacheClearApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.PlanAsync);
        ArgumentNullException.ThrowIfNull(actions.ClearAsync);
        ArgumentNullException.ThrowIfNull(actions.HasActiveDownloads);
        ArgumentNullException.ThrowIfNull(actions.IsSettingsPage);
        ArgumentNullException.ThrowIfNull(actions.ShowSettingsPage);
        ArgumentNullException.ThrowIfNull(actions.Notify);
        ArgumentNullException.ThrowIfNull(actions.Confirm);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
