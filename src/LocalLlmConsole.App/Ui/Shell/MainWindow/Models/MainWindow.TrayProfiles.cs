using System.Windows;

namespace LocalLlmConsole;

public partial class MainWindow
{
    private async Task OpenTrayProfileMenuAsync()
    {
        if (_modelServices is null)
        {
            _trayIcon?.ShowNotification(Loc.T("Tray.AppStarting"));
            return;
        }

        _trayProfileMenu ??= new TrayProfileMenuController(
            ModelServices.TrayProfiles,
            new TrayProfileMenuControllerActions(
                ExecuteTrayProfileAsync,
                ReportTrayProfileErrorAsync,
                RestoreFromTray,
                () =>
                {
                    RestoreFromTray();
                    Close();
                }));
        await _trayProfileMenu.OpenAsync(this);
    }

    private async Task ExecuteTrayProfileAsync(TrayProfileMenuEntry entry)
    {
        var result = await ModelServices.TrayProfiles.ExecuteAsync(
            entry,
            new TrayProfileCommandActions(LoadTrayProfileAsync, StopTrayProfileAsync));
        if (result.Action == TrayProfileActionKind.Stop)
        {
            if (result.StopCompleted)
                _trayIcon?.ShowNotification(Loc.T("Tray.StoppedProfile", entry.Model.Name, entry.Profile.Name));
            else if (!string.IsNullOrWhiteSpace(_viewModel.StatusText))
                _trayIcon?.ShowNotification(_viewModel.StatusText);
            return;
        }

        if (result.LoadOutcome == ModelRuntimeLoadApplicationOutcome.Started)
        {
            _trayIcon?.ShowNotification(Loc.T("Tray.StartedProfile", entry.Model.Name, entry.Profile.Name));
        }
        else if (!string.IsNullOrWhiteSpace(_viewModel.StatusText))
        {
            _trayIcon?.ShowNotification(
                _viewModel.StatusText,
                error: result.LoadOutcome == ModelRuntimeLoadApplicationOutcome.MissingRuntime);
        }
    }

    private async Task<ModelRuntimeLoadApplicationOutcome> LoadTrayProfileAsync(
        ModelRecord model,
        NamedModelLaunchProfile profile)
        => await _coreServices.Models.ModelRuntimeLoadApplication.LoadOverviewAsync(
            new OverviewModelRuntimeLoadApplicationRequest(
                model,
                IsModelLoaded(model),
                IsModelActive(model),
                AppReady: true,
                SelectedProfileLoaded: false),
            ModelRuntimeLoadActions(
                () => _settings,
                profile.Id,
                profile.Name,
                RunTrayResponsiveAsync,
                restoreForInteractivePrompts: true));

    private async Task StopTrayProfileAsync(ModelRecord model, NamedModelLaunchProfile profile)
        => await RunTrayResponsiveAsync(
            Loc.T("Tray.StoppingProfile", model.Name, profile.Name),
            () => _overviewSelection.UnloadSessionAsync(LoadedModelSessionManager.SessionIdFor(model.Id, profile.Id)));

    private async Task ReportTrayProfileErrorAsync(Exception exception)
    {
        SetStatus(exception.Message);
        await WriteAppLogAsync(exception);
        _trayIcon?.ShowNotification(exception.Message, error: true);
    }

    private void QueueOpenTrayProfileMenuRefresh()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_trayProfileMenu is not null)
                RunBackground(_trayProfileMenu.RefreshIfOpenAsync, "Tray profile menu refresh failed");
        });
    }

    private async Task ToggleTrayProfileFavoriteAsync(ModelRecord model, NamedModelLaunchProfile profile)
    {
        var favorite = await ModelServices.TrayProfiles.ToggleFavoriteAsync(profile.Id);
        SetStatus(Loc.T(
            favorite ? "Tray.FavoriteAdded" : "Tray.FavoriteRemoved",
            model.Name,
            profile.Name));
        await RefreshModelsAsync();
    }
}
