using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class UiRuntimeAndWindowTests : ManagerRegressionTestBase
{
    [Fact]
    public void MinimizeBehaviorUsesExplicitTrayAndTaskbarModes()
    {
        Loc.LoadLanguage("en");
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var controller = new TrayWindowStateController();

        Assert.Equal("taskbarOnly", settings.MinimizeBehavior);
        Assert.Equal(
            [Loc.T("Pref.TaskbarOnly"), Loc.T("Pref.TrayOnly"), Loc.T("Pref.TrayAndTaskbar")],
            AppPreferenceService.MinimizeBehaviorOptions());
        Assert.Equal("trayAndTaskbar", AppPreferenceService.MinimizeBehavior("Tray + taskbar"));
        Assert.Equal("both", AppPreferenceService.ModelAccessMode("network access"));
        Assert.Equal("gateway", AppPreferenceService.ModelAccessMode("Gateway LAN only"));
        Assert.True(AppPreferenceService.GatewayAllowsLanAccess("Gateway LAN only"));
        Assert.False(AppPreferenceService.DirectModelsAllowLanAccess("Gateway LAN only"));
        Assert.Equal("127.0.0.1", AppPreferenceService.RuntimeHostForAccessMode("Gateway LAN only"));
        Assert.Equal("0.0.0.0", AppPreferenceService.RuntimeHostForAccessMode("Direct models LAN only"));
        Assert.Equal("latest", settings.CudaPackagePreference);
        Assert.Equal(
            [Loc.T("Pref.Latest"), Loc.T("Pref.Compatibility")],
            AppPreferenceService.CudaPackagePreferenceOptions());
        Assert.Equal("latest", AppPreferenceService.CudaPackagePreference("Latest"));
        Assert.Equal("compatibility", AppPreferenceService.CudaPackagePreference("CUDA 12 compatibility"));
        Assert.True(AppPreferenceService.YesNoValue("on", fallback: false));
        Assert.True(AppPreferenceService.TryIntValue("42", out var parsed));
        Assert.Equal(42, parsed);
        Assert.False(AppPreferenceService.TryIntValue("bad", out _));
        Assert.Equal(10, AppPreferenceService.ClampedIntValue("99", fallback: 7, min: 1, max: 10));
        Assert.Equal(TrayMinimizeAction.TaskbarOnly, controller.BuildMinimizePlan("taskbarOnly").Action);
        Assert.Equal(TrayMinimizeAction.TrayOnly, controller.BuildMinimizePlan("trayOnly").Action);
        var trayAndTaskbar = controller.BuildMinimizePlan("trayAndTaskbar");
        Assert.Equal(TrayMinimizeAction.TrayAndTaskbar, trayAndTaskbar.Action);
        Assert.Contains("taskbar and tray", trayAndTaskbar.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TrayMinimizeAction.TrayOnly, controller.WindowStateChangedAction(System.Windows.WindowState.Minimized, "trayOnly"));
        Assert.Equal(TrayMinimizeAction.TaskbarOnly, controller.WindowStateChangedAction(System.Windows.WindowState.Normal, "trayOnly"));
        Assert.Equal(TrayMinimizeAction.TrayAndTaskbar, controller.WindowStateChangedAction(System.Windows.WindowState.Normal, "trayAndTaskbar"));
        Assert.Equal(TrayMinimizeAction.TrayAndTaskbar, controller.WindowStateChangedAction(System.Windows.WindowState.Minimized, "trayAndTaskbar"));
        Assert.True(TrayWindowStateController.KeepsTrayIconVisible("trayAndTaskbar"));
        Assert.False(TrayWindowStateController.KeepsTrayIconVisible("trayOnly"));

        var minimize = controller.BeginHideToTray(System.Windows.WindowState.Maximized);
        Assert.True(minimize.ShouldApply);
        Assert.True(minimize.ShouldShowHint);
        Assert.True(controller.IsMinimizingToTray);
        Assert.True(controller.HasShownTrayHint);
        Assert.Equal(System.Windows.WindowState.Maximized, controller.RestoreState);
        controller.CompleteHideToTray();
        Assert.False(controller.IsMinimizingToTray);
        Assert.Equal(System.Windows.WindowState.Maximized, controller.BuildRestorePlan().RestoreState);
        var secondMinimize = controller.BeginHideToTray(System.Windows.WindowState.Minimized);
        Assert.True(secondMinimize.ShouldApply);
        Assert.False(secondMinimize.ShouldShowHint);
        Assert.Equal(System.Windows.WindowState.Maximized, secondMinimize.RestoreState);
        controller.CompleteHideToTray();
    }

    [Fact]
    public void MainWindowViewModelTracksPageStatusAndBusyState()
    {
        Loc.LoadLanguage("en");
        var vm = new MainWindowViewModel();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        var controller = new UiBusyStateController();
        var pageEnabled = true;
        bool? waitCursor = null;

        Assert.Equal("Overview", vm.CurrentPage);
        Assert.Equal(Loc.T("Status.Starting"), vm.StatusText);
        Assert.True(vm.TryBeginBusy(out var busyMessage));
        Assert.Equal("", busyMessage);
        Assert.False(vm.TryBeginBusy(out busyMessage));
        Assert.Equal(Loc.T("Status.PleaseWaitFor", vm.StatusText), busyMessage);
        Assert.True(vm.EndBusy());
        Assert.False(vm.EndBusy());

        vm.CurrentPage = "Models";
        vm.SetStatus("");

        Assert.Equal("Models", vm.CurrentPage);
        Assert.Equal(Loc.T("Status.Ready"), vm.DisplayStatusText);
        Assert.Contains(nameof(MainWindowViewModel.CurrentPage), changes);
        Assert.Contains(nameof(MainWindowViewModel.StatusText), changes);
        Assert.Contains(nameof(MainWindowViewModel.DisplayStatusText), changes);
        Assert.Contains(nameof(MainWindowViewModel.IsBusy), changes);

        controller.Begin(pageEnabled, enabled => pageEnabled = enabled, enabled => waitCursor = enabled);
        Assert.True(controller.HasActiveBusyState);
        Assert.False(pageEnabled);
        Assert.True(waitCursor);

        controller.Begin(pageIsEnabled: true, enabled => pageEnabled = enabled, enabled => waitCursor = enabled);
        Assert.False(pageEnabled);
        Assert.True(waitCursor);
        Assert.True(controller.End(enabled => pageEnabled = enabled, enabled => waitCursor = enabled));
        Assert.True(pageEnabled);
        Assert.False(waitCursor);
        Assert.False(controller.HasActiveBusyState);
        Assert.False(controller.End(enabled => pageEnabled = enabled, enabled => waitCursor = enabled));
    }
}
