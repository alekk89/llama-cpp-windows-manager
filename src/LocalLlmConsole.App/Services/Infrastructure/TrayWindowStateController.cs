using System.Windows;

namespace LocalLlmConsole.Services;

public enum TrayMinimizeAction
{
    TaskbarOnly,
    TrayOnly,
    TrayAndTaskbar
}

public sealed record TrayMinimizePlan(
    TrayMinimizeAction Action,
    string StatusMessage = "");

public sealed record TrayHidePlan(
    bool ShouldApply,
    bool ShouldShowHint,
    WindowState RestoreState,
    string StatusMessage);

public sealed record TrayRestorePlan(WindowState RestoreState);

public sealed class TrayWindowStateController
{
    private WindowState _restoreState = WindowState.Normal;
    private bool _isMinimizingToTray;
    private bool _trayHintShown;

    public bool IsMinimizingToTray => _isMinimizingToTray;

    public bool HasShownTrayHint => _trayHintShown;

    public WindowState RestoreState => _restoreState;

    public TrayMinimizePlan BuildMinimizePlan(string minimizeBehavior)
    {
        var behavior = AppPreferenceService.MinimizeBehavior(minimizeBehavior);
        return behavior switch
        {
            "trayOnly" => new TrayMinimizePlan(TrayMinimizeAction.TrayOnly),
            "trayAndTaskbar" => new TrayMinimizePlan(TrayMinimizeAction.TrayAndTaskbar, "Minimized to taskbar and tray."),
            _ => new TrayMinimizePlan(TrayMinimizeAction.TaskbarOnly)
        };
    }

    public TrayMinimizeAction WindowStateChangedAction(WindowState windowState, string minimizeBehavior)
    {
        var behavior = AppPreferenceService.MinimizeBehavior(minimizeBehavior);
        if (behavior == "trayAndTaskbar")
            return TrayMinimizeAction.TrayAndTaskbar;
        if (windowState != WindowState.Minimized)
            return TrayMinimizeAction.TaskbarOnly;

        return behavior == "trayOnly"
            ? TrayMinimizeAction.TrayOnly
            : TrayMinimizeAction.TaskbarOnly;
    }

    public static bool KeepsTrayIconVisible(string minimizeBehavior)
        => AppPreferenceService.MinimizeBehavior(minimizeBehavior) == "trayAndTaskbar";

    public TrayHidePlan BeginHideToTray(WindowState currentWindowState)
    {
        if (_isMinimizingToTray)
            return new TrayHidePlan(false, false, _restoreState, "");

        _isMinimizingToTray = true;
        if (currentWindowState != WindowState.Minimized)
        {
            _restoreState = currentWindowState;
        }
        else if (_restoreState == WindowState.Minimized)
        {
            _restoreState = WindowState.Normal;
        }

        var shouldShowHint = !_trayHintShown;
        if (shouldShowHint)
            _trayHintShown = true;

        return new TrayHidePlan(true, shouldShowHint, _restoreState, "Minimized to tray.");
    }

    public void CompleteHideToTray()
        => _isMinimizingToTray = false;

    public TrayRestorePlan BuildRestorePlan()
        => new(_restoreState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal);
}
