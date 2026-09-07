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
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(4, 0, 0, 0)
        };
        AutomationProperties.SetAutomationId(status, "EndpointCopyStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions.Add(new ColumnDefinition());
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copyEndpoint = CopyButton("EndpointCopyEndpointButton", Loc.T("EndpointInspection.CopyEndpoint"),
            report.Endpoint, Loc.T("EndpointInspection.Copied"), status, copyToClipboard);
        copyEndpoint.IsEnabled = !string.IsNullOrWhiteSpace(report.Endpoint);
        AddRow(Loc.T("EndpointInspection.Endpoint"), report.Endpoint, copyEndpoint, leftToRight: true);
        var configured = !string.IsNullOrWhiteSpace(apiKey);
        var copyKey = CopyButton("EndpointCopyApiKeyButton", Loc.T("EndpointInspection.CopyApiKey"),
            apiKey, Loc.T("EndpointInspection.Copied"), status, copyToClipboard);
        copyKey.IsEnabled = configured;
        copyKey.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        copyKey.ToolTip = Loc.T("EndpointInspection.CopySafetyNote");
        AddRow(Loc.T("EndpointInspection.Authentication"), Loc.T(configured
            ? "EndpointInspection.ApiKeyConfigured" : "EndpointInspection.ApiKeyMissing"), copyKey);
        var copyReport = CopyButton("EndpointCopyReportButton", Loc.T("EndpointInspection.CopyReport"),
            EndpointInspectionReportFormatter.Format(report, configured), Loc.T("EndpointInspection.Copied"), status, copyToClipboard);
        copyReport.ToolTip = Loc.T("EndpointInspection.CopySafetyNote");
        var health = AddRow(Loc.T("Overview.SessionsCol.State"), report.Health, copyReport);
        health.FontWeight = FontWeights.SemiBold;
        health.ToolTip = Loc.T("EndpointInspection.Inspected") + ": " + report.InspectedAt.ToLocalTime().ToString("g");
        // Reachability alone does not prove readiness (e.g. a loading server).
        health.Foreground = ResourceBrush(report.IsReachable ? "TextMain" : "Danger");
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(status, 3);
        Grid.SetColumnSpan(status, 3);
        table.Children.Add(status);
        return new Border
        {
            Background = ResourceBrush("SurfaceRaised"),
            BorderBrush = ResourceBrush("PanelBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 4, 0, 8),
            Child = table
        };

        System.Windows.Controls.TextBox AddRow(string label, string value, WpfButton action, bool leftToRight = false)
        {
            var row = table.RowDefinitions.Count;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 33 });
            var stripe = new Border
            {
                Background = ResourceBrush(row % 2 == 0 ? "GridRowBack" : "GridRowAlt"),
                BorderBrush = ResourceBrush("PanelBorder"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            Grid.SetRow(stripe, row);
            Grid.SetColumnSpan(stripe, 3);
            table.Children.Add(stripe);
            var caption = new TextBlock
            {
                Text = label,
                Foreground = ResourceBrush("TextMuted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 4, 16, 4)
            };
            Grid.SetRow(caption, row);
            table.Children.Add(caption);
            var text = new System.Windows.Controls.TextBox
            {
                Text = value,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)WpfApplication.Current.Resources["EndpointReportSelectableText"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 12, 4)
            };
            if (leftToRight) text.FlowDirection = System.Windows.FlowDirection.LeftToRight;
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            table.Children.Add(text);
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(action, row);
            Grid.SetColumn(action, 2);
            table.Children.Add(action);
            return text;
        }
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
                status.Visibility = Visibility.Visible;
            }
            catch
            {
                status.Text = Loc.T("EndpointInspection.CopyFailed");
                status.Visibility = Visibility.Visible;
            }
        };
        return button;
    }

    private static WpfBrush ResourceBrush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];
}
