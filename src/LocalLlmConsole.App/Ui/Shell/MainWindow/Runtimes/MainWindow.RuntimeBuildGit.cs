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
    private long MaxLogBytes() => BoundedLogFile.MegabytesToBytes(_settings.MaxLogFileSizeMb);

    private async Task DeleteRuntimeBuildAsync(RuntimeRecord runtime)
    {
        var deletion = RuntimeServices.RuntimeBuildDeletionApplication;
        if (deletion is null) return;
        await deletion.DeleteRuntimeAsync(runtime, _settings, RuntimeBuildDeletionActions());
    }
}
