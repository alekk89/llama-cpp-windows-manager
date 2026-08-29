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
    private async Task ApplyModelCapabilitiesAsync(ModelRecord? model, CancellationToken cancellationToken = default)
    {
        var capabilities = model is null
            ? ModelCapabilityService.Empty()
            : await CachedModelCapabilitiesAsync(model, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (model is not null && !string.Equals(SelectedModel()?.Id, model.Id, StringComparison.OrdinalIgnoreCase)) return;

        var capabilityState = _coreServices.Ui.SelectedCapabilities.Apply(model, capabilities);
        if (_launchSettingsPanel.ModelCapabilityText is not null)
        {
            _launchSettingsPanel.ModelCapabilityText.Text = capabilityState.DisplayText;
            _launchSettingsPanel.ModelCapabilityText.ToolTip = TooltipText(capabilityState.DisplayText);
        }
        UpdateLaunchControlVisibility();
    }

    private async Task<ModelCapabilitySummary> CachedModelCapabilitiesAsync(ModelRecord model, CancellationToken cancellationToken = default)
        => await _coreServices.Models.ModelCapabilities.ReadAsync(model, cancellationToken);

    private void UpdateLaunchControlVisibility()
    {
        var plan = _coreServices.Models.LaunchSettingsControlStates.Build(new LaunchSettingsControlStateRequest(
            _coreServices.Ui.AdvancedSections.ShowLaunchSettings,
            SelectedLaunchRuntimeBackend(),
            _coreServices.Ui.SelectedCapabilities.VisionLaunchSettingsAvailable,
            ComboValue(_launchSettingsPanel.FormControls.SpeculativeTypeCombo)));

        _launchSettingsPanel.ApplyControlState(plan);
    }
}
