using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class WpfRuntimeHostAndProjectorTests : WpfUiTestBase
{
    [Fact]
    public async Task CommandPanelExplainsEffectiveHostAndUpdatesAfterLanPolicyChanges()
        => await RunStaAsync(() =>
        {
            var panel = new LaunchRuntimeOptionsPanel(new TextBox(), _ => null, _ => null);
            var settings = AppSettings.CreateDefault(TestWorkspace) with { Host = "10.10.10.21" };
            panel.UpdateHostStatus(settings);

            Assert.Contains("127.0.0.1", panel.HostStatusText, StringComparison.Ordinal);
            Assert.Contains("Settings > LAN exposure", panel.HostStatusText, StringComparison.Ordinal);
            Assert.Contains(VisualDescendants<TextBlock>(panel.CommandRoot), text =>
                text.Text == panel.HostStatusText && text.Visibility == Visibility.Visible);

            panel.UpdateHostStatus(settings with { ModelAccessMode = "models" });
            Assert.Equal("Server listens on 10.10.10.21.", panel.HostStatusText);
        });

    [Fact]
    public async Task AdvertisedProjectorOffloadSwitchImportsAndCyclesWithoutLosingSavedValue()
        => await RunStaAsync(() =>
        {
            var raw = new TextBox { Text = "--no-mmproj-offload" };
            var panel = new LaunchRuntimeOptionsPanel(raw, _ => null, _ => null);
            var options = RuntimeLaunchHelpParser.Parse("""
                  --mmproj-offload, --no-mmproj-offload
                                                    whether to enable GPU offloading for multimodal projector (default: enabled)
                """).Where(RuntimeLaunchOptionPolicy.CanRender).ToArray();
            panel.SetOptions(options);

            Assert.Equal(1, panel.OptionCount);
            Assert.Equal("", raw.Text);
            var toggle = Assert.Single(VisualDescendants<Button>(panel.AdditionalSettingsRoot));
            Assert.Equal("Disabled", toggle.Content);
            Assert.Equal("--no-mmproj-offload", panel.BuildCustomParameters());
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Default", toggle.Content);
            Assert.Equal("", panel.BuildCustomParameters());
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Enabled", toggle.Content);
            Assert.Equal("--mmproj-offload", panel.BuildCustomParameters());

            panel.SetLoading("Another runtime");
            Assert.Equal("--mmproj-offload", raw.Text);
            panel.SetOptions(options);
            Assert.Equal("--mmproj-offload", panel.BuildCustomParameters());
        });
}
