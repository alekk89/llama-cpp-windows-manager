namespace LocalLlmConsole.Services;

public sealed record DownloadCompletionApplicationActions(
    Func<string, TimeSpan, Task> WaitUntilInactiveOrTerminalAsync,
    Func<Func<Task>, Task> RunOnUiThreadAsync,
    Func<Task> ScanModelsAsync,
    Func<Task> RefreshModelsAsync,
    Func<Task> RefreshOverviewAsync,
    Func<Task> RefreshDownloadHistoryAsync,
    Func<Task> RefreshHuggingFaceInstallStateAsync);

public sealed class DownloadCompletionApplicationService
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(1500);

    public async Task MonitorAsync(
        string jobId,
        DownloadCompletionApplicationActions actions)
    {
        Validate(actions);

        await actions.WaitUntilInactiveOrTerminalAsync(jobId, DefaultPollInterval);
        await actions.RunOnUiThreadAsync(async () =>
        {
            await actions.ScanModelsAsync();
            await actions.RefreshModelsAsync();
            await actions.RefreshOverviewAsync();
            await actions.RefreshDownloadHistoryAsync();
            await actions.RefreshHuggingFaceInstallStateAsync();
        });
    }

    private static void Validate(DownloadCompletionApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.WaitUntilInactiveOrTerminalAsync);
        ArgumentNullException.ThrowIfNull(actions.RunOnUiThreadAsync);
        ArgumentNullException.ThrowIfNull(actions.ScanModelsAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshModelsAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshOverviewAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshDownloadHistoryAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshHuggingFaceInstallStateAsync);
    }
}
