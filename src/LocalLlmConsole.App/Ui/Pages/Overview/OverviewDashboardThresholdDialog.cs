using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

internal static class OverviewDashboardThresholdDialog
{
    /// <summary>Returns the edited threshold, or null when cancelled.</summary>
    public static OverviewDashboardMetricThreshold? Show(
        string metricName,
        OverviewDashboardThresholdRule rule,
        OverviewDashboardMetricThreshold current)
    {
        var title = Loc.T("Dashboard.ThresholdsTitle", metricName);
        OverviewDashboardMetricThreshold? result = null;
        var window = OverviewDashboardCardTitleDialog.DialogWindow(title);
        var editor = new OverviewDashboardThresholdEditor(rule, current);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(OverviewDashboardCardTitleDialog.Header(window, title));
        editor.Root.Margin = new Thickness(0, 14, 0, 14);
        Grid.SetRow(editor.Root, 1);
        root.Children.Add(editor.Root);

        var actions = new Grid { HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        var restore = new WpfButton { Content = Loc.T("Dashboard.ThresholdsRestoreDefaults"), MinHeight = 30, Padding = new Thickness(10, 0, 10, 0) };
        VisualRole.SetButtonRole(restore, VisualRole.Quiet);
        restore.Click += (_, _) => editor.RestoreDefaults();
        var cancel = new WpfButton { Content = Loc.T("Common.Cancel"), MinHeight = 30, IsCancel = true };
        var save = new WpfButton { Content = Loc.T("Common.Save"), MinHeight = 30, IsDefault = true };
        VisualRole.SetButtonRole(save, VisualRole.Primary);
        cancel.Click += (_, _) => window.DialogResult = false;
        save.Click += (_, _) =>
        {
            if (!editor.TryRead(out var threshold)) return;
            result = threshold;
            window.DialogResult = true;
        };
        actions.Children.Add(restore);
        Grid.SetColumn(cancel, 2);
        actions.Children.Add(cancel);
        Grid.SetColumn(save, 4);
        actions.Children.Add(save);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        window.Content = OverviewDashboardCardTitleDialog.Frame(root);
        window.Loaded += (_, _) => editor.Enabled.Focus();
        return window.ShowDialog() == true ? result : null;
    }
}

/// <summary>
/// The editable fields of a metric threshold. Kept separate from the modal
/// window so validation and defaults can be exercised directly.
/// </summary>
internal sealed class OverviewDashboardThresholdEditor
{
    private readonly OverviewDashboardThresholdRule _rule;

    public OverviewDashboardThresholdEditor(
        OverviewDashboardThresholdRule rule,
        OverviewDashboardMetricThreshold current)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        ArgumentNullException.ThrowIfNull(current);
        Root = new StackPanel();
        Enabled = new WpfCheckBox
        {
            Content = Loc.T("Dashboard.ThresholdsEnabled"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Root.Children.Add(Enabled);
        var below = rule.Kind == OverviewDashboardThresholdKind.Below;
        Warning = LevelBox(below ? "Dashboard.ThresholdWarningBelow" : "Dashboard.ThresholdWarningAbove");
        Critical = LevelBox(below ? "Dashboard.ThresholdCriticalBelow" : "Dashboard.ThresholdCriticalAbove");
        if (rule.Kind == OverviewDashboardThresholdKind.Increasing)
        {
            Root.Children.Add(MutedText(Loc.T("Dashboard.ThresholdIncreasingHelp",
                (int)OverviewDashboardThresholdPolicy.IncreasingWarningHold.TotalMinutes)));
        }
        else
        {
            Root.Children.Add(Field(Warning));
            Root.Children.Add(Field(Critical));
            Root.Children.Add(MutedText(Loc.T("Dashboard.ThresholdBlankHelp")));
        }
        Error = MutedText("");
        Error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        Error.Visibility = Visibility.Collapsed;
        Root.Children.Add(Error);
        Enabled.Checked += (_, _) => UpdateEnabled();
        Enabled.Unchecked += (_, _) => UpdateEnabled();
        Load(current);
    }

    public StackPanel Root { get; }
    public WpfCheckBox Enabled { get; }
    public WpfTextBox Warning { get; }
    public WpfTextBox Critical { get; }
    public TextBlock Error { get; }

    public void RestoreDefaults() => Load(_rule.Default);

    public bool TryRead(out OverviewDashboardMetricThreshold threshold)
    {
        threshold = _rule.Default;
        var enabled = Enabled.IsChecked == true;
        double? warning = null;
        double? critical = null;
        var valid = _rule.Kind == OverviewDashboardThresholdKind.Increasing
                    || (TryParse(Warning.Text, out warning) && TryParse(Critical.Text, out critical));
        var candidate = new OverviewDashboardMetricThreshold(enabled, warning, critical);
        valid &= OverviewDashboardThresholdPolicy.IsValid(_rule, candidate);
        Error.Text = valid ? "" : Loc.T("Dashboard.ThresholdInvalid");
        Error.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (valid) threshold = candidate;
        return valid;
    }

    private void Load(OverviewDashboardMetricThreshold threshold)
    {
        Enabled.IsChecked = threshold.Enabled;
        Warning.Text = Format(threshold.Warning);
        Critical.Text = Format(threshold.Critical);
        Error.Visibility = Visibility.Collapsed;
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        Warning.IsEnabled = Enabled.IsChecked == true;
        Critical.IsEnabled = Enabled.IsChecked == true;
    }

    private WpfTextBox LevelBox(string labelKey)
    {
        var box = new WpfTextBox { MinHeight = 30, MinWidth = 90, Tag = Loc.T(labelKey) };
        System.Windows.Automation.AutomationProperties.SetName(box, Loc.T(labelKey));
        box.ToolTip = Loc.T("Dashboard.ThresholdBlankHelp");
        return box;
    }

    private Grid Field(WpfTextBox box)
    {
        var field = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        field.ColumnDefinitions.Add(new ColumnDefinition());
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new System.Windows.Controls.Label
        {
            Content = (string)box.Tag,
            Target = box,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        label.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextSoft");
        field.Children.Add(label);
        Grid.SetColumn(box, 1);
        field.Children.Add(box);
        var unit = new TextBlock
        {
            Text = _rule.Unit,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        unit.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        Grid.SetColumn(unit, 2);
        field.Children.Add(unit);
        return field;
    }

    private static TextBlock MutedText(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        return block;
    }

    private static string Format(double? value)
        => value?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";

    private static bool TryParse(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            || !double.IsFinite(parsed))
            return false;
        value = parsed;
        return true;
    }
}
