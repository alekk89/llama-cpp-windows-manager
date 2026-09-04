using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public abstract partial class WpfUiTestBase
{
    protected static AppSettings AssertLaunchSettingsSurface()
    {
        var settings = AppSettings.CreateDefault(Path.Combine(Path.GetTempPath(), "wpf-smoke"));
        var panelState = new LocalLlmConsole.LaunchSettingsPanelState();
        var controlPlan = new LaunchSettingsControlStateService().Build(new LaunchSettingsControlStateRequest(
            ShowAdvancedSections: true,
            RuntimeBackend.Cpu,
            VisionLaunchSettingsAvailable: true,
            SpeculativeType: "none"));
        var panel = LocalLlmConsole.LaunchSettingsPanelFactory.Create(new LocalLlmConsole.LaunchSettingsPanelRequest(
            settings,
            [new RuntimeChoice("cpu", "Official CPU", RuntimeBackend.Cpu, RuntimeMode.Native, "llama-server.exe")],
            ShowAdvancedLaunchSettings: true,
            RuntimeSelectionChanged: () => { },
            AdvancedSettingsChanged: _ => { },
            LaunchSettingsSearchChanged: () => panelState.ApplyControlState(controlPlan),
            SaveForModelAsync: () => Task.CompletedTask,
            FitToAvailableVramAsync: () => Task.CompletedTask,
            SaveDefaultsAsync: () => Task.CompletedTask,
            ResetDefaults: () => { },
            SaveAsNewAsync: () => Task.CompletedTask,
            ChooseVisionProjectorAsync: () => Task.CompletedTask,
            ChooseDraftModelAsync: () => Task.CompletedTask,
            ChooseMtpHeadAsync: () => Task.CompletedTask,
            SaveAsNewNameChanged: () => { },
            ChooseAdditionalFile: _ => null,
            ChooseAdditionalDirectory: _ => null));
        panelState.Apply(panel);
        panel.FormControls.RuntimeOptions!.SetOptions([
            new RuntimeLaunchOptionDefinition("--cpu-mask", ["--cpu-mask"], "MASK", "CPU affinity mask", RuntimeLaunchOptionValueKind.Text, []),
            new RuntimeLaunchOptionDefinition("--numa", ["--numa"], "TYPE", "NUMA strategy", RuntimeLaunchOptionValueKind.Choice, ["distribute", "isolate"], "distribute"),
            new RuntimeLaunchOptionDefinition("--log-colors", ["--log-colors"], "", "colorize runtime logs", RuntimeLaunchOptionValueKind.Switch, []),
            new RuntimeLaunchOptionDefinition("--no-log-colors", ["--no-log-colors"], "", "disable colored runtime logs", RuntimeLaunchOptionValueKind.Switch, [])
        ]);
        panelState.ApplyControlState(controlPlan);

        Assert.Equal(28, panel.LaunchSettingsSearchBox.Height);
        var launchSettingsToolbar = Assert.IsType<Grid>(panel.FitToAvailableVramButton.Parent);
        var launchSettingsSearchHost = Assert.IsType<Grid>(panel.LaunchSettingsSearchBox.Parent);
        Assert.Same(launchSettingsToolbar, panel.AdvancedLaunchSettingsButton.Parent);
        Assert.Same(launchSettingsToolbar, launchSettingsSearchHost.Parent);
        Assert.Equal(0, Grid.GetColumn(launchSettingsSearchHost));
        Assert.Equal(1, Grid.GetColumn(panel.FitToAvailableVramButton));
        Assert.Equal(2, Grid.GetColumn(panel.AdvancedLaunchSettingsButton));
        Assert.Equal(28, panel.FitToAvailableVramButton.Height);
        Assert.True(panel.RuntimeCombo.MinHeight >= 28);
        Assert.IsType<LocalLlmConsole.SearchableComboBox>(panel.RuntimeCombo);
        Assert.False(panel.RuntimeCombo.IsEditable);
        Assert.True(panel.RuntimeCombo.StaysOpenOnEdit);
        Assert.NotNull(panel.FormControls.HostBox);
        Assert.Equal("127.0.0.1", panel.FormControls.HostBox.Text);
        panel.FormControls.HostBox.Text = "10.10.10.21";
        Assert.Equal("10.10.10.21", LocalLlmConsole.LaunchSettingsFormBinder.Read(settings, panel.FormControls).Host);
        var allocation = panel.FormControls.VulkanAllocationBlockSizeBox!;
        Assert.Equal("Runtime default", allocation.Text);
        allocation.Text = "4096";
        Assert.Equal(4096, LocalLlmConsole.LaunchSettingsFormBinder.Read(settings, panel.FormControls).VulkanAllocationBlockSizeMiB);
        allocation.Text = "-1";
        Assert.Throws<InvalidOperationException>(() => LocalLlmConsole.LaunchSettingsFormBinder.Read(settings, panel.FormControls));
        allocation.Text = "";
        Assert.Equal(0, LocalLlmConsole.LaunchSettingsFormBinder.Read(settings, panel.FormControls).VulkanAllocationBlockSizeMiB);
        allocation.Text = "Runtime default";
        Assert.All(
            panelState.LaunchSettingElements.SelectMany(pair => pair.Value),
            element =>
            {
                var tooltip = element.ToolTip?.ToString();
                Assert.False(string.IsNullOrWhiteSpace(tooltip));
                Assert.DoesNotContain("Tooltip.", tooltip, StringComparison.Ordinal);
            });
        Assert.Contains("model's folder", panel.FormControls.VisionProjectorButton!.ToolTip?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nextn_predict_layers", panel.FormControls.SpecDraftModelButton!.ToolTip?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("legacy --mtp-head", panel.FormControls.MtpHeadButton!.ToolTip?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(panel.FormControls.RuntimeOptions.ApplyCommandButton.ToolTip?.ToString()));
        Assert.Equal(3, panel.FormControls.RuntimeOptions.OptionCount);
        Assert.Contains("Performance & Memory", panel.FormControls.RuntimeOptions.GroupTitles);
        Assert.Equal(Visibility.Visible, panel.FormControls.RuntimeOptions.AdditionalSettingsRoot.Visibility);
        Assert.Equal(Visibility.Visible, panel.FormControls.RuntimeOptions.CommandRoot.Visibility);
        Assert.False(panel.FormControls.RuntimeOptions.CommandTextBox.IsReadOnly);
        Assert.DoesNotContain(
            VisualDescendants<TextBlock>(panel.FormControls.RuntimeOptions.Root),
            text => text.Text.Contains("additional settings exposed by", StringComparison.OrdinalIgnoreCase));
        var cpuMaskLabel = Assert.Single(VisualDescendants<TextBlock>(panel.FormControls.RuntimeOptions.Root), text => text.Text == "CPU Mask");
        var numaLabel = Assert.Single(VisualDescendants<TextBlock>(panel.FormControls.RuntimeOptions.Root), text => text.Text == "NUMA");
        var cpuMaskRow = Assert.IsType<Grid>(cpuMaskLabel.Parent);
        var numaRow = Assert.IsType<Grid>(numaLabel.Parent);
        var numaCombo = Assert.Single(VisualDescendants<ComboBox>(numaRow));
        Assert.Equal(["Inherit (runtime default: distribute)", "distribute", "isolate"], numaCombo.Items.Cast<string>().ToArray());
        numaCombo.SelectedItem = "isolate";
        Assert.Contains("--numa isolate", panel.FormControls.RuntimeOptions.BuildCustomParameters(), StringComparison.Ordinal);
        numaCombo.SelectedIndex = 0;
        Assert.DoesNotContain("--numa", panel.FormControls.RuntimeOptions.BuildCustomParameters(), StringComparison.Ordinal);
        Assert.Equal(104, cpuMaskRow.ColumnDefinitions[0].Width.Value);
        var cpuMaskEditor = cpuMaskRow.Children.Cast<FrameworkElement>().Single(element => Grid.GetColumn(element) == 1);
        Assert.Equal(28, cpuMaskEditor.Height);
        Assert.Equal(new Thickness(0, 0, 4, 1), cpuMaskEditor.Margin);
        Assert.Contains("--cpu-mask", cpuMaskLabel.ToolTip?.ToString(), StringComparison.Ordinal);
        var cpuMaskTextBox = Assert.Single(VisualDescendants<TextBox>(cpuMaskRow));
        Assert.Empty(cpuMaskTextBox.Text);
        Assert.DoesNotContain(VisualDescendants<TextBlock>(cpuMaskRow), text => text.Text == "Runtime default");
        var runtimeSwitch = Assert.Single(VisualDescendants<Button>(panel.FormControls.RuntimeOptions.Root), button =>
            button.ToolTip?.ToString()?.Contains("--log-colors", StringComparison.Ordinal) == true);
        Assert.Equal("Default", runtimeSwitch.Content);
        runtimeSwitch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Enabled", runtimeSwitch.Content);
        runtimeSwitch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Disabled", runtimeSwitch.Content);
        runtimeSwitch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Default", runtimeSwitch.Content);
        Assert.Equal(0, Grid.GetColumn(cpuMaskRow));
        Assert.Equal(2, Grid.GetColumn(numaRow));
        Assert.Equal(Grid.GetRow(cpuMaskRow), Grid.GetRow(numaRow));
        panel.LaunchSettingsSearchBox.Text = "context size";
        Assert.All(panelState.LaunchSettingElements["Context size"], element => Assert.Equal(Visibility.Visible, element.Visibility));
        Assert.All(panelState.LaunchSettingElements["Threads"], element => Assert.Equal(Visibility.Collapsed, element.Visibility));
        panel.LaunchSettingsSearchBox.Text = "numa";
        Assert.Equal(Visibility.Visible, panel.FormControls.RuntimeOptions.Root.Visibility);
        Assert.Equal(0, Grid.GetColumn(numaRow));
        panel.LaunchSettingsSearchBox.Text = "--cpu-mask";
        Assert.Equal(Visibility.Visible, cpuMaskRow.Visibility);
        panel.LaunchSettingsSearchBox.Text = "no-setting-can-match-this";
        Assert.Equal(Visibility.Collapsed, panel.FormControls.RuntimeOptions.AdditionalSettingsRoot.Visibility);
        Assert.Equal(Visibility.Visible, panel.FormControls.RuntimeOptions.CommandRoot.Visibility);
        panel.LaunchSettingsSearchBox.Clear();
        var basicPlan = new LaunchSettingsControlStateService().Build(new LaunchSettingsControlStateRequest(
            ShowAdvancedSections: false,
            RuntimeBackend.Cpu,
            VisionLaunchSettingsAvailable: true,
            SpeculativeType: "none"));
        panelState.ApplyControlState(basicPlan);
        Assert.Equal(Grid.GetRow(panel.FormControls.BatchSizeBox!), Grid.GetRow(panel.FormControls.MicroBatchSizeBox!));
        Assert.Equal(1, Grid.GetColumn(panel.FormControls.BatchSizeBox!));
        Assert.Equal(4, Grid.GetColumn(panel.FormControls.MicroBatchSizeBox!));
        Assert.Equal(Visibility.Collapsed, panel.FormControls.RuntimeOptions.AdditionalSettingsRoot.Visibility);
        Assert.Equal(Visibility.Visible, panel.FormControls.RuntimeOptions.CommandRoot.Visibility);
        panel.FormControls.RuntimeOptions.UpdatePreview("llama-server --model model.gguf");
        panel.FormControls.RuntimeOptions.CommandTextBox.Text += " --cpu-mask FF --future-runtime-option value";
        panel.FormControls.RuntimeOptions.ApplyCommandButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("FF", cpuMaskTextBox.Text);
        Assert.Contains("--future-runtime-option", panel.FormControls.CustomParametersBox!.Text, StringComparison.Ordinal);
        panel.FormControls.RuntimeOptions.SetLoading("Official CUDA");
        Assert.Equal(0, panel.FormControls.RuntimeOptions.OptionCount);
        Assert.Empty(panel.FormControls.RuntimeOptions.GroupTitles);
        Assert.Contains("Official CUDA", panel.FormControls.RuntimeOptions.StatusText, StringComparison.Ordinal);


        return settings;
    }
}

public sealed class WpfLaunchSettingsTests : WpfUiTestBase
{
    [Fact]
    public async Task LaunchSettingsRenderAndValidateIndependently()
        => await RunStaAsync(() => AssertLaunchSettingsSurface());
}
