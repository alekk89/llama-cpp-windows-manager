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
    private void ShowWslLinux()
    {
        SetPage("WSL Linux", "Detect WSL, choose the Linux distro used for llama.cpp, and open setup actions.");
        var page = WslPageFactory.Create(new WslPageRequest(
            _viewModel,
            _pageControllers.Wsl.Build(),
            ButtonToolTip));
        _wslPage.Apply(page);
        PageHost.Content = page.Root;
        if (_environmentPageSnapshots.TryGetWslTools(out var cachedReport, out var cachedTools))
            PopulateWslLinuxPage(cachedReport, cachedTools);

        ApplyPendingHelpFocus();
        if (_environmentPageSnapshots.TryStartWslAutoRefresh())
            RunBackground(RefreshWslLinuxAsync, "WSL refresh failed");
    }

    private async Task RefreshWslLinuxAsync()
    {
        await RunAsync("Detecting WSL...", async () =>
        {
            var refresh = await _coreServices.Environment.WslPageWorkflow.RefreshAsync(_settings);
            if (refresh.SettingsChanged)
            {
                _settings = refresh.Settings;
                await PersistSettingsAsync();
            }
            _environmentPageSnapshots.StoreWslTools(refresh.Report, refresh.Tools);
            PopulateWslLinuxPage(refresh.Report, refresh.Tools);
            SetStatus(refresh.Report.Status);
        });
    }

    private async Task AutoSelectDetectedWslDistroAsync()
    {
        var result = await _coreServices.Environment.WslPageWorkflow.DetectRecommendedDistroAsync(_settings);
        if (!result.SettingsChanged)
            return;
        _settings = result.Settings;
        await PersistSettingsAsync();
    }

    private void PopulateWslLinuxPage(WslEnvironmentReport report, WslToolSnapshot tools)
    {
        var hasUbuntu = report.Distros.Any(distro => distro.IsUbuntu);
        _wslPage.ApplyActionState(report, hasUbuntu, tools);

        MetricCardFactory.SetMetricText(_wslPage.StatusMetric, report.Status);
        MetricCardFactory.SetMetricText(_wslPage.SelectedMetric, WslEnvironmentService.SelectedDistroSummary(report, _settings.WslDistro));
        MetricCardFactory.SetMetricText(_wslPage.InfoMetric, WslEnvironmentService.InstalledDistroSummary(report));
        MetricCardFactory.SetMetricText(_wslPage.ToolsMetric, WslEnvironmentService.ToolSummary(tools));

        _viewModel.WslLinux.ReplaceDistroRows(report, _settings.WslDistro);
        ApplyPendingHelpFocus();
    }
}
