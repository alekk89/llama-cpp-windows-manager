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
    private async Task SearchHuggingFaceAsync()
    {
        var query = _modelsPage.HuggingFaceQuery;
        var huggingFace = AppServices.HuggingFace;
        Require(huggingFace);
        await _coreServices.HuggingFaceServices.HuggingFaceSearchApplication.SearchAsync(query, _settings, HuggingFaceSearchActions(huggingFace!));
    }

    private HuggingFaceSearchApplicationActions HuggingFaceSearchActions(HuggingFaceService huggingFace)
        => new(
            RunAsync,
            ConfigureHfSearchGrid,
            InstalledHuggingFaceInventoryAsync,
            async query => await huggingFace.SearchAsync(query),
            _viewModel.HuggingFace.ReplaceSearchResults);

    private async Task StartHuggingFaceDownloadAsync(HuggingFaceFile file)
    {
        var huggingFace = AppServices.HuggingFace;
        Require(huggingFace);
        await _coreServices.HuggingFaceServices.HuggingFaceDownloadApplication.StartAsync(file, _settings, HuggingFaceDownloadActions(huggingFace!));
    }

    private HuggingFaceDownloadApplicationActions HuggingFaceDownloadActions(HuggingFaceService huggingFace)
        => new(
            RunAsync,
            async (file, settings) => await huggingFace.StartDownloadAsync(file, settings),
            RefreshOverviewAsync,
            ShowDownloadHistoryAsync,
            jobId => RunBackground(() => MonitorDownloadAsync(jobId), "Download monitor failed"),
            SetStatus);

    private HuggingFaceModelCardApplicationActions HuggingFaceModelCardActions()
        => new(
            _coreServices.App.ShellIntegration.OpenUrl,
            SetStatus);
}
