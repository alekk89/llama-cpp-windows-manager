using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private static WpfComboBox LaunchCombo(params string[] values) => LaunchCombo((IEnumerable<string>)values);

    private static WpfComboBox LaunchCombo(IEnumerable<string> values) => new()
    {
        ItemsSource = values.ToArray(),
        SelectedIndex = 0,
        MinHeight = 27,
        MinWidth = 76,
        Margin = new Thickness(0, 0, 6, 4),
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
    };

    private static string TooltipText(string text) => text;

    private static string ComboValue(WpfComboBox? combo)
        => (combo?.SelectedItem?.ToString() ?? combo?.Text ?? "").Trim().ToLowerInvariant();

    private void NormalizeContextSizeBox()
    {
        var contextSizeBox = _launchSettingsPanel.FormControls.ContextSizeBox;
        if (contextSizeBox is null) return;
        var text = contextSizeBox.Text.Trim();
        if (!LaunchSettingParser.TryNormalizeContextSize(text, out var value)) return;
        var normalized = value.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(text, normalized, StringComparison.Ordinal))
            contextSizeBox.Text = normalized;
        UpdateContextSizeSuggestion();
    }
    private void UpdateContextSizeSuggestion()
    {
        var contextSizeBox = _launchSettingsPanel.FormControls.ContextSizeBox;
        if (contextSizeBox is null) return;
        var text = contextSizeBox.Text.Trim();
        contextSizeBox.ToolTip = TooltipText(LaunchSettingMetadataService.ContextSizeTooltip(text));
    }
}
