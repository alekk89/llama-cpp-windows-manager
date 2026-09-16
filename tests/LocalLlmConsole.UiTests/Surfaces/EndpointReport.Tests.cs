using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Services;
using LocalLlmConsole.Localization;

namespace LocalLlmConsole.UiTests;

public sealed class WpfEndpointReportTests : WpfUiTestBase
{
    [Theory]
    [InlineData(620)]
    [InlineData(760)]
    public async Task CompactReportShowsAllocatedContextAndOnlyReportedSettings(double width)
        => await RunStaAsync(() =>
        {
            var owner = new Window();
            var report = Report() with
            {
                Defaults = new("model.gguf", 32768, 2, -1, true, "", "", "", .7, null, .95, null, "build", false, new Dictionary<string, bool>()),
                Slots = [new(0, true, 32768, -1, 12, null, "", null, null, null, null, null),
                    new(1, false, 32768, -1, null, null, "", null, null, null, null, null)]
            };
            var dialog = EndpointInspectionDialogFactory.Create(owner, report, "private-key");
            try
            {
                var content = Layout(dialog, width);
                Assert.Contains(VisualDescendants<TextBlock>(content), text => text.Text == "32,768 tokens");
                var id = Assert.Single(VisualDescendants<TextBox>(content), text =>
                    AutomationProperties.GetAutomationId(text) == "EndpointModelIdText");
                Assert.Equal("qwen-27b:2", id.Text);
                Assert.Equal(TextWrapping.NoWrap, id.TextWrapping);
                Assert.DoesNotContain(VisualDescendants<TextBox>(content), text => text.IsVisible && text.Text == "Qwen 27B");
                Assert.InRange(id.ActualHeight, 1, 25);
                var expand = Assert.Single(VisualDescendants<Button>(content), button =>
                    AutomationProperties.GetAutomationId(button) == "EndpointModelDetailsButton");
                expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                content.UpdateLayout();
                var details = Assert.Single(VisualDescendants<TextBox>(content), text =>
                    AutomationProperties.GetAutomationId(text) == "EndpointModelDetailsText");
                Assert.Contains("Train ctx: 131,072 tokens", details.Text, StringComparison.Ordinal);
                Assert.Contains(VisualDescendants<TextBlock>(content), text => text.Text == "14.9 GB");
                Assert.DoesNotContain(VisualDescendants<TextBox>(content), text => text.Text.Contains("private-key", StringComparison.Ordinal));
                Assert.Contains(VisualDescendants<TextBox>(content), text => text.Text == "1 active / 2 total");
                Assert.DoesNotContain(VisualDescendants<TextBlock>(content), text => text.Text == Loc.T("EndpointInspection.TopK"));
                Assert.DoesNotContain(VisualDescendants<TextBlock>(content), text => text.Text == Loc.T("EndpointInspection.Build"));
                Assert.True(details.ActualHeight > 0);
                Assert.Equal(Loc.T("EndpointInspection.CollapseDetails"), AutomationProperties.GetName(expand));
                expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Loc.T("EndpointInspection.ExpandDetails"), AutomationProperties.GetName(expand));
                content.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                content.UpdateLayout();
                var table = Assert.Single(VisualDescendants<DataGrid>(content));
                Assert.True(table.ActualWidth <= width - 24);
                Assert.DoesNotContain(VisualDescendants<ScrollViewer>(table), scroll => scroll.ScrollableWidth > 1);
            }
            finally { dialog.Close(); owner.Close(); }
        });

    [Fact]
    public async Task MissingKeyHidesCopyActionAndPartialDetailsRemainExpandable()
        => await RunStaAsync(() =>
        {
            var owner = new Window();
            var copied = new List<string>();
            var dialog = EndpointInspectionDialogFactory.Create(owner, Report() with
            {
                Health = "Unavailable: connection refused",
                UnavailableSources = ["/props: HTTP 404"]
            }, copyToClipboard: copied.Add);
            try
            {
                var content = Layout(dialog, 620);
                var key = Assert.Single(VisualDescendants<Button>(content), button =>
                    AutomationProperties.GetAutomationId(button) == "EndpointCopyApiKeyButton");
                Assert.False(key.IsEnabled);
                Assert.Equal(Visibility.Collapsed, key.Visibility);
                var details = Assert.Single(VisualDescendants<Expander>(content));
                Assert.False(details.IsExpanded);
                details.IsExpanded = true;
                content.UpdateLayout();
                Assert.Equal("/props: HTTP 404", Assert.IsType<TextBox>(details.Content).Text);
                var endpoint = Assert.Single(VisualDescendants<Button>(content), button =>
                    AutomationProperties.GetAutomationId(button) == "EndpointCopyEndpointButton");
                endpoint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Report().Endpoint, Assert.Single(copied));
            }
            finally { dialog.Close(); owner.Close(); }
        });

    [Fact]
    public async Task GatewayReportCopiesLanEndpointAndOffersReversibleFirewallAction()
        => await RunStaAsync(() =>
        {
            var owner = new Window();
            var copied = new List<string>();
            var installCalls = 0;
            var removeCalls = 0;
            var report = new EndpointInspectionReport(
                EndpointInspectionKind.Gateway,
                "Shared gateway",
                "http://127.0.0.1:8080/v1",
                "Healthy",
                DateTimeOffset.UtcNow,
                [], null, [], [], "Keep loaded", "Gateway LAN", [])
            {
                GatewayNetwork = new EndpointInspectionGatewayNetwork(
                    true,
                    "http://127.0.0.1:8080/v1",
                    "http://192.168.1.20:8080/v1",
                    "http://+:8080/",
                    "missing",
                    "",
                    "not_tested")
            };
            var actions = new EndpointInspectionGatewayActions(
                () =>
                {
                    installCalls++;
                    return Task.FromResult(new GatewayFirewallRuleOperationResult(
                        true, false, new GatewayFirewallRuleState(GatewayFirewallRuleStatus.Installed, 8080)));
                },
                () =>
                {
                    removeCalls++;
                    return Task.FromResult(new GatewayFirewallRuleOperationResult(
                        true, false, new GatewayFirewallRuleState(GatewayFirewallRuleStatus.Missing, 8080)));
                });
            var dialog = EndpointInspectionDialogFactory.Create(owner, report, copyToClipboard: copied.Add, gatewayActions: actions);
            try
            {
                var content = Layout(dialog, 760);
                var lanCopy = Assert.Single(VisualDescendants<Button>(content), button =>
                    AutomationProperties.GetAutomationId(button) == "EndpointCopyLanEndpointButton");
                lanCopy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("http://192.168.1.20:8080/v1", Assert.Single(copied));

                var status = Assert.Single(VisualDescendants<TextBlock>(content), text =>
                    AutomationProperties.GetAutomationId(text) == "EndpointFirewallStatus");
                var install = Assert.Single(VisualDescendants<Button>(content), button =>
                    AutomationProperties.GetAutomationId(button) == "EndpointInstallFirewallButton");
                var remove = Assert.Single(VisualDescendants<Button>(content), button =>
                    AutomationProperties.GetAutomationId(button) == "EndpointRemoveFirewallButton");
                Assert.Equal(Visibility.Visible, install.Visibility);
                Assert.Equal(Visibility.Collapsed, remove.Visibility);
                install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, installCalls);
                Assert.Equal(Loc.T("EndpointInspection.FirewallInstalled"), status.Text);
                Assert.Equal(Visibility.Collapsed, install.Visibility);
                Assert.Equal(Visibility.Visible, remove.Visibility);

                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, removeCalls);
                Assert.Equal(Loc.T("EndpointInspection.FirewallMissing"), status.Text);
                Assert.Equal(Visibility.Visible, install.Visibility);
                Assert.Equal(Visibility.Collapsed, remove.Visibility);
            }
            finally { dialog.Close(); owner.Close(); }
        });

    private static FrameworkElement Layout(Window dialog, double width)
    {
        var content = Assert.IsAssignableFrom<FrameworkElement>(dialog.Content);
        content.Measure(new Size(width, 480));
        content.Arrange(new Rect(0, 0, width, 480));
        content.UpdateLayout();
        content.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        content.UpdateLayout();
        return content;
    }

    private static EndpointInspectionReport Report()
        => new(EndpointInspectionKind.DirectModel, "Qwen", "http://127.0.0.1:8082/v1", "Ready",
            DateTimeOffset.UtcNow, [new("qwen-27b:2", "Qwen 27B", "llama.cpp", "CUDA", null, null, 131072, 27_000_000_000, 16_000_000_000)],
            null, [], [], "", "", []);
}
