namespace LocalLlmConsole.Services;

public enum RuntimePackageApplicationOutcome
{
    Blocked,
    Cancelled,
    Applied
}

public sealed record RuntimePackageDeleteConfirmation(
    RuntimePackagePreset Preset,
    RuntimeDeletionPlan Plan,
    string Title,
    string Message);

public sealed record RuntimePackageApplicationActions(
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<Task> RefreshRuntimesAsync,
    Func<Task> RefreshOverviewAsync,
    Func<Task> YieldUiAsync,
    Action RefreshPackageGrid,
    Action<string> SetStatus,
    Action<string, string> ShowInformation,
    Func<RuntimePackageDeleteConfirmation, bool> ConfirmDelete,
    Func<string, bool>? ConfirmSkipChecksum = null);

public sealed class RuntimePackageApplicationService
{
    private readonly StateStore _stateStore;
    private readonly RuntimePackageStatusService _packageStatus;
    private readonly RuntimePackageCheckWorkflowService _packageCheck;
    private readonly RuntimePackageInstallWorkflowService _packageInstall;
    private readonly RuntimeDeletionPlanner _deletionPlanner;
    private readonly RuntimeDeletionExecutorService _deletionExecutor;
    private readonly RuntimeBuildPrerequisiteService _prerequisites;

