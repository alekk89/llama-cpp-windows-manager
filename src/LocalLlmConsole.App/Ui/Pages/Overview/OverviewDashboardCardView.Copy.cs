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
    private const double CopyChipHeight = 17;
    private static readonly TimeSpan CopyConfirmationDuration = TimeSpan.FromSeconds(1.6);
    private DispatcherTimer? _copyConfirmation;

    public WpfButton? CopyButton { get; private set; }
    public bool IsCopyConfirmationVisible => _copyConfirmation?.IsEnabled == true;

    /// <summary>
    /// Adds the card's copy action as a small chip on the card's top edge, above
    /// the title and readings, so it never covers a value and reserves no column
    /// beside them. It is drawn only while the pointer or keyboard focus is in
    /// the card.
    /// </summary>
    public void ConfigureCopy(Action copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        CopyButton = new WpfButton
        {
            Content = CopyGlyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 10,
            MinHeight = 0,
            MinWidth = 0,
            Height = CopyChipHeight,
            Padding = new Thickness(4, 0, 4, 0),
            // The chip ends just above the content; the rest sits in
            // the card padding and the gap above the card.
            Margin = new Thickness(0, -CopyChipHeight - 1, -4, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Hidden
        };
        ToolTipService.SetShowOnDisabled(CopyButton, true);
        AutomationProperties.SetLiveSetting(CopyButton, AutomationLiveSetting.Polite);
        ResetCopyButton();
        Root.ClipToBounds = false;
        System.Windows.Controls.Panel.SetZIndex(CopyButton, 1);
        InvalidateMinimumSize();
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
            : Visibility.Hidden;
    }
}
