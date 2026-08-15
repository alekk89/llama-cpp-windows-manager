using System.Windows.Automation;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public static class UiAccessibility
{
    public static void SetButtonToolTip(WpfButton? button, string toolTip)
    {
        if (button is null) return;
        button.ToolTip = toolTip;
        AutomationProperties.SetName(button, toolTip.TrimEnd('.'));
        AutomationProperties.SetHelpText(button, toolTip);
    }
}
