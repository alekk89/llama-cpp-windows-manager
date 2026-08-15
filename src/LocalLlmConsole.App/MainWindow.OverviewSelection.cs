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
    private async Task RefreshOverviewModelSelectorAsync()
    {
        var modelLookup = AppServices.ModelLookupApplication;
        if (modelLookup is null) return;
        await RefreshOverviewModelChoicesAsync(await modelLookup.ListAsync());
    }

    private async Task RefreshOverviewModelChoicesAsync(IReadOnlyList<ModelRecord> models)
    {
        var selectedId = SelectedOverviewChoice()?.Id;
        if (string.IsNullOrWhiteSpace(selectedId))
            selectedId = _sessions.SelectedSnapshot()?.ModelId;

        var groupSnapshot = await ModelServices.ModelGroups.SnapshotAsync();
        var profiles = await AppServices.StateStore.ListNamedModelLaunchProfilesAsync();
        _viewModel.Overview.ReplaceModels(models, groupSnapshot.Groups, groupSnapshot.Assignments, profiles);
        _overviewPage.SelectModelChoice(selectedId, _viewModel.Overview.ModelChoices);

        await RefreshOverviewLaunchProfilesAsync();

        UpdateOverviewModelActions();
    }

    private async Task RefreshOverviewLaunchProfilesAsync()
        => await RefreshOverviewLaunchProfilesAsync(SelectedOverviewModel(), CancellationToken.None);

    private async Task RefreshOverviewLaunchProfilesAsync(ModelRecord? model, CancellationToken cancellationToken)
    {
        if (SelectedOverviewChoice() is { Kind: OverviewModelChoiceKind.Group, Group: { } group } groupChoice)
        {
            _viewModel.Overview.ReplaceGroupLaunchProfileSummary(group, groupChoice.LaunchProfileCount);
            _overviewPage.SelectLaunchProfile(group.Id);
            _overviewPage.SetLaunchProfileEnabled(false);
            return;
        }

        _overviewPage.SetLaunchProfileEnabled(true);
        var selectedProfileId = _overviewPage.SelectedLaunchProfileId;
        IReadOnlyList<NamedModelLaunchProfile> profiles = model is null || ModelServices.LaunchProfiles is null
            ? []
            : await ModelServices.LaunchProfiles.ListNamedAsync(model);
        cancellationToken.ThrowIfCancellationRequested();
        if (model is not null && ModelServices.LaunchProfiles is not null && profiles.Count == 0)
            profiles = [await ModelServices.LaunchProfiles.EnsureDefaultAsync(model, _settings)];
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameModel(model, SelectedOverviewModel())) return;
        _viewModel.Overview.ReplaceLaunchProfiles(profiles);
        _overviewPage.SelectLaunchProfile(selectedProfileId);
    }

    private string SelectedOverviewLaunchProfileId()
        => _overviewPage.SelectedLaunchProfileId;

    private Task SelectOverviewLaunchProfileAsync()
    {
        UpdateOverviewModelActions();
        return Task.CompletedTask;
    }

    private ModelRecord? SelectedOverviewModel()
    {
        return _overviewPage.SelectedModel(_viewModel.Overview.ModelChoices);
    }

    private OverviewModelChoice? SelectedOverviewChoice()
        => _overviewPage.SelectedChoice(_viewModel.Overview.ModelChoices);

    private ModelGroupRecord? SelectedOverviewGroup()
        => SelectedOverviewChoice()?.Group;

    private void UpdateOverviewModelActions()
    {
        var model = SelectedOverviewModel();
        var groupChoice = SelectedOverviewChoice() is { Kind: OverviewModelChoiceKind.Group } choice ? choice : null;
        var hasSelection = model is not null || groupChoice is not null;
        var hasProfileSelection = groupChoice is not null
            ? groupChoice.LaunchProfileCount > 0
            : !string.IsNullOrWhiteSpace(SelectedOverviewLaunchProfileId());
        var selectedProfileLoaded = groupChoice is not null
            ? IsOverviewSelectedGroupLoaded(groupChoice)
            : IsOverviewSelectedProfileLoaded(model);
        _overviewPage.SetModelActionsEnabled(hasSelection, hasProfileSelection, selectedProfileLoaded);
    }

    private bool IsOverviewSelectedGroupLoaded(OverviewModelChoice groupChoice)
    {
        var profileIds = groupChoice.LaunchProfileIds ?? [];
        return profileIds.Count > 0 && profileIds.All(profileId => _sessions.Snapshots().Any(session =>
            session.IsRunning && session.LaunchProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase)));
    }

    private bool IsOverviewSelectedProfileLoaded(ModelRecord? model)
    {
        if (model is null || _sessions.SessionForModel(model.Id) is not { IsRunning: true } session)
            return false;
        return string.Equals(
            session.LaunchProfileId,
            SelectedOverviewLaunchProfileId(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string LoadedSessionIdFromRowButton(object sender)
        => (sender as FrameworkElement)?.Tag is UiRow row
            ? row.Data["SessionId"]?.ToString() ?? ""
            : "";

    private static UiRow? EndpointRowFromLink(object sender)
        => sender is System.Windows.Documents.Hyperlink { Tag: UiRow row } ? row : null;

    private Task InspectSelectedOverviewEndpointAsync()
        => _overviewPage.SelectedLoadedSessionRow is { } row
            ? InspectOverviewEndpointRowAsync(row)
            : Task.CompletedTask;

    private async Task InspectOverviewEndpointRowAsync(UiRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        EndpointInspectionReport report;
        if (string.Equals(row.Data["Kind"]?.ToString(), "Gateway", StringComparison.OrdinalIgnoreCase))
        {
            report = await _coreServices.Runtime.EndpointInspection.InspectGatewayAsync(
                _settings,
                AppPreferenceService.GatewaySwapPolicyLabel(_settings.AutoLoadGatewayPolicy),
                AppPreferenceService.ModelAccessModeLabel(_settings.ModelAccessMode));
        }
        else
        {
            var sessionId = row.Data["SessionId"]?.ToString() ?? "";
            var session = _sessions.OverviewSnapshots().FirstOrDefault(item => string.Equals(
                item.SessionId,
                sessionId,
                StringComparison.OrdinalIgnoreCase));
            if (session is null)
            {
                SetStatus("The selected endpoint session is no longer available.");
                return;
            }
            report = await _coreServices.Runtime.EndpointInspection.InspectDirectAsync(session);
        }

        EndpointInspectionDialogFactory.Show(this, report);
    }

    private async Task UnloadLoadedSessionAsync(string sessionId)
    {
        var session = _sessions.Snapshots().FirstOrDefault(item => string.Equals(
            item.SessionId,
            sessionId,
            StringComparison.OrdinalIgnoreCase));
        if (session is null) return;

        var model = await FindModelByIdAsync(session.ModelId);
        if (model is null)
        {
            SetStatus("The selected loaded model is no longer present in the catalog.");
            return;
        }

        await _coreServices.Models.ModelRuntimeUnloadApplication.UnloadOverviewAsync(
            new ModelRuntimeUnloadApplicationRequest(model, session.IsRunning),
            ModelRuntimeUnloadActions());
    }

    private async Task SelectOverviewModelSessionAsync(CancellationToken cancellationToken)
    {
        if (_coreServices.Ui.SelectionReentrancy.IsLoadedSessionSelectionChanging) return;

        var model = SelectedOverviewModel();
        await RefreshOverviewLaunchProfilesAsync(model, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameModel(model, SelectedOverviewModel())) return;
        await _coreServices.Runtime.OverviewModelSelectionApplication.SelectAsync(
            new OverviewModelSelectionApplicationRequest(
                model,
                IsModelLoaded(model),
                IsModelActive(model)),
            OverviewModelSelectionActions(),
            cancellationToken);
    }

    private OverviewModelSelectionApplicationActions OverviewModelSelectionActions()
        => new(
            _coreServices.Runtime.RuntimeSessions.SelectModel,
            settings => _activeRuntimeSettings = settings,
            SaveActiveRuntimeSessionsAsync,
            RefreshRuntimeMetricsAsync,
            SetStatus);

    private async Task SelectLoadedSessionRowAsync(CancellationToken cancellationToken)
    {
        if (_coreServices.Ui.SelectionReentrancy.IsLoadedSessionSelectionChanging) return;
        if (_overviewPage.SelectedLoadedSessionRow is not { } row) return;

        var modelId = row.Data["ModelId"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(modelId)) return;

        await _coreServices.Runtime.OverviewLoadedSessionSelectionApplication.SelectAsync(
            modelId,
            OverviewLoadedSessionSelectionActions(),
            cancellationToken);
    }

    private OverviewLoadedSessionSelectionApplicationActions OverviewLoadedSessionSelectionActions()
        => new(
            FindOverviewModelChoice,
            RefreshOverviewModelSelectorAsync,
            modelId =>
            {
                using var selectionScope = _coreServices.Ui.SelectionReentrancy.SuppressLoadedSessionSelection();
                _overviewPage.SelectModelId(modelId);
            },
            _coreServices.Runtime.RuntimeSessions.SelectModel,
            settings => _activeRuntimeSettings = settings,
            SaveActiveRuntimeSessionsAsync,
            RefreshRuntimeMetricsAsync,
            UpdateOverviewModelActions,
            SetStatus);

    private ModelRecord? FindOverviewModelChoice(string modelId)
        => _viewModel.Overview.ModelChoices.FirstOrDefault(item =>
            item.Kind == OverviewModelChoiceKind.Model
            && string.Equals(item.Id, modelId, StringComparison.OrdinalIgnoreCase))?.Model;

    private static bool SameModel(ModelRecord? left, ModelRecord? right)
        => string.Equals(left?.Id ?? "", right?.Id ?? "", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> SelectOverviewLoadedModelAsync(string modelId)
    {
        if (_viewModel.CurrentPage != "Overview" || string.IsNullOrWhiteSpace(modelId))
            return false;

        if (FindOverviewModelChoice(modelId) is null)
            await RefreshOverviewModelSelectorAsync();

        using (_coreServices.Ui.SelectionReentrancy.SuppressLoadedSessionSelection())
            _overviewPage.SelectModelId(modelId);

        var selection = _coreServices.Runtime.RuntimeSessions.SelectModel(modelId);
        if (!selection.Selected)
            return false;

        _activeRuntimeSettings = selection.ActiveSettings;
        UpdateOverviewModelActions();
        return true;
    }

    private async Task<(string Model, string Runtime)> ActiveRuntimeLabelsAsync()
    {
        await Task.CompletedTask;
        var selectedModel = SelectedOverviewModel();
        var active = selectedModel is null
            ? _sessions.SelectedSnapshot()
            : _sessions.SessionForModel(selectedModel.Id);
        var labels = _coreServices.Runtime.RuntimeOverviewStatus.Labels(new RuntimeOverviewStatusRequest(
            selectedModel,
            active,
            _llama.State,
            _llama.LastExitCode));
        return (labels.Model, labels.Runtime);
    }

    private async Task<string> ActiveModelDisplayNameAsync(string modelId)
    {
        var modelLookup = AppServices.ModelLookupApplication;
        return modelLookup is null
            ? (string.IsNullOrWhiteSpace(modelId) ? "Unknown model" : modelId)
            : await modelLookup.DisplayNameAsync(modelId);
    }
}