    public RuntimePackageApplicationService(
        StateStore stateStore,
        RuntimePackageStatusService packageStatus,
        RuntimePackageCheckWorkflowService packageCheck,
        RuntimePackageInstallWorkflowService packageInstall,
        RuntimeDeletionPlanner deletionPlanner,
        RuntimeDeletionExecutorService deletionExecutor,
        RuntimeBuildPrerequisiteService prerequisites)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _packageStatus = packageStatus ?? throw new ArgumentNullException(nameof(packageStatus));
        _packageCheck = packageCheck ?? throw new ArgumentNullException(nameof(packageCheck));
        _packageInstall = packageInstall ?? throw new ArgumentNullException(nameof(packageInstall));
        _deletionPlanner = deletionPlanner ?? throw new ArgumentNullException(nameof(deletionPlanner));
        _deletionExecutor = deletionExecutor ?? throw new ArgumentNullException(nameof(deletionExecutor));
        _prerequisites = prerequisites ?? throw new ArgumentNullException(nameof(prerequisites));
    }

    public async Task<RuntimePackageApplicationOutcome> InstallAsync(
        RuntimePackagePreset preset,
        AppSettings settings,
        RuntimeCatalogSessionState sessionState,
        long maxLogBytes,
        RuntimePackageApplicationActions actions)
    {
        Validate(preset, settings, sessionState, actions);

        await _prerequisites.EnsurePackageInstallReadyAsync(preset, settings.WslDistro);
        var inventory = await BuildInventoryAsync(preset, sessionState);
        if (inventory.Unavailable.Count == 0
            && !RuntimePackageInventoryPresenter.CanInstallPackage(inventory.Installed, inventory.SourceBuilds, inventory.CheckedState))
        {
            actions.SetStatus($"This {RuntimePackageSourceCatalog.PackageRuntimeLabel(preset)} is already installed. Run Check to look for a newer release, or delete it before reinstalling.");
            await actions.RefreshRuntimesAsync();
            return RuntimePackageApplicationOutcome.Blocked;
        }

        await actions.RunBusyAsync($"Installing {preset.Label}...", async () =>
        {
            var jobResult = await TryInstallOnceAsync(preset, settings, maxLogBytes, actions, requireChecksum: true);
            if (jobResult is not null)
            {
                sessionState.SetRuntimePackageUpdateState(preset.Id, jobResult.UpdateState);
                await actions.RefreshRuntimesAsync();
                await actions.RefreshOverviewAsync();
                actions.SetStatus(jobResult.StatusMessage);
            }
        });
        return RuntimePackageApplicationOutcome.Applied;
    }

    public async Task<RuntimePackageApplicationOutcome> CheckUpdateAsync(
        RuntimePackagePreset preset,
        RuntimePackagePresetRow? row,
        AppSettings settings,
        RuntimeCatalogSessionState sessionState,
        long maxLogBytes,
        RuntimePackageApplicationActions actions)
    {
        Validate(preset, settings, sessionState, actions);

        if (row is not null)
        {
            ApplyCheckingState(row);
            actions.RefreshPackageGrid();
            await actions.YieldUiAsync();
        }

        var inventory = await BuildInventoryAsync(preset, sessionState);
        await actions.RunBusyAsync($"Checking {preset.Label} release...", async () =>
        {
            try
            {
                var outcome = await _packageCheck.CheckAsync(new RuntimePackageCheckWorkflowRequest(
                    preset,
                    inventory,
                    settings.CudaPackagePreference,
                    maxLogBytes));
                var result = outcome.CheckResult;
                sessionState.SetRuntimePackageUpdateState(preset.Id, result.State);
                if (row is not null)
                {
                    RuntimePackageStatusService.ApplyCheckResult(row, result);
                    actions.RefreshPackageGrid();
                }

                actions.ShowInformation("Runtime download check", result.Message);
            }
            catch (Exception ex)
            {
                if (row is not null)
                {
                    ApplyCheckFailedState(row, ex.Message);
                    actions.RefreshPackageGrid();
                }
                throw;
            }
        });
        await actions.RefreshRuntimesAsync();
        return RuntimePackageApplicationOutcome.Applied;
    }

    public async Task<RuntimePackageApplicationOutcome> DeleteBuildsAsync(
        RuntimePackagePreset preset,
        AppSettings settings,
        RuntimeCatalogSessionState sessionState,
        RuntimePackageApplicationActions actions)
    {
        Validate(preset, settings, sessionState, actions);

        var plan = await _deletionPlanner.PlanPackageDeletionAsync(preset, settings.RuntimeRoot);
        if (!plan.CanDelete)
        {
            actions.SetStatus(plan.StatusMessage);
            return RuntimePackageApplicationOutcome.Blocked;
        }

        var confirmation = new RuntimePackageDeleteConfirmation(
            preset,
            plan,
            "Delete runtime downloads",
            $"Delete all local installs for this {RuntimePackageSourceCatalog.PackageRuntimeLabel(preset)}?{Environment.NewLine}{Environment.NewLine}{preset.Label}{Environment.NewLine}{Environment.NewLine}Installed runtimes: {plan.Folders.Count}");
        if (!actions.ConfirmDelete(confirmation))
            return RuntimePackageApplicationOutcome.Cancelled;

        await actions.RunBusyAsync("Deleting runtime downloads...", async () =>
        {
            await _deletionExecutor.DeletePackageAsync(plan, settings.RuntimeRoot);
            sessionState.RemoveRuntimePackageUpdateState(preset.Id);
            await actions.RefreshRuntimesAsync();
            await actions.RefreshOverviewAsync();
            actions.SetStatus($"Deleted local installs for {preset.Label}.");
        });
        return RuntimePackageApplicationOutcome.Applied;
    }

    private async Task<RuntimePackageInventory> BuildInventoryAsync(
        RuntimePackagePreset preset,
        RuntimeCatalogSessionState sessionState)
    {
        var allRuntimes = await _stateStore.ListRuntimesAsync();
        return _packageStatus.BuildInventory(preset, allRuntimes, sessionState.RuntimePackageUpdateStates);
    }

    private static void ApplyCheckingState(RuntimePackagePresetRow row)
    {
        row.LocalStatus = "Checking...";
        row.LatestRelease = "Checking release...";
        row.CheckAction = "Checking";
        row.CanCheck = false;
    }

    private static void ApplyCheckFailedState(RuntimePackagePresetRow row, string message)
    {
        row.LocalStatus = "Check failed";
        row.LatestRelease = $"Check failed: {message}";
        row.CheckAction = "Check";
        row.CanCheck = true;
    }

    private async Task<RuntimePackageInstallWorkflowResult?> TryInstallOnceAsync(
        RuntimePackagePreset preset,
        AppSettings settings,
        long maxLogBytes,
        RuntimePackageApplicationActions actions,
        bool requireChecksum)
    {
        try
        {
            return await _packageInstall.InstallAsync(new RuntimePackageInstallWorkflowRequest(
                preset,
                settings,
                maxLogBytes,
                RequireChecksum: requireChecksum));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("missing SHA-256 verification metadata", StringComparison.OrdinalIgnoreCase))
        {
            if (actions.ConfirmSkipChecksum is null)
                throw;

            var confirm = actions.ConfirmSkipChecksum(
                $"SHA-256 verification metadata is not available for this release.{Environment.NewLine}{Environment.NewLine}" +
                $"Install {preset.Label} without checksum verification?{Environment.NewLine}{Environment.NewLine}" +
                "Only proceed if you trust the download source.");
            if (!confirm)
            {
                actions.SetStatus("Install cancelled: checksum verification is required for this package.");
                return null;
            }

            return await _packageInstall.InstallAsync(new RuntimePackageInstallWorkflowRequest(
                preset,
                settings,
                maxLogBytes,
                RequireChecksum: false));
        }
    }

    private static void Validate(
        RuntimePackagePreset preset,
        AppSettings settings,
        RuntimeCatalogSessionState sessionState,
        RuntimePackageApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sessionState);
        Validate(actions);
    }

    private static void Validate(RuntimePackageApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshRuntimesAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshOverviewAsync);
        ArgumentNullException.ThrowIfNull(actions.YieldUiAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshPackageGrid);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
        ArgumentNullException.ThrowIfNull(actions.ShowInformation);
        ArgumentNullException.ThrowIfNull(actions.ConfirmDelete);
        // ConfirmSkipChecksum is optional and may be null.
    }
}
