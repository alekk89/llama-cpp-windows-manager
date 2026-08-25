using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public static class SettingsGridColumnFactory
{
    public const int TextInputCommitDelayMilliseconds = 1000;

    public static DataGridTemplateColumn ValueColumn(
        RoutedEventHandler revealClick,
        RoutedEventHandler copyClick,
        RoutedEventHandler primaryClick)
    {
        var root = new FrameworkElementFactory(typeof(DockPanel));
        root.SetBinding(FrameworkElement.ToolTipProperty, new WpfBinding(nameof(EditableSettingRow.ToolTip)));

        var actions = new FrameworkElementFactory(typeof(WrapPanel));
        actions.SetValue(DockPanel.DockProperty, Dock.Right);
        actions.SetValue(WrapPanel.OrientationProperty, System.Windows.Controls.Orientation.Horizontal);
        actions.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        actions.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 0, 2, 0));
        var actionsStyle = new Style(typeof(WrapPanel));
        actionsStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        var noPrimaryAction = new DataTrigger
        {
            Binding = new WpfBinding(nameof(EditableSettingRow.Action)),
            Value = ""
        };
        noPrimaryAction.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        actionsStyle.Triggers.Add(noPrimaryAction);
        var secretActions = new DataTrigger
        {
            Binding = new WpfBinding(nameof(EditableSettingRow.Type)),
            Value = "secret"
        };
        secretActions.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        actionsStyle.Triggers.Add(secretActions);
        actions.SetValue(FrameworkElement.StyleProperty, actionsStyle);

        var revealButton = ActionButton(
            nameof(EditableSettingRow.RevealAction),
            nameof(EditableSettingRow.CanRevealAction),
            nameof(EditableSettingRow.RevealToolTip));
        revealButton.AddHandler(WpfButton.ClickEvent, revealClick);
        actions.AppendChild(revealButton);

        var copyButton = ActionButton(
            nameof(EditableSettingRow.CopyAction),
            nameof(EditableSettingRow.CanCopyAction),
            nameof(EditableSettingRow.CopyToolTip));
        copyButton.AddHandler(WpfButton.ClickEvent, copyClick);
        actions.AppendChild(copyButton);

        var primaryButton = ActionButton(
            nameof(EditableSettingRow.Action),
            nameof(EditableSettingRow.CanAction),
            nameof(EditableSettingRow.ActionToolTip),
            VisualRole.Primary);
        primaryButton.AddHandler(WpfButton.ClickEvent, primaryClick);
        actions.AppendChild(primaryButton);
        root.AppendChild(actions);

        var editor = new FrameworkElementFactory(typeof(Grid));

        var textBox = new FrameworkElementFactory(typeof(WpfTextBox));
        textBox.SetBinding(WpfTextBox.TextProperty, new WpfBinding(nameof(EditableSettingRow.Value))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            Delay = TextInputCommitDelayMilliseconds
        });
        textBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        textBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        textBox.SetValue(System.Windows.Controls.Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        textBox.SetValue(System.Windows.Controls.Control.PaddingProperty, new Thickness(8, 2, 8, 2));
        textBox.SetValue(FrameworkElement.MinHeightProperty, 28.0);
        textBox.SetValue(FrameworkElement.HeightProperty, 28.0);
        textBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 3, 0));
        var textBoxStyle = new Style(typeof(WpfTextBox), (Style)WpfApplication.Current.Resources[typeof(WpfTextBox)]);
        foreach (var hiddenType in new[] { "choice", "readonly", "secret" })
        {
            var trigger = new DataTrigger { Binding = new WpfBinding(nameof(EditableSettingRow.Type)), Value = hiddenType };
            trigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            textBoxStyle.Triggers.Add(trigger);
        }
        textBox.SetValue(FrameworkElement.StyleProperty, textBoxStyle);
        editor.AppendChild(textBox);

        var combo = new FrameworkElementFactory(typeof(WpfComboBox));
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new WpfBinding(nameof(EditableSettingRow.Options)));
        combo.SetBinding(WpfComboBox.SelectedItemProperty, new WpfBinding(nameof(EditableSettingRow.Value))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        combo.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        combo.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        combo.SetValue(FrameworkElement.MinHeightProperty, 28.0);
        combo.SetValue(FrameworkElement.HeightProperty, 28.0);
        combo.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 3, 0));
        var comboStyle = new Style(typeof(WpfComboBox), (Style)WpfApplication.Current.Resources[typeof(WpfComboBox)]);
        comboStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        var showComboForChoice = new DataTrigger { Binding = new WpfBinding(nameof(EditableSettingRow.Type)), Value = "choice" };
        showComboForChoice.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        comboStyle.Triggers.Add(showComboForChoice);
        combo.SetValue(FrameworkElement.StyleProperty, comboStyle);
        editor.AppendChild(combo);

        var textBlock = new FrameworkElementFactory(typeof(TextBlock));
        textBlock.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(EditableSettingRow.DisplayValue)));
        textBlock.SetValue(TextBlock.ForegroundProperty, WpfApplication.Current.Resources["TextSoft"]);
        textBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        textBlock.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        textBlock.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 3, 0));
        var textBlockStyle = new Style(typeof(TextBlock));
        textBlockStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        foreach (var visibleType in new[] { "readonly", "secret" })
        {
            var trigger = new DataTrigger { Binding = new WpfBinding(nameof(EditableSettingRow.Type)), Value = visibleType };
            trigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
            textBlockStyle.Triggers.Add(trigger);
        }
        textBlock.SetValue(FrameworkElement.StyleProperty, textBlockStyle);
        editor.AppendChild(textBlock);
        root.AppendChild(editor);

        return new DataGridTemplateColumn
        {
            Header = Loc.T("Settings.Col.Value"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 190,
            CanUserResize = true,
            CellTemplate = new DataTemplate { VisualTree = root }
        };
    }

    public static Style CellTextStyle(Style baseStyle)
    {
        var style = new Style(typeof(TextBlock), baseStyle);
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new WpfBinding(nameof(EditableSettingRow.ToolTip))));
        return style;
    }

    private static FrameworkElementFactory ActionButton(
        string contentBinding,
        string enabledBinding,
        string tooltipBinding,
        string visualRole = "")
    {
        var button = new FrameworkElementFactory(typeof(WpfButton));
        button.SetBinding(ContentControl.ContentProperty, new WpfBinding(contentBinding));
        button.SetBinding(UIElement.IsEnabledProperty, new WpfBinding(enabledBinding));
        button.SetBinding(FrameworkElement.ToolTipProperty, new WpfBinding(tooltipBinding));
        button.SetBinding(FrameworkElement.TagProperty, new WpfBinding("."));
        button.SetValue(ToolTipService.ShowOnDisabledProperty, true);
        button.SetValue(FrameworkElement.MinHeightProperty, 24.0);
        button.SetValue(System.Windows.Controls.Control.PaddingProperty, new Thickness(6, 1, 6, 2));
        button.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 0, 0));
        button.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Left);
        var style = new Style(typeof(WpfButton), (Style)WpfApplication.Current.Resources[typeof(WpfButton)]);
        if (!string.IsNullOrWhiteSpace(visualRole))
            style.Setters.Add(new Setter(VisualRole.ButtonRoleProperty, visualRole));
        if (visualRole == VisualRole.Primary)
        {
            var dangerTrigger = new DataTrigger
            {
                Binding = new WpfBinding(nameof(EditableSettingRow.Key)),
                Value = "cache"
            };
            dangerTrigger.Setters.Add(new Setter(VisualRole.ButtonRoleProperty, VisualRole.Danger));
            style.Triggers.Add(dangerTrigger);
        }
        var emptyTrigger = new Trigger { Property = ContentControl.ContentProperty, Value = "" };
        emptyTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        style.Triggers.Add(emptyTrigger);
        button.SetValue(FrameworkElement.StyleProperty, style);
        return button;
    }
}
