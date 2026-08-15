using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private void ShowUpdates()
    {
        SetPage("Updates", "App updates from GitHub releases.");
        var page = UpdatesPageFactory.Create(new UpdatesPageRequest(
            _viewModel.Updates,
            new UpdatesPageActions(
                ShowUpdatesPrimaryActionAsync,
                () => _coreServices.App.ShellIntegration.OpenUrl(AppUpdateService.RepositoryUrl))));
        PageHost.Content = page.Content;
    }

    private async Task ShowUpdatesPrimaryActionAsync()
    {
        if (_viewModel.Updates.LatestUpdate is { IsAvailable: true } available)
            await InstallAppUpdateAsync(available, confirm: true);
        else
            await CheckForAppUpdatesAsync(manual: true);
    }

    private async Task CheckForAppUpdatesOnStartupAsync()
    {
        try
        {
            await CheckForAppUpdatesAsync(manual: false);
        }
        catch
        {
        }
    }

    private async Task CheckForAppUpdatesAsync(bool manual)
    {
        await _coreServices.App.AppUpdateApplication.CheckForUpdatesAsync(manual, AppUpdateCheckActions());
    }

    private async Task InstallAppUpdateAsync(AppUpdateInfo update, bool confirm)
    {
        await _coreServices.App.AppUpdateApplication.InstallAsync(
            new AppUpdateInstallApplicationRequest(update, confirm, Environment.ProcessPath, Environment.ProcessId),
            AppUpdateInstallActions());
    }

    private AppUpdateCheckApplicationActions AppUpdateCheckActions()
        => new(
            () => _viewModel.Updates.CheckInFlight,
            inFlight => _viewModel.Updates.CheckInFlight = inFlight,
            async (isManual, token) => await _coreServices.App.AppUpdateWorkflow.CheckLatestAsync(isManual, token),
            _viewModel.Updates.SetLatestUpdate,
            UpdateAppUpdateNavigation,
            () => _viewModel.CurrentPage == "Updates",
            ShowUpdates,
            SetStatus,
            ConfirmAppUpdatePrompt,
            NotifyAppUpdatePrompt,
            InstallAppUpdateAsync);

    private AppUpdateInstallApplicationActions AppUpdateInstallActions()
        => new(
            ConfirmAppUpdatePrompt,
            NotifyAppUpdatePrompt,
            RunAsync,
            async (requestedUpdate, processPath, processId, token) =>
                await _coreServices.App.AppUpdateWorkflow.StageAndStartInstallAsync(requestedUpdate, processPath, processId, token),
            SetStatus,
            Close);

    private bool ConfirmAppUpdatePrompt(AppUpdateApplicationPrompt prompt)
        => _coreServices.App.Dialogs.Confirm(
            this,
            prompt.Message,
            prompt.Title,
            MessageBoxImageFor(prompt.Kind));

    private void NotifyAppUpdatePrompt(AppUpdateApplicationPrompt prompt)
        => _coreServices.App.Dialogs.Notify(
            this,
            prompt.Message,
            prompt.Title,
            MessageBoxImageFor(prompt.Kind));

    private static MessageBoxImage MessageBoxImageFor(AppUpdateApplicationPromptKind kind)
        => kind == AppUpdateApplicationPromptKind.Warning
            ? MessageBoxImage.Warning
            : MessageBoxImage.Information;

    private async Task ShowCompletedAppUpdateNoticeAsync()
    {
        var notice = await _coreServices.App.AppUpdateWorkflow.TryConsumeInstalledNoticeAsync();
        if (notice is null) return;
        _coreServices.App.Dialogs.Notify(
            this,
            $"Updated to {notice.Version}.\n\n{notice.ReleaseName}\n\n{DisplayFormatService.TrimForDisplay(notice.ReleaseNotes, 2600)}",
            "Update installed",
            MessageBoxImage.Information);
    }

    private void UpdateAppUpdateNavigation()
    {
        UpdatesNavButton.Content = _viewModel.Updates.HasAvailableUpdate
            ? Localization.Loc.T("Nav.InstallUpdate")
            : Localization.Loc.T("Nav.CheckForUpdates");
    }
}
