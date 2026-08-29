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
    private void ShowOverview()
        => ShowOverview(refresh: true);

    private void ShowOverview(bool refresh, bool rebuild = false)
    {
        _pageControllers.Overview.CancelPendingSelections();
        SetPage("Overview", Loc.T("PageSubtitle.Overview"));
        if (rebuild || !_overviewPage.IsAvailable)
        {
            var overview = OverviewPageFactory.Create(new OverviewPageRequest(
                _viewModel,
                _pageControllers.Overview.Build(),
                SetRuntimeMetricsGridColumnSizing,
                _settings.OverviewDashboardLayout));
            _overviewPage.Apply(overview);
            _overviewPage.ApplyUiPreferences(_settings);
            _runtimeDashboardPage.Apply(overview);
        }
        else
        {
            _overviewPage.ApplyUiPreferences(_settings);
        }

        PageHost.Content = _overviewPage.Scroller;
        StartRuntimeDashboardRefreshTimer();
        if (refresh)
        {
            RunBackground(RefreshOverviewAsync, "Overview refresh failed");
            RunBackground(RefreshOverviewModelSelectorAsync, "Overview model refresh failed");
            RunBackground(RefreshRuntimeMetricsAsync, "Runtime metrics refresh failed");
        }
    }

    private void ShowModels()
    {
        SetPage("Models", Loc.T("PageSubtitle.Models"));

        var modelsPage = ModelsPageFactory.Create(new ModelsPageRequest(
            _viewModel,
            _settings.ModelsRoot,
            CreateLaunchSettingsPanel(),
            _pageControllers.Models.Build()));

        _modelsPage.Apply(modelsPage);
        _modelsPage.ApplyUiPreferences(_settings);
        ConfigureHfSearchGrid();
        PageHost.Content = modelsPage.Root;
        RunBackground(RefreshModelsAsync, "Models refresh failed");
    }

    private void ShowRuntimes()
    {
        SetPage("Runtimes", Loc.T("PageSubtitle.Runtimes"));
        var runtimesPage = RuntimesPageFactory.Create(new RuntimesPageRequest(
            _viewModel,
            _settings.RuntimeRoot,
            _settings.CudaPackagePreference,
            _pageControllers.Runtimes.Build()));

        _runtimesPage.Apply(runtimesPage);
        PageHost.Content = runtimesPage.Root;
        RunBackground(DetectAndRefreshRuntimesAsync, "Runtime refresh failed");
    }

    private async Task ScanModelsFolderAsync()
    {
        await RunAsync(Loc.T("Models.Scanning"), async () =>
        {
            var catalog = ModelServices.Catalog;
            Require(catalog);
            var result = await catalog!.ScanDetailedAsync(_settings.ModelsRoot);
            await RefreshModelsAsync();
            await RefreshOverviewAsync();
            SetStatus(result.Summary);
            ShowModelScanDiagnostics(result);
        });
    }

    private void RefreshCurrentPage()
    {
        switch (_viewModel.CurrentPage)
        {
            case "Overview": ShowOverview(refresh: true, rebuild: true); break;
            case "Models": ShowModels(); break;
            case "Runtimes": ShowRuntimes(); break;
            case "Benchmarks": ShowBenchmarks(); break;
            case "Settings": ShowSettings(); break;
            case "Metrics": ShowLifetime(); break;
            case "Logs": ShowLogs(); break;
            case "Windows": ShowWindows(); break;
            case "WSL Linux": ShowWslLinux(); break;
            case "Updates": ShowUpdates(); break;
            case "Help": ShowHelp(); break;
        }
    }
}
