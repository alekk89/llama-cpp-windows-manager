using System.Windows;
using System.Windows.Controls;

namespace LocalLlmConsole;

/// <summary>A metric's warning or critical state and its announced text.</summary>
public sealed record OverviewDashboardMetricAlert(OverviewDashboardAlertLevel Level, string Status);

public sealed partial class OverviewDashboardMetricRowView
{
    internal static readonly string WarningGlyph = char.ConvertFromUtf32(0xE7BA);
    internal static readonly string CriticalGlyph = char.ConvertFromUtf32(0xEA39);
    private readonly TextBlock _alertGlyph;
    private bool _valueUnavailable;
    private OverviewDashboardMetricAlert? _alert;

    public OverviewDashboardAlertLevel AlertLevel => EffectiveAlert?.Level ?? OverviewDashboardAlertLevel.Normal;

    /// <summary>The alert only applies while the row shows a real value.</summary>
    private OverviewDashboardMetricAlert? EffectiveAlert
        => IsAvailable && !_valueUnavailable && _alert is { Level: not OverviewDashboardAlertLevel.Normal } alert
            ? alert
            : null;

    /// <summary>
    /// Shows a warning or critical state with a theme brush, an icon, and the
    /// accessible item status, so the state never relies on colour alone.
    /// </summary>
    public bool SetAlert(OverviewDashboardMetricAlert? alert)
    {
        if (alert is { Level: OverviewDashboardAlertLevel.Normal })
            alert = null;
        if (alert == _alert) return false;
        _alert = alert;
        UpdateAlertVisual();
        UpdateItemStatus();
        return true;
    }

    private void UpdateAlertVisual()
    {
        var alert = EffectiveAlert;
        var brush = alert?.Level switch
        {
            OverviewDashboardAlertLevel.Critical => "Danger",
            OverviewDashboardAlertLevel.Warning => "Warning",
            _ => _valueUnavailable || !IsAvailable ? "TextMuted" : "TextMain"
        };
        _value.SetResourceReference(TextBlock.ForegroundProperty, brush);
        if (alert is null)
        {
            _alertGlyph.Visibility = Visibility.Collapsed;
            _alertGlyph.ToolTip = null;
            return;
        }
        _alertGlyph.Text = alert.Level == OverviewDashboardAlertLevel.Critical ? CriticalGlyph : WarningGlyph;
        _alertGlyph.SetResourceReference(TextBlock.ForegroundProperty, brush);
        _alertGlyph.ToolTip = alert.Status;
        _alertGlyph.Visibility = Visibility.Visible;
    }

    private static TextBlock AlertGlyph()
        => new()
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            Margin = new Thickness(5, 0, 0, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
}
