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
    private Task<bool> ConfirmRuntimeLaunchAdmissionAsync(RuntimeLaunchAdmissionPlan plan)
    {
        if (plan.Action == RuntimeLaunchAdmissionAction.Allow) return Task.FromResult(true);
        if (plan.BlocksLaunch)
        {
            _coreServices.App.Dialogs.Notify(
                this,
                plan.InteractiveMessage,
                "VRAM check",
                MessageBoxImage.Warning);
            return Task.FromResult(false);
        }

        var confirmed = _coreServices.App.Dialogs.Confirm(
            this,
            plan.InteractiveMessage,
            "VRAM check",
            MessageBoxImage.Warning);
        return Task.FromResult(confirmed);
    }
}
