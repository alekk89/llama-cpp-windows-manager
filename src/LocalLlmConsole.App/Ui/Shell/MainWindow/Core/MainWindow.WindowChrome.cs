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
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsTitleBarButtonSource(e.OriginalSource as DependencyObject)) return;

        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if Windows has already ended the mouse operation.
        }
    }

    private static bool IsTitleBarButtonSource(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Button) return true;
        }
        return false;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        var plan = _coreServices.Ui.TrayWindowState.BuildMinimizePlan(_settings.MinimizeBehavior);
        if (plan.Action == TrayMinimizeAction.TrayOnly)
        {
            MinimizeToTray();
            return;
        }

        if (plan.Action == TrayMinimizeAction.TrayAndTaskbar)
            ShowTrayIcon();
        else
            HideTrayIcon();
        if (!string.IsNullOrWhiteSpace(plan.StatusMessage))
            SetStatus(plan.StatusMessage);
        WindowState = WindowState.Minimized;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        ApplyWindowWorkAreaBounds();
        var action = _coreServices.Ui.TrayWindowState.WindowStateChangedAction(WindowState, _settings.MinimizeBehavior);
        if (action == TrayMinimizeAction.TrayOnly)
        {
            MinimizeToTray();
            return;
        }

        if (action == TrayMinimizeAction.TrayAndTaskbar)
            ShowTrayIcon();
        else if (WindowState != WindowState.Minimized)
        {
            HideTrayIcon();
        }

        if (WindowState != WindowState.Minimized)
        {
            _minimizedUiRefreshPolicy.Reset();
            if (_viewModel.CurrentPage == "Overview")
                RunBackground(RefreshRuntimeMetricsAsync, "Runtime metrics refresh failed");
        }
    }

    private void ApplyWindowWorkAreaBounds()
    {
        if (WindowState != WindowState.Maximized)
        {
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            return;
        }

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var workingArea = Forms.Screen.FromHandle(handle).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            MaxWidth = workingArea.Width;
            MaxHeight = workingArea.Height;
            return;
        }

        var topLeft = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(workingArea.Left, workingArea.Top));
        var bottomRight = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(workingArea.Right, workingArea.Bottom));
        MaxWidth = Math.Max(MinWidth, bottomRight.X - topLeft.X);
        MaxHeight = Math.Max(MinHeight, bottomRight.Y - topLeft.Y);
    }

    private void ConstrainInitialWindowToWorkArea()
    {
        if (WindowState == WindowState.Maximized) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var workingArea = Forms.Screen.FromHandle(handle).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var topLeft = source?.CompositionTarget is null
            ? SystemParameters.WorkArea.TopLeft
            : source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(workingArea.Left, workingArea.Top));
        var bottomRight = source?.CompositionTarget is null
            ? SystemParameters.WorkArea.BottomRight
            : source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(workingArea.Right, workingArea.Bottom));
        var layout = WindowWorkAreaSizingService.Fit(
            Width,
            Height,
            MinWidth,
            MinHeight,
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
        MinWidth = layout.MinimumWidth;
        MinHeight = layout.MinimumHeight;
        Width = layout.Width;
        Height = layout.Height;
        Left = layout.Left;
        Top = layout.Top;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleWindowState();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = TrayIconHost.Create(
            AppDisplayName,
            () => Dispatcher.Invoke(RestoreFromTray),
            () => Dispatcher.InvokeAsync(OpenTrayProfileMenuAsync));
    }

    private void ShowTrayIcon()
    {
        if (_trayIcon is null) InitializeTrayIcon();
        if (_trayIcon is not null) _trayIcon.Visible = true;
    }

    private void HideTrayIcon()
    {
        if (_trayIcon is not null) _trayIcon.Visible = false;
    }

    private void ApplyTrayIconVisibilityPreference()
    {
        if (TrayWindowStateController.KeepsTrayIconVisible(_settings.MinimizeBehavior)
            || (AppPreferenceService.MinimizeBehavior(_settings.MinimizeBehavior) == "trayOnly"
                && (!IsVisible || !ShowInTaskbar)))
        {
            ShowTrayIcon();
            return;
        }

        HideTrayIcon();
    }

    private void MinimizeToTray()
    {
        var plan = _coreServices.Ui.TrayWindowState.BeginHideToTray(WindowState);
        if (!plan.ShouldApply) return;
        try
        {
            ShowTrayIcon();
            ShowInTaskbar = false;
            Hide();

            if (plan.ShouldShowHint)
                _trayIcon?.ShowStillRunningHint(Localization.Loc.T("Tray.StillRunningHint"));

            SetStatus(plan.StatusMessage);
        }
        finally
        {
            _coreServices.Ui.TrayWindowState.CompleteHideToTray();
        }
    }

    private void RestoreFromTray()
    {
        var plan = _coreServices.Ui.TrayWindowState.BuildRestorePlan();
        ShowInTaskbar = true;
        Show();
        WindowState = plan.RestoreState;
        ApplyTrayIconVisibilityPreference();
        Activate();
    }

    private void DisposeTrayIcon()
    {
        _trayProfileMenu?.Dispose();
        _trayProfileMenu = null;
        if (_trayIcon is null) return;
        try
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not dispose tray icon: {ex.Message}");
        }
        _trayIcon = null;
    }
}
