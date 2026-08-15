namespace LocalLlmConsole.Services;

public sealed record ForegroundTaskApplicationActions(
    Func<string, bool> TryBeginBusy,
    Action EndBusy,
    Action<string> SetStatus,
    Func<string> CurrentStatus,
    Func<Task> YieldAsync,
    Func<Exception, Task> WriteErrorAsync,
    Action<Exception> ShowError);

public sealed class ForegroundTaskApplicationService
{
    public async Task RunBusyAsync(
        string message,
        Func<Task> action,
        ForegroundTaskApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(action);
        Validate(actions);

        if (!actions.TryBeginBusy(message))
            return;

        try
        {
            actions.SetStatus(message);
            await actions.YieldAsync();
            await action();
            if (string.Equals(actions.CurrentStatus(), message, StringComparison.Ordinal))
                actions.SetStatus("");
        }
        catch (Exception ex)
        {
            await ReportErrorAsync(ex, actions);
        }
        finally
        {
            actions.EndBusy();
        }
    }

    public async Task RunEventAsync(
        Func<Task> action,
        ForegroundTaskApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(action);
        Validate(actions);

        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // Superseded interactive work is expected when the user changes selection quickly.
        }
        catch (Exception ex)
        {
            await ReportErrorAsync(ex, actions);
        }
    }

    private static async Task ReportErrorAsync(
        Exception exception,
        ForegroundTaskApplicationActions actions)
    {
        actions.SetStatus(exception.Message);
        await actions.WriteErrorAsync(exception);
        actions.ShowError(exception);
    }

    private static void Validate(ForegroundTaskApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.TryBeginBusy);
        ArgumentNullException.ThrowIfNull(actions.EndBusy);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
        ArgumentNullException.ThrowIfNull(actions.CurrentStatus);
        ArgumentNullException.ThrowIfNull(actions.YieldAsync);
        ArgumentNullException.ThrowIfNull(actions.WriteErrorAsync);
        ArgumentNullException.ThrowIfNull(actions.ShowError);
    }
}
