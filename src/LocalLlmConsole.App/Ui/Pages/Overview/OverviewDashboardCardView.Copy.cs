using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public sealed partial class OverviewDashboardCardView
{
    private const string CopyGlyph = "";
    private const string CopiedGlyph = "";
    private const string CopyFailedGlyph = "";
    private static readonly TimeSpan CopyConfirmationDuration = TimeSpan.FromSeconds(1.6);
    private DispatcherTimer? _copyConfirmation;

    public WpfButton? CopyButton { get; private set; }
    public bool IsCopyConfirmationVisible => _copyConfirmation?.IsEnabled == true;

    /// <summary>
    /// Adds the card's copy action. It appears while the pointer or keyboard
    /// focus is in the card, so idle cards stay uncluttered.
    /// </summary>
    public void ConfigureCopy(Action copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        CopyButton = new WpfButton
        {
            Content = CopyGlyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 11,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(0, -4, -6, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed
        };
        ToolTipService.SetShowOnDisabled(CopyButton, true);
        AutomationProperties.SetLiveSetting(CopyButton, AutomationLiveSetting.Polite);
        ResetCopyButton();
        Grid.SetRowSpan(CopyButton, Math.Max(1, _content.RowDefinitions.Count));
        System.Windows.Controls.Panel.SetZIndex(CopyButton, 10);
        _content.Children.Add(CopyButton);
        CopyButton.Click += (_, args) =>
        {
            args.Handled = true;
            copy();
        };
        Root.MouseEnter += (_, _) => UpdateCopyVisibility();
        Root.MouseLeave += (_, _) => UpdateCopyVisibility();
        Root.IsKeyboardFocusWithinChanged += (_, _) => UpdateCopyVisibility();
    }

    public void ShowCopyResult(bool copied)
    {
        if (CopyButton is null) return;
        CopyButton.Content = copied ? CopiedGlyph : CopyFailedGlyph;
        var message = Loc.T(copied ? "EndpointInspection.Copied" : "EndpointInspection.CopyFailed");
        CopyButton.ToolTip = message;
        AutomationProperties.SetName(CopyButton, message);
        _copyConfirmation ??= CreateCopyConfirmationTimer();
        _copyConfirmation.Stop();
        _copyConfirmation.Start();
        UpdateCopyVisibility();
    }

    private DispatcherTimer CreateCopyConfirmationTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Root.Dispatcher) { Interval = CopyConfirmationDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ResetCopyButton();
            UpdateCopyVisibility();
        };
        return timer;
    }

    private void ResetCopyButton()
    {
        if (CopyButton is null) return;
        CopyButton.Content = CopyGlyph;
        CopyButton.ToolTip = Loc.T("Dashboard.CopyReadingsTooltip");
        AutomationProperties.SetName(CopyButton, Loc.T("Dashboard.CopyReadings"));
        AutomationProperties.SetHelpText(CopyButton, Loc.T("Dashboard.CopyReadingsTooltip"));
    }

    private void UpdateCopyVisibility()
    {
        if (CopyButton is null) return;
        CopyButton.Visibility = Root.IsMouseOver || Root.IsKeyboardFocusWithin || IsCopyConfirmationVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
