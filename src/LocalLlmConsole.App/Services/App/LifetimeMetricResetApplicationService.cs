using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public enum LifetimeMetricResetApplicationOutcome
{
    Ignored,
    Blocked,
    Cancelled,
    ResetModel,
    ResetAll
}

public sealed record LifetimeMetricResetConfirmation(
    string Title,
    string Message);

public sealed record LifetimeMetricResetApplicationActions(
    Func<LifetimeMetricResetConfirmation, bool> ConfirmReset,
    Func<string, Task> DeleteModelUsageAsync,
    Func<Task> DeleteAllUsageAsync,
    Action ResetLifetimeCounters,
    Func<Task> RefreshMetricsAsync,
    Action<string> SetStatus);

public sealed class LifetimeMetricResetApplicationService
{
    public async Task<LifetimeMetricResetApplicationOutcome> ResetAsync(
        UiRow? row,
        LifetimeMetricResetApplicationActions actions)
    {
        Validate(actions);
        if (row is null)
            return LifetimeMetricResetApplicationOutcome.Ignored;

        if (string.Equals(row.Data["Kind"]?.ToString(), "total", StringComparison.OrdinalIgnoreCase))
            return await ResetAllAsync(actions);

        return await ResetModelAsync(row, actions);
    }

    private static async Task<LifetimeMetricResetApplicationOutcome> ResetModelAsync(
        UiRow row,
        LifetimeMetricResetApplicationActions actions)
    {
        if (!row.B1)
        {
            actions.SetStatus("Only model rows can be reset individually.");
            return LifetimeMetricResetApplicationOutcome.Blocked;
        }

        var modelId = row.Data["ModelId"]?.ToString();
        var modelName = row.Data["ModelName"]?.ToString() ?? row.C1;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            actions.SetStatus("Only model rows can be reset individually.");
            return LifetimeMetricResetApplicationOutcome.Blocked;
        }

        var confirmation = new LifetimeMetricResetConfirmation(
            "Reset lifetime metrics",
            $"Reset usage metrics for:{Environment.NewLine}{Environment.NewLine}{modelName}");
        if (!actions.ConfirmReset(confirmation))
            return LifetimeMetricResetApplicationOutcome.Cancelled;

        await actions.DeleteModelUsageAsync(modelId);
        await actions.RefreshMetricsAsync();
        actions.SetStatus($"Lifetime metrics reset for {modelName}.");
        return LifetimeMetricResetApplicationOutcome.ResetModel;
    }

    private static async Task<LifetimeMetricResetApplicationOutcome> ResetAllAsync(
        LifetimeMetricResetApplicationActions actions)
    {
        var confirmation = new LifetimeMetricResetConfirmation(
            "Reset all lifetime metrics",
            "Reset usage metrics for all models?");
        if (!actions.ConfirmReset(confirmation))
            return LifetimeMetricResetApplicationOutcome.Cancelled;

        await actions.DeleteAllUsageAsync();
        actions.ResetLifetimeCounters();
        await actions.RefreshMetricsAsync();
        actions.SetStatus("All lifetime metrics reset.");
        return LifetimeMetricResetApplicationOutcome.ResetAll;
    }

    private static void Validate(LifetimeMetricResetApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.ConfirmReset);
        ArgumentNullException.ThrowIfNull(actions.DeleteModelUsageAsync);
        ArgumentNullException.ThrowIfNull(actions.DeleteAllUsageAsync);
        ArgumentNullException.ThrowIfNull(actions.ResetLifetimeCounters);
        ArgumentNullException.ThrowIfNull(actions.RefreshMetricsAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
