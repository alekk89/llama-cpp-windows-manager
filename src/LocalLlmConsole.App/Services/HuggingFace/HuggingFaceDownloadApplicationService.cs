namespace LocalLlmConsole.Services;

public enum HuggingFaceDownloadApplicationOutcome
{
    Started
}

public sealed record HuggingFaceDownloadApplicationActions(
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<HuggingFaceFile, AppSettings, Task<JobRecord>> StartDownloadAsync,
    Func<Task> RefreshOverviewAsync,
    Func<string, Task> ShowDownloadHistoryAsync,
    Action<string> StartMonitor,
    Action<string> SetStatus);

public sealed class HuggingFaceDownloadApplicationService
{
    public async Task<HuggingFaceDownloadApplicationOutcome> StartAsync(
        HuggingFaceFile file,
        AppSettings settings,
        HuggingFaceDownloadApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(settings);
        Validate(actions);

        await actions.RunBusyAsync("Starting download...", async () =>
        {
            var job = await actions.StartDownloadAsync(file, settings);
            await actions.RefreshOverviewAsync();
            await actions.ShowDownloadHistoryAsync(job.Id);
            actions.StartMonitor(job.Id);
            actions.SetStatus($"Download started: {file.Name} ({job.Id})");
        });
        return HuggingFaceDownloadApplicationOutcome.Started;
    }

    private static void Validate(HuggingFaceDownloadApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.StartDownloadAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshOverviewAsync);
        ArgumentNullException.ThrowIfNull(actions.ShowDownloadHistoryAsync);
        ArgumentNullException.ThrowIfNull(actions.StartMonitor);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
