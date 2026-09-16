using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace LocalLlmConsole;

public static partial class EndpointInspectionDialogFactory
{
    private static Border GatewayNetworkCard(
        EndpointInspectionGatewayNetwork network,
        EndpointInspectionGatewayActions? actions)
    {
        var content = new StackPanel();
        content.Children.Add(FieldsGrid(
            (Loc.T("EndpointInspection.ListenerPrefix"), network.ListenerPrefix),
            (Loc.T("EndpointInspection.LanVerification"),
                EndpointInspectionReportFormatter.LanVerification(network.LanVerification))));

        var firewallStatus = new TextBlock
        {
            Text = WithDetail(
                EndpointInspectionReportFormatter.FirewallStatus(network.FirewallStatus),
                network.FirewallDetail),
            Foreground = ResourceBrush(network.FirewallStatus == "installed" ? "TextMain" : "Warning"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 2, 2, 1)
        };
        AutomationProperties.SetAutomationId(firewallStatus, "EndpointFirewallStatus");
        AutomationProperties.SetLiveSetting(firewallStatus, AutomationLiveSetting.Polite);
        content.Children.Add(firewallStatus);
        content.Children.Add(new TextBlock
        {
            Text = Loc.T("EndpointInspection.FirewallScope"),
            Foreground = ResourceBrush("TextSoft"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Margin = new Thickness(2, 1, 2, 5)
        });

        if (actions is not null)
        {
            var buttons = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Left,
                Margin = new Thickness(0, 3, 0, 0)
            };
            var install = Button(Loc.T("EndpointInspection.AllowFirewall"));
            var remove = Button(Loc.T("EndpointInspection.RemoveFirewall"));
            AutomationProperties.SetAutomationId(install, "EndpointInstallFirewallButton");
            AutomationProperties.SetAutomationId(remove, "EndpointRemoveFirewallButton");
            VisualRole.SetButtonRole(install, VisualRole.Primary);
            VisualRole.SetButtonRole(remove, VisualRole.Danger);
            buttons.Children.Add(install);
            buttons.Children.Add(remove);
            content.Children.Add(buttons);

            SetButtonVisibility(network.FirewallStatus);
            install.Click += async (_, _) => await ApplyAsync(actions.InstallFirewallRuleAsync, installing: true);
            remove.Click += async (_, _) => await ApplyAsync(actions.RemoveFirewallRuleAsync, installing: false);

            async Task ApplyAsync(
                Func<Task<GatewayFirewallRuleOperationResult>> operation,
                bool installing)
            {
                install.IsEnabled = false;
                remove.IsEnabled = false;
                firewallStatus.Text = Loc.T("EndpointInspection.FirewallWorking");
                firewallStatus.Foreground = ResourceBrush("TextSoft");
                try
                {
                    var result = await operation();
                    if (result.Success)
                    {
                        firewallStatus.Text = EndpointInspectionReportFormatter.FirewallStatus(result.State.StatusCode);
                        firewallStatus.Foreground = ResourceBrush(
                            result.State.StatusCode == "installed" ? "TextMain" : "Warning");
                        SetButtonVisibility(result.State.StatusCode);
                    }
                    else
                    {
                        var failure = result.Cancelled
                            ? Loc.T("EndpointInspection.FirewallCancelled")
                            : Loc.T(installing
                                ? "EndpointInspection.FirewallInstallFailed"
                                : "EndpointInspection.FirewallRemoveFailed");
                        firewallStatus.Text = WithDetail(failure, result.Error);
                        firewallStatus.Foreground = ResourceBrush("Danger");
                        SetButtonVisibility(result.State.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    firewallStatus.Text = Loc.T(installing
                        ? "EndpointInspection.FirewallInstallFailed"
                        : "EndpointInspection.FirewallRemoveFailed") + " " + ex.Message;
                    firewallStatus.Foreground = ResourceBrush("Danger");
                }
                finally
                {
                    install.IsEnabled = true;
                    remove.IsEnabled = true;
                }
            }

            void SetButtonVisibility(string status)
            {
                var installed = status == "installed";
                install.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
                remove.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        return Card(Loc.T("EndpointInspection.LanReadiness"), content);
    }

    private static string WithDetail(string message, string detail)
        => string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail.Trim()}";
}
