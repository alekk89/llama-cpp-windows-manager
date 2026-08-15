using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public static partial class LaunchSettingsPanelFactory
{
    private static LaunchSettingsFormControls AddLaunchSections(
        StackPanel panel,
        LaunchSettingsPanelBuilder builder,
        LaunchSettingsPanelRequest request,
        WpfTextBox launchPortBox)
    {
        var editors = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
        {
            [nameof(AppSettings.Port)] = launchPortBox
        };

        foreach (var sectionGroup in LaunchSettingUiSchema.Definitions.GroupBy(definition => definition.SectionKey))
        {
            var grid = LaunchSettingsGrid();
            foreach (var definition in sectionGroup)
            {
                var control = CreateSchemaEditor(definition, request, editors);
                var label = Loc.T(definition.LabelKey);
                var tooltip = Loc.T(CurrentTooltipKey(definition));
                if (definition.Advanced)
                    builder.AddAdvancedLaunchSetting(grid, label, control, tooltip);
                else
                    builder.AddLaunchSetting(grid, label, control, tooltip);
            }

            var advancedSection = sectionGroup.Any(definition => definition.AdvancedSection);
            AddLaunchSection(panel, builder, Loc.T(sectionGroup.Key), grid, advancedSection);
        }

        var rawParameters = (WpfTextBox)editors[nameof(AppSettings.CustomParameters)];
        var runtimeOptions = new LaunchRuntimeOptionsPanel(
            rawParameters,
            request.ChooseAdditionalFile,
            request.ChooseAdditionalDirectory);
        panel.Children.Add(runtimeOptions.Root);
        return new LaunchSettingsFormControls(editors, runtimeOptions);
    }

    private static string CurrentTooltipKey(LaunchSettingUiDefinition definition)
        => definition.LabelKey switch
        {
            "Launch.Field.Vision" => "Tooltip.Current.Vision",
            "Launch.Field.VisionHead" => "Tooltip.Current.VisionHead",
            "Launch.Field.SpecType" => "Tooltip.Current.SpecType",
            "Launch.Field.DraftModel" => "Tooltip.Current.DraftModel",
            "Launch.Field.MtpHead" => "Tooltip.Current.MtpHead",
            _ => definition.LabelKey.Replace("Launch.Field.", "Tooltip.Field.", StringComparison.Ordinal)
        };

    private static FrameworkElement CreateSchemaEditor(
        LaunchSettingUiDefinition definition,
        LaunchSettingsPanelRequest request,
        IDictionary<string, FrameworkElement> editors)
    {
        var value = ReadSettingValue(request.Settings, definition.Id);
        FrameworkElement editor;
        if (definition.Editor == LaunchSettingEditorKind.Choice)
        {
            editor = LaunchCombo(definition.Choices ?? Array.Empty<string>());
        }
        else
        {
            var textBox = LaunchTextBox(value);
            editor = definition.Editor switch
            {
                LaunchSettingEditorKind.VisionProjector => RegisterPicker(
                    definition.Id, textBox, VisionProjectorPicker(textBox, request.ChooseVisionProjectorAsync, out var button), button, editors),
                LaunchSettingEditorKind.DraftModel => RegisterPicker(
                    definition.Id, textBox, DraftModelPicker(textBox, request.ChooseDraftModelAsync, out var button), button, editors),
                LaunchSettingEditorKind.MtpHead => RegisterPicker(
                    definition.Id, textBox, MtpHeadPicker(textBox, request.ChooseMtpHeadAsync, out var button), button, editors),
                _ => textBox
            };
        }

        editors[definition.Id] = editor is Grid picker
            ? picker.Children.OfType<WpfTextBox>().First()
            : editor;
        return editor;
    }

    private static FrameworkElement RegisterPicker(
        string id,
        WpfTextBox textBox,
        FrameworkElement picker,
        WpfButton button,
        IDictionary<string, FrameworkElement> editors)
    {
        editors[id] = textBox;
        editors[id + ".button"] = button;
        return picker;
    }

    private static string ReadSettingValue(AppSettings settings, string propertyName)
    {
        var property = typeof(AppSettings).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Unknown launch setting '{propertyName}'.");
        var value = property.GetValue(settings);
        return value switch
        {
            bool flag => flag ? "on" : "off",
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? ""
        };
    }

    private static void AddLaunchSection(
        StackPanel panel,
        LaunchSettingsPanelBuilder builder,
        string title,
        Grid grid,
        bool isAdvancedSection = false)
    {
        var section = LaunchSection(title, grid);
        builder.AddSection(title, section, grid, isAdvancedSection);
        if (isAdvancedSection)
            builder.AddAdvancedSection(section);
        panel.Children.Add(section);
    }
}
