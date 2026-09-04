namespace LocalLlmConsole.Services;

public sealed record AppUpdateCheckWorkflowResult(
    AppUpdateInfo Update,
    string StatusMessage,
    bool ShouldPromptInstall,
    bool ShouldShowNoUpdateDialog,
    string DialogTitle,
    string DialogMessage);

public sealed class AppUpdateWorkflowService
{
    private readonly AppUpdateService _updates;
    private readonly string _workspaceRoot;

    public AppUpdateWorkflowService(AppUpdateService updates, string workspaceRoot)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot))
            : workspaceRoot;
    }

    public async Task<AppUpdateCheckWorkflowResult> CheckLatestAsync(
        bool manual,
        CancellationToken cancellationToken = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var update = await _updates.CheckLatestAsync(linked.Token);
        return DescribeCheckResult(update, manual);
    }

    public async Task<string> StageAndStartInstallAsync(
        AppUpdateInfo update,
        string? currentExecutablePath,
        int currentProcessId,
        CancellationToken cancellationToken = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var plan = await _updates.StageInstallAsync(update, _workspaceRoot, currentExecutablePath, linked.Token);
        await _updates.StartInstallerAsync(plan, currentProcessId, linked.Token);
        return "Update staged. Closing to install...";
    }

    public async Task<InstalledUpdateNotice?> TryConsumeInstalledNoticeAsync(CancellationToken cancellationToken = default)
        => await AppUpdateService.TryConsumeInstalledNoticeAsync(_workspaceRoot, cancellationToken);

    public static AppUpdateCheckWorkflowResult DescribeCheckResult(AppUpdateInfo update, bool manual)
    {
        if (update.IsAvailable)
        {
            return new AppUpdateCheckWorkflowResult(
                update,
                $"Update available: {update.LatestVersion}.",
                ShouldPromptInstall: manual,
                ShouldShowNoUpdateDialog: false,
                "Install update",
                $"Update {update.CurrentVersion} -> {update.LatestVersion} is available.\n\n{update.ReleaseName}\n\n" +
                (update.AuthenticityVerified ? "" : "This release is unsigned. Its SHA-256 checksum will be checked before installation.\n\n") +
                "Install it now?");
        }

        return new AppUpdateCheckWorkflowResult(
            update,
            "No app updates available.",
            ShouldPromptInstall: false,
            ShouldShowNoUpdateDialog: manual,
            "Check for updates",
            $"No updates are available.\n\nCurrent version: {update.CurrentVersion}");
    }
}
