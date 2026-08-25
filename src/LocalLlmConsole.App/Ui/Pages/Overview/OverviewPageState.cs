using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public sealed class OverviewPageState
{
    private OverviewPageControls? _controls;

    public WpfComboBox? ModelCombo { get; private set; }

    public WpfComboBox? LaunchProfileCombo { get; private set; }

    public WpfButton? LoadButton { get; private set; }

    public DataGrid? LoadedSessionsGrid { get; private set; }

    public UiRow? SelectedLoadedSessionRow => LoadedSessionsGrid?.SelectedItem as UiRow;

    public string SelectedLoadedSessionId => SelectedLoadedSessionRow?.Data["SessionId"]?.ToString() ?? "";

    public bool IsAvailable => _controls is not null;

    public ScrollViewer? Scroller => _controls?.Scroller;

    public void Apply(OverviewPageControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        _controls = controls;
        ModelCombo = controls.ModelCombo;
        LaunchProfileCombo = controls.LaunchProfileCombo;
        LoadButton = controls.LoadButton;
        LoadedSessionsGrid = controls.LoadedSessionsGrid;
    }

    public void ApplyUiPreferences(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_controls is not { } controls) return;

        if (!LayoutsMatch(controls.DashboardController.Layout, settings.OverviewDashboardLayout))
            controls.DashboardController.ApplyLayout(settings.OverviewDashboardLayout);

        controls.RuntimeLogSection.Visibility = VisibilityFor(settings.ShowOverviewLiveRuntimeLog);
        controls.MetricsSection.Visibility = VisibilityFor(settings.ShowOverviewAllMetrics);
        controls.RuntimeSectionsSplitter.Visibility = Visibility.Collapsed;

        var root = controls.Root;
        ConfigureOverviewDetailRow(root.RowDefinitions[2], settings.ShowOverviewLiveRuntimeLog, 0, 0, autoSize: true);
        root.RowDefinitions[3].Height = new GridLength(0);
        ConfigureOverviewDetailRow(root.RowDefinitions[4], settings.ShowOverviewAllMetrics, .92, 130);
    }

    private static void ConfigureOverviewDetailRow(
        RowDefinition row,
        bool visible,
        double weight,
        double minimum,
        bool autoSize = false)
    {
        row.MinHeight = visible ? minimum : 0;
        row.Height = visible
            ? autoSize ? GridLength.Auto : new GridLength(weight, GridUnitType.Star)
            : new GridLength(0);
    }

    private static Visibility VisibilityFor(bool visible)
        => visible ? Visibility.Visible : Visibility.Collapsed;

    private static bool LayoutsMatch(OverviewDashboardLayout current, OverviewDashboardLayout? requested)
    {
        var normalized = OverviewDashboardLayoutPolicy.Normalize(requested);
        return current.Version == normalized.Version
               && current.CardSizesLocked == normalized.CardSizesLocked
               && current.LockedSurfaceWidth.Equals(normalized.LockedSurfaceWidth)
               && current.Cards.Count == normalized.Cards.Count
               && current.Cards.Zip(normalized.Cards).All(pair => CardsMatch(pair.First, pair.Second));
    }

    private static bool CardsMatch(OverviewDashboardCardLayout first, OverviewDashboardCardLayout second)
        => string.Equals(first.Id, second.Id, StringComparison.OrdinalIgnoreCase)
           && first.ColumnSpan == second.ColumnSpan
           && first.Height == second.Height
           && first.Bounds == second.Bounds
           && string.Equals(first.Title, second.Title, StringComparison.Ordinal)
           && first.MetricIds.SequenceEqual(second.MetricIds, StringComparer.Ordinal)
           && (first.ChartMetricIds ?? []).SequenceEqual(second.ChartMetricIds ?? [], StringComparer.Ordinal);

    public void FocusLoadedSessionsGrid()
        => LoadedSessionsGrid?.Focus();

    public void FocusModelCombo()
        => ModelCombo?.Focus();

    public OverviewModelChoice? SelectedChoice(IReadOnlyList<OverviewModelChoice> modelChoices)
    {
        ArgumentNullException.ThrowIfNull(modelChoices);

        if (ModelCombo?.SelectedItem is OverviewModelChoice choice)
            return choice;
        if (ModelCombo?.SelectedValue is string selectedId)
            return modelChoices.FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    public ModelRecord? SelectedModel(IReadOnlyList<OverviewModelChoice> modelChoices)
        => SelectedChoice(modelChoices)?.Model;

    public string SelectedLaunchProfileId => LaunchProfileCombo?.SelectedValue?.ToString() ?? "";

    public string SelectedLaunchProfileName
        => (LaunchProfileCombo?.SelectedItem as OverviewLaunchProfileChoice)?.Name ?? "";

    public void SelectLaunchProfile(string? profileId)
    {
        if (LaunchProfileCombo is null) return;
        LaunchProfileCombo.SelectedValue = profileId ?? "";
        if (LaunchProfileCombo.SelectedIndex < 0 && LaunchProfileCombo.Items.Count > 0)
            LaunchProfileCombo.SelectedIndex = 0;
    }

    public void SelectModelChoice(string? selectedId, IReadOnlyList<OverviewModelChoice> modelChoices)
    {
        ArgumentNullException.ThrowIfNull(modelChoices);
        if (ModelCombo is null) return;

        if (modelChoices.Count == 0)
        {
            ModelCombo.SelectedIndex = -1;
            return;
        }

        var match = modelChoices.FirstOrDefault(model => string.Equals(model.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? modelChoices.First();
        ModelCombo.SelectedValue = match.Id;
    }

    public void SetLaunchProfileEnabled(bool enabled)
    {
        if (LaunchProfileCombo is not null)
            LaunchProfileCombo.IsEnabled = enabled;
    }

    public void SelectModelId(string modelId)
    {
        if (ModelCombo is not null)
            ModelCombo.SelectedValue = modelId;
    }

    public void SetModelActionsEnabled(
        bool hasSelection,
        bool hasProfileSelection,
        bool selectedProfileLoaded,
        bool selectedModelMissing)
    {
        if (LoadButton is not null)
        {
            var canLoad = hasSelection && hasProfileSelection && !selectedProfileLoaded;
            LoadButton.IsEnabled = canLoad && !selectedModelMissing;
            LoadButton.Visibility = hasSelection && hasProfileSelection || selectedModelMissing
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            LoadButton.Content = selectedProfileLoaded
                ? Loc.T("Overview.LoadedButton")
                : Loc.T("Overview.LoadButton");
            LoadButton.ToolTip = selectedModelMissing
                ? Loc.T("Overview.MissingModelLoadTooltip")
                : selectedProfileLoaded ? null : Loc.T("Tooltip.Load");
        }
    }

    public void RestoreLoadedSessionSelection(string sessionId, IReadOnlyList<UiRow> sessionRows)
    {
        ArgumentNullException.ThrowIfNull(sessionRows);
        if (LoadedSessionsGrid is null) return;

        LoadedSessionsGrid.SelectedItem = string.IsNullOrWhiteSpace(sessionId)
            ? null
            : sessionRows.FirstOrDefault(row =>
                string.Equals(row.Data["SessionId"]?.ToString(), sessionId, StringComparison.OrdinalIgnoreCase));
    }
}
