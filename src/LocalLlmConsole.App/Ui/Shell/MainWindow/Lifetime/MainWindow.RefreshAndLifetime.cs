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
    private void ShowLifetime()
    {
        SetPage("Metrics", Loc.T("Lifetime.PageDescription"));
        var page = SelectorFavoriteBinding.ConfigureLifetime(LifetimePageFactory.Create(new LifetimePageRequest(
            _viewModel.LifetimeMetrics.Rows,
            _viewModel.LifetimeMetrics.Selection,
            _pageControllers.Lifetime.Build())), () => _stateStore, SetStatus);
        _lifetimePage.Apply(page.Controls);
        PageHost.Content = page.Content;
        RunBackground(RefreshLifetimeMetricsAsync, "Metrics refresh failed");
    }

    private async Task RefreshModelsAsync()
    {
        var modelRefresh = ModelServices.ModelCatalogRefreshApplication;
        var selectedId = SelectedModel()?.Id;
        var selectedProfileId = SelectedModelLaunchProfileId();
        var result = await modelRefresh.RefreshAsync(ModelCatalogRefreshActions());
        var groupSnapshot = await ModelServices.ModelGroups.SnapshotAsync();
        var groupsByProfile = groupSnapshot.Assignments.Values
            .Select(assignment => (assignment.LaunchProfileId, Group: groupSnapshot.Groups.FirstOrDefault(group =>
                group.Id.Equals(assignment.GroupId, StringComparison.OrdinalIgnoreCase))))
            .Where(pair => pair.Group is not null)
            .ToDictionary(pair => pair.LaunchProfileId, pair => pair.Group!, StringComparer.OrdinalIgnoreCase);
        var favoriteFlags = await Task.WhenAll(ModelServices.TrayProfiles.FavoriteProfileIdsAsync(), AppServices.StartupLaunchProfiles.ConfiguredProfileIdsAsync(), AppServices.StateStore.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.Model));

        _viewModel.Models.ReplaceModels(
            result.Models,
            IsModelLoaded,
            result.NamedLaunchProfiles,
            result.ModelSizeLabels,
            groupsByProfile,
            favoriteFlags[0], favoriteFlags[1], favoriteFlags[2]);
        var profileModelId = _viewModel.Models.ModelIdForLaunchProfile(selectedProfileId);
        _viewModel.Models.ShowLaunchProfilesForModel(
            profileModelId ?? selectedId ?? _viewModel.Models.Rows.FirstOrDefault()?.Model.Id);
        SelectModelAfterRefresh(selectedId, selectedProfileId);
        await RenderSelectedModelLaunchSettingsAsync();
        await RefreshOverviewModelChoicesAsync(result.Models, result.ModelSizeLabels);
    }

    private ModelCatalogRefreshApplicationActions ModelCatalogRefreshActions()
        => new(EnsureDefaultModelLaunchProfilesAsync);

    private void SelectModelAfterRefresh(string? selectedId, string? selectedProfileId = null)
    {
        _modelsPage.SelectModelAfterRefresh(selectedId, selectedProfileId, _viewModel.Models.Rows, _viewModel.Models.VariantRows);
    }

    private void SelectLaunchProfileAfterRefresh(string profileId)
        => SelectModelAfterRefresh(null, profileId);

    private async Task RefreshRuntimesAsync()
    {
        var runtimeCatalog = RuntimeServices.RuntimeCatalogApplication;
        if (runtimeCatalog is null) return;
        var selectedId = SelectedRuntime()?.Id;
        var result = await runtimeCatalog.RefreshAsync(new RuntimeCatalogRefreshApplicationRequest(
            _settings,
            _sessions.Snapshots(),
            _runtimeCatalogState.RuntimeUpdateStates,
            _runtimeCatalogState.RuntimePackageUpdateStates));
        var favoriteRuntimeIds = await AppServices.StateStore.ListSelectorFavoriteIdsAsync(SelectorFavoriteKind.Runtime);
        _viewModel.Runtimes.ReplaceRows(result.Rows.Runtimes, favoriteRuntimeIds);
        _viewModel.RuntimePackages.ReplaceRows(result.Rows.PackagePresets);
        _viewModel.RuntimeBuilds.ReplaceRows(result.Rows.BuildPresets);
        _runtimesPage.RestoreRuntimeSelection(selectedId, _viewModel.Runtimes.Rows);
        await RefreshRuntimeSelectorAsync(runtimes: result.Runtimes);
    }

    private async Task RefreshLifetimeMetricsAsync()
    {
        var lifetimeMetrics = AppServices.LifetimeMetricsApplication;
        if (lifetimeMetrics is null) return;
        await _lifetimeMetricsRefreshGate.WaitAsync();
        try
        {
            var version = lifetimeMetrics.DataVersion;
            var selection = _lifetimePage.Selection;
            _viewModel.LifetimeMetrics.SetSelection(selection);
            var report = await lifetimeMetrics.GetReportAsync(
                selection.Query,
                electricityTariff: ElectricityTariffPolicy.FromSettings(_settings));
            var presentation = _viewModel.LifetimeMetrics.ReplaceReport(report);
            _lifetimePage.ApplyPresentation(presentation);
            _lastLifetimeReportDataVersion = version;
            _nextLifetimeReportRefreshAt = DateTimeOffset.UtcNow.AddSeconds(5);
        }
        finally
        {
            _lifetimeMetricsRefreshGate.Release();
        }
    }

    private async Task LifetimeFiltersChangedAsync()
    {
        if (_lifetimePage.IsApplying) return;
        _viewModel.LifetimeMetrics.SetSelection(_lifetimePage.Selection);
        await RefreshLifetimeMetricsAsync();
    }

    private async Task LifetimeRangeChangedAsync()
    {
        _lifetimePage.ClearDateSelection();
        await LifetimeFiltersChangedAsync();
    }

    private async Task ClearLifetimeDateSelectionAsync()
    {
        _lifetimePage.ClearDateSelection();
        await LifetimeFiltersChangedAsync();
    }

    private async Task ResetVisibleLifetimeMetricAsync()
    {
        var modelId = _lifetimePage.Selection.ModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            await ResetLifetimeMetricAsync(new LifetimeMetricRow
            {
                Kind = LifetimeMetricRowKind.Total,
                ModelName = Loc.T("Lifetime.AllModels")
            });
            return;
        }

        var row = _viewModel.LifetimeMetrics.Rows.FirstOrDefault(candidate =>
            candidate.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        await ResetLifetimeMetricAsync(row);
    }

    private async Task ResetLifetimeMetricAsync(LifetimeMetricRow? row)
        => await _coreServices.App.LifetimeMetricResetApplication.ResetAsync(row, LifetimeMetricResetActions());

    private LifetimeMetricResetApplicationActions LifetimeMetricResetActions()
        => new(
            ConfirmLifetimeMetricReset,
            DeleteLifetimeMetricAsync,
            DeleteAllLifetimeMetricsAsync,
            ResetLifetimeCounters,
            RefreshLifetimeMetricsAsync,
            SetStatus);

    private bool ConfirmLifetimeMetricReset(LifetimeMetricResetConfirmation confirmation)
        => _coreServices.App.Dialogs.Confirm(
            this,
            confirmation.Message,
            confirmation.Title,
            MessageBoxImage.Warning);

    private async Task DeleteLifetimeMetricAsync(string modelId)
    {
        var lifetimeMetrics = AppServices.LifetimeMetricsApplication;
        if (lifetimeMetrics is not null)
            await lifetimeMetrics.DeleteModelUsageAsync(modelId);
    }

    private async Task DeleteAllLifetimeMetricsAsync()
    {
        var lifetimeMetrics = AppServices.LifetimeMetricsApplication;
        if (lifetimeMetrics is not null)
            await lifetimeMetrics.DeleteAllUsageAsync();
    }
}
