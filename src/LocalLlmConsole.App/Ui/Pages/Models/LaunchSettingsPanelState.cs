using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed class LaunchSettingsPanelState
{
    public WpfComboBox? RuntimeCombo { get; private set; }

    public TextBlock? ModelCapabilityText { get; private set; }

    public WpfTextBox? LaunchSettingsSearchBox { get; private set; }

    public WpfButton? AdvancedLaunchSettingsButton { get; private set; }

    public LaunchSettingsFormControls FormControls { get; private set; } = new();

    public Dictionary<string, List<FrameworkElement>> LaunchSettingElements { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> AdvancedLaunchSettingLabels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<LaunchSettingsSectionElements> LaunchSettingSections { get; } = [];

    public List<FrameworkElement> AdvancedLaunchSections { get; } = [];

    private WpfButton? SaveModelLaunchSettingsButton { get; set; }

    private WpfTextBox? SaveAsNewModelNameBox { get; set; }

    private WpfButton? SaveAsNewModelButton { get; set; }

    public string SaveAsNewModelName => (SaveAsNewModelNameBox?.Text ?? "").Trim();

    public void Apply(LaunchSettingsPanelControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        LaunchSettingElements.Clear();
        foreach (var (key, value) in controls.LaunchSettingElements)
            LaunchSettingElements[key] = value;

        AdvancedLaunchSettingLabels.Clear();
        foreach (var label in controls.AdvancedLaunchSettingLabels)
            AdvancedLaunchSettingLabels.Add(label);

        LaunchSettingSections.Clear();
        LaunchSettingSections.AddRange(controls.LaunchSettingSections);

        AdvancedLaunchSections.Clear();
        AdvancedLaunchSections.AddRange(controls.AdvancedLaunchSections);

        RuntimeCombo = controls.RuntimeCombo;
        ModelCapabilityText = controls.ModelCapabilityText;
        LaunchSettingsSearchBox = controls.LaunchSettingsSearchBox;
        AdvancedLaunchSettingsButton = controls.AdvancedLaunchSettingsButton;
        SaveModelLaunchSettingsButton = controls.SaveModelLaunchSettingsButton;
        SaveAsNewModelNameBox = controls.SaveAsNewModelNameBox;
        SaveAsNewModelButton = controls.SaveAsNewModelButton;
        FormControls = controls.FormControls;
    }

    public void SetSaveForModelState(string content, bool enabled, bool visible)
    {
        if (SaveModelLaunchSettingsButton is null) return;

        SaveModelLaunchSettingsButton.Content = content;
        SaveModelLaunchSettingsButton.IsEnabled = enabled;
        SaveModelLaunchSettingsButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SaveModelLaunchSettingsButton.ToolTip = Loc.T("Launch.SaveProfileTooltip");
    }

    public void SetSaveAsNewModelName(string name)
    {
        if (SaveAsNewModelNameBox is not null)
            SaveAsNewModelNameBox.Text = name ?? "";
    }

    public void SetSaveAsNewEnabled(bool enabled)
    {
        if (SaveAsNewModelButton is not null)
            SaveAsNewModelButton.IsEnabled = enabled;
    }

    public void ApplyControlState(LaunchSettingsControlStatePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var search = LaunchSettingsSearch.From(LaunchSettingsSearchBox?.Text);
        ApplyLaunchSettingVisibility(plan, search);
        ApplyLaunchSectionVisibility(plan, search);
        ApplyLaunchSettingEnabled(plan.EnabledSettings);
        FormControls.RuntimeOptions?.ApplyVisibility(plan.ShowAdvancedSections, LaunchSettingsSearchBox?.Text);

        if (FormControls.GpuLayersBox is not null)
            FormControls.GpuLayersBox.IsEnabled = plan.GpuLayersAvailable;
        if (FormControls.GpuModeCombo is not null)
            FormControls.GpuModeCombo.IsEnabled = plan.GpuLayersAvailable;
        if (FormControls.GpuDevicesBox is not null)
            FormControls.GpuDevicesBox.IsEnabled = plan.GpuLayersAvailable;
        if (FormControls.GpuSplitBox is not null)
            FormControls.GpuSplitBox.IsEnabled = plan.GpuLayersAvailable;
        if (FormControls.VisionCombo is not null)
            FormControls.VisionCombo.IsEnabled = plan.VisionLaunchSettingsAvailable;
        if (FormControls.VisionProjectorPathBox is not null)
            FormControls.VisionProjectorPathBox.IsEnabled = plan.VisionLaunchSettingsAvailable;
        if (FormControls.VisionProjectorButton is not null)
            FormControls.VisionProjectorButton.IsEnabled = plan.VisionLaunchSettingsAvailable;
        if (FormControls.VisionImageMinTokensBox is not null)
            FormControls.VisionImageMinTokensBox.IsEnabled = plan.VisionLaunchSettingsAvailable;
        if (FormControls.VisionImageMaxTokensBox is not null)
            FormControls.VisionImageMaxTokensBox.IsEnabled = plan.VisionLaunchSettingsAvailable;
        if (FormControls.MtpHeadPathBox is not null)
            FormControls.MtpHeadPathBox.IsEnabled = plan.MtpHeadSettingsAvailable;
        if (FormControls.MtpHeadButton is not null)
            FormControls.MtpHeadButton.IsEnabled = plan.MtpHeadSettingsAvailable;
    }

    private void ApplyLaunchSettingVisibility(LaunchSettingsControlStatePlan plan, LaunchSettingsSearch search)
    {
        foreach (var label in LaunchSettingElements.Keys)
            SetLaunchSettingVisibility(label, LaunchSettingVisible(label, plan, search));
    }

    private void SetLaunchSettingVisibility(string label, bool visible)
    {
        if (!LaunchSettingElements.TryGetValue(label, out var elements)) return;
        foreach (var element in elements)
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool LaunchSettingVisible(string label, LaunchSettingsControlStatePlan plan, LaunchSettingsSearch search)
    {
        var baseVisible = !plan.VisibleSettings.TryGetValue(label, out var visible) || visible;
        if (!baseVisible) return false;

        var advancedSetting = AdvancedLaunchSettingLabels.Contains(label);
        if (advancedSetting && !plan.ShowAdvancedSections && !search.HasQuery) return false;

        return !search.HasQuery || search.Matches(SearchTextFor(label));
    }

    private void ApplyLaunchSectionVisibility(LaunchSettingsControlStatePlan plan, LaunchSettingsSearch search)
    {
        foreach (var section in LaunchSettingSections)
        {
            var hiddenByAdvanced = section.IsAdvancedSection && !plan.ShowAdvancedSections && !search.HasQuery;
            var hasVisibleSetting = section.SettingLabels.Any(LaunchSettingCurrentlyVisible);
            section.Section.Visibility = !hiddenByAdvanced && hasVisibleSetting ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private bool LaunchSettingCurrentlyVisible(string label)
        => LaunchSettingElements.TryGetValue(label, out var elements)
           && elements.Any(element => element.Visibility == Visibility.Visible);

    private string SearchTextFor(string label)
    {
        var section = LaunchSettingSections.FirstOrDefault(candidate =>
            candidate.SettingLabels.Contains(label, StringComparer.OrdinalIgnoreCase));
        return $"{label} {section?.Title ?? ""} {LaunchSettingMetadataService.Tooltip(label)}";
    }

    private void ApplyLaunchSettingEnabled(IReadOnlyDictionary<string, bool> enabledSettings)
    {
        foreach (var (label, enabled) in enabledSettings)
            SetLaunchSettingEnabled(label, enabled);
    }

    private void SetLaunchSettingEnabled(string label, bool enabled)
    {
        if (!LaunchSettingElements.TryGetValue(label, out var elements)) return;
        foreach (var element in elements)
            element.IsEnabled = enabled;
    }

    private readonly record struct LaunchSettingsSearch(string[] Terms)
    {
        public bool HasQuery => Terms.Length > 0;

        public static LaunchSettingsSearch From(string? query)
        {
            var terms = (query ?? "")
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new LaunchSettingsSearch(terms);
        }

        public bool Matches(string text)
            => Terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
