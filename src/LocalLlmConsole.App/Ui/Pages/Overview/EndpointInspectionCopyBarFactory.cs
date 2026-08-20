using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

internal static class EndpointInspectionCopyBarFactory
{
    public static FrameworkElement Create(
        EndpointInspectionReport report,
        string apiKey,
        Action<string> copyToClipboard)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(copyToClipboard);

        var status = new TextBlock
        {
            Foreground = ResourceBrush("TextSoft"),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11.5,
            Margin = new Thickness(4, 0, 0, 0)
        };
        AutomationProperties.SetAutomationId(status, "EndpointCopyStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

        var buttons = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        buttons.Children.Add(CopyButton(
            "EndpointCopyEndpointButton",
            Loc.T("EndpointInspection.CopyEndpoint"),
            report.Endpoint,
            Loc.T("EndpointInspection.Copied"),
            status,
            copyToClipboard));
        var reportButton = CopyButton(
            "EndpointCopyReportButton",
            Loc.T("EndpointInspection.CopyReport"),
            EndpointInspectionReportFormatter.Format(report, !string.IsNullOrWhiteSpace(apiKey)),
            Loc.T("EndpointInspection.Copied"),
            status,
            copyToClipboard);
        reportButton.ToolTip = Loc.T("EndpointInspection.CopySafetyNote");
        buttons.Children.Add(reportButton);
        var copyApiKey = CopyButton(
            "EndpointCopyApiKeyButton",
            Loc.T("EndpointInspection.CopyApiKey"),
            apiKey,
            Loc.T("EndpointInspection.Copied"),
            status,
            copyToClipboard);
        copyApiKey.IsEnabled = !string.IsNullOrWhiteSpace(apiKey);
        copyApiKey.ToolTip = Loc.T("EndpointInspection.CopySafetyNote");
        buttons.Children.Add(copyApiKey);

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var row = new DockPanel();
        row.Children.Add(buttons);
        DockPanel.SetDock(status, Dock.Right);
        row.Children.Add(status);
        panel.Children.Add(row);
        return panel;
    }

    private static WpfButton CopyButton(
        string automationId,
        string label,
        string value,
        string copiedStatus,
        TextBlock status,
        Action<string> copyToClipboard)
    {
        var button = new WpfButton
        {
            Content = label,
            MinHeight = 27,
            Padding = new Thickness(9, 1, 9, 1),
            Margin = new Thickness(0, 0, 5, 0)
        };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            try
            {
                copyToClipboard(value);
                status.Text = copiedStatus;
            }
            catch
            {
                status.Text = Loc.T("EndpointInspection.CopyFailed");
            }
        };
        return button;
    }

    private static WpfBrush ResourceBrush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];
}
