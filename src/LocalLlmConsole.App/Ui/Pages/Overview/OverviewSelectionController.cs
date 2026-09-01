using System.Windows;

namespace LocalLlmConsole;

public sealed record OverviewSelectionControllerActions(
    Func<MainWindowLoadedAppServices> AppServices,
    Func<MainWindowLoadedModelServices> ModelServices,
    Func<AppSettings> Settings,
    Action<AppSettings?> SetActiveRuntimeSettings,
    Func<ModelRuntimeUnloadApplicationActions> ModelRuntimeUnloadActions,
    Func<Task> SaveActiveRuntimeSessionsAsync,
    Func<Task> RefreshRuntimeMetricsAsync,
    Action<string> SetStatus,
    Window Owner,
    Action<string> CopyToClipboard);

public sealed class OverviewSelectionController
{
    private readonly MainWindowViewModel _viewModel;
    private readonly OverviewPageState _page;
    private readonly LoadedModelSessionManager _sessions;
    private readonly MainWindowCoreRuntimeServices _runtime;
    private readonly MainWindowCoreModelServices _models;
    private readonly SelectionReentrancyCoordinator _selection;
    private readonly OverviewSelectionControllerActions _actions;

    public OverviewSelectionController(
        MainWindowViewModel viewModel,
        OverviewPageState page,
        LoadedModelSessionManager sessions,
        MainWindowCoreRuntimeServices runtime,
        MainWindowCoreModelServices models,
        SelectionReentrancyCoordinator selection,
        OverviewSelectionControllerActions actions)
    {
        _viewModel = viewModel;
        _page = page;
        _sessions = sessions;
        _runtime = runtime;
        _models = models;
        _selection = selection;
        _actions = actions;
    }

    public async Task RefreshModelsAsync()
        => await RefreshModelChoicesAsync(await _actions.AppServices().ModelLookupApplication.ListAsync());

    public async Task RefreshModelChoicesAsync(
        IReadOnlyList<ModelRecord> models,
        IReadOnlyDictionary<string, string>? modelSizeLabels = null)
    {
        var selectedId = SelectedChoice()?.Id;
        if (string.IsNullOrWhiteSpace(selectedId))
            selectedId = _sessions.SelectedSnapshot()?.ModelId;

        var modelServices = _actions.ModelServices();
        modelSizeLabels ??= await modelServices.ModelCatalogRefreshApplication.ReadModelSizeLabelsAsync(models);
        var groupSnapshot = await modelServices.ModelGroups.SnapshotAsync();
        var profiles = await _actions.AppServices().StateStore.ListNamedModelLaunchProfilesAsync();
        _viewModel.Overview.ReplaceModels(models, groupSnapshot.Groups, groupSnapshot.Assignments, profiles, modelSizeLabels);
        _page.SelectModelChoice(selectedId, _viewModel.Overview.ModelChoices);
        await RefreshLaunchProfilesAsync();
        UpdateActions();
    }

    public async Task RefreshLaunchProfilesAsync()
        => await RefreshLaunchProfilesAsync(SelectedModel(), CancellationToken.None);

    public async Task RefreshLaunchProfilesAsync(ModelRecord? model, CancellationToken cancellationToken)
    {
        if (SelectedChoice() is { Kind: OverviewModelChoiceKind.Group, Group: { } group } groupChoice)
        {
            _viewModel.Overview.ReplaceGroupLaunchProfileSummary(group, groupChoice.LaunchProfileCount);
            _page.SelectLaunchProfile(group.Id);
            _page.SetLaunchProfileEnabled(false);
            return;
        }

        _page.SetLaunchProfileEnabled(true);
        var selectedProfileId = _page.SelectedLaunchProfileId;
        var launchProfiles = _actions.ModelServices().LaunchProfiles;
        IReadOnlyList<NamedModelLaunchProfile> profiles = model is null
            ? []
            : await launchProfiles.ListNamedAsync(model);
        cancellationToken.ThrowIfCancellationRequested();
        if (model is not null && profiles.Count == 0)
            profiles = [await launchProfiles.EnsureDefaultAsync(model, _actions.Settings())];
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameModel(model, SelectedModel())) return;
        _viewModel.Overview.ReplaceLaunchProfiles(profiles);
        _page.SelectLaunchProfile(selectedProfileId);
    }

    public string SelectedLaunchProfileId => _page.SelectedLaunchProfileId;

    public ModelRecord? SelectedModel()
        => _page.SelectedModel(_viewModel.Overview.ModelChoices);

    public OverviewModelChoice? SelectedChoice()
        => _page.SelectedChoice(_viewModel.Overview.ModelChoices);

    public ModelGroupRecord? SelectedGroup()
        => SelectedChoice()?.Group;

    public void UpdateActions()
    {
        var model = SelectedModel();
        var groupChoice = SelectedChoice() is { Kind: OverviewModelChoiceKind.Group } choice ? choice : null;
        var hasSelection = model is not null || groupChoice is not null;
        var hasProfileSelection = groupChoice is not null
            ? groupChoice.LaunchProfileCount > 0
            : !string.IsNullOrWhiteSpace(SelectedLaunchProfileId);
        var selectedProfileLoaded = groupChoice is not null
            ? IsGroupLoaded(groupChoice)
            : IsProfileLoaded(model);
        _page.SetModelActionsEnabled(
            hasSelection,
            hasProfileSelection,
            selectedProfileLoaded,
            SelectedChoice()?.IsMissing == true);
    }

    private bool IsGroupLoaded(OverviewModelChoice groupChoice)
    {
        var profileIds = groupChoice.LaunchProfileIds ?? [];
        return profileIds.Count > 0 && profileIds.All(profileId => _sessions.Snapshots().Any(session =>
            session.IsRunning && session.LaunchProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase)));
    }

    private bool IsProfileLoaded(ModelRecord? model)
    {
        return model is not null
            && _sessions.SessionForProfile(model.Id, SelectedLaunchProfileId) is { IsRunning: true };
    }

    public bool IsSelectedProfileLoaded(ModelRecord? model)
        => IsProfileLoaded(model);

    public static string SessionIdFromRowButton(object sender)
        => (sender as FrameworkElement)?.Tag is OverviewSessionRow row
            ? row.SessionId
            : "";

    public static OverviewSessionRow? EndpointRowFromLink(object sender)
        => sender is System.Windows.Documents.Hyperlink { Tag: OverviewSessionRow row } ? row : null;

    public Task InspectSelectedEndpointAsync()
        => _page.SelectedLoadedSessionRow is { } row ? InspectEndpointRowAsync(row) : Task.CompletedTask;

    public async Task InspectEndpointRowAsync(OverviewSessionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        EndpointInspectionReport report;
        string apiKey;
        var settings = _actions.Settings();
        if (row.Kind == OverviewEndpointKind.Gateway)
        {
            apiKey = settings.ModelApiKey;
            report = await _runtime.EndpointInspection.InspectGatewayAsync(
                settings,
                AppPreferenceService.GatewaySwapPolicyLabel(settings.AutoLoadGatewayPolicy),
                AppPreferenceService.ModelAccessModeLabel(settings.ModelAccessMode));
        }
        else
        {
            var sessionId = row.SessionId;
            var session = _sessions.OverviewSnapshots().FirstOrDefault(item => string.Equals(
                item.SessionId,
                sessionId,
                StringComparison.OrdinalIgnoreCase));
            if (session is null)
            {
                _actions.SetStatus(Loc.T("Overview.EndpointSessionUnavailable"));
                return;
            }
            apiKey = session.LaunchSettings.ModelApiKey;
            report = await _runtime.EndpointInspection.InspectDirectAsync(session);
        }

        EndpointInspectionDialogFactory.Show(_actions.Owner, report, apiKey, _actions.CopyToClipboard);
    }

    public async Task UnloadSessionAsync(string sessionId)
    {
        var session = _sessions.SessionById(sessionId);
        if (session is null) return;
        await _sessions.StopAsync(session.SessionId);
        _actions.SetActiveRuntimeSettings(_sessions.ActiveSettings);
        await _actions.SaveActiveRuntimeSessionsAsync();
        await _actions.RefreshRuntimeMetricsAsync();
        UpdateActions();
        _actions.SetStatus(Loc.T("Tray.StoppedProfile", session.ModelName, session.LaunchProfileName));
    }

    public async Task SelectModelSessionAsync(CancellationToken cancellationToken)
    {
        if (_selection.IsLoadedSessionSelectionChanging) return;

        var model = SelectedModel();
        await RefreshLaunchProfilesAsync(model, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameModel(model, SelectedModel())) return;
        await _runtime.OverviewModelSelectionApplication.SelectAsync(
            new OverviewModelSelectionApplicationRequest(
                model,
                IsModelLoaded(model),
                IsModelActive(model)),
            new OverviewModelSelectionApplicationActions(
                _runtime.RuntimeSessions.SelectModel,
                _actions.SetActiveRuntimeSettings,
                _actions.SaveActiveRuntimeSessionsAsync,
                _actions.RefreshRuntimeMetricsAsync,
                _actions.SetStatus),
            cancellationToken);
    }

    public async Task SelectLoadedSessionRowAsync(CancellationToken cancellationToken)
    {
        if (_selection.IsLoadedSessionSelectionChanging || _page.SelectedLoadedSessionRow is not { } row) return;
        var modelId = row.ModelId;
        if (string.IsNullOrWhiteSpace(modelId)) return;

        await _runtime.OverviewLoadedSessionSelectionApplication.SelectAsync(
            modelId,
            row.SessionId,
            new OverviewLoadedSessionSelectionApplicationActions(
                FindModelChoice,
                RefreshModelsAsync,
                selectedModelId =>
                {
                    using var selectionScope = _selection.SuppressLoadedSessionSelection();
                    _page.SelectModelId(selectedModelId);
                },
                _runtime.RuntimeSessions.SelectSession,
                _actions.SetActiveRuntimeSettings,
                _actions.SaveActiveRuntimeSessionsAsync,
                _actions.RefreshRuntimeMetricsAsync,
                UpdateActions,
                _actions.SetStatus),
            cancellationToken);
    }

    public async Task<bool> SelectLoadedModelAsync(string modelId)
    {
        if (_viewModel.CurrentPage != "Overview" || string.IsNullOrWhiteSpace(modelId)) return false;
        if (FindModelChoice(modelId) is null)
            await RefreshModelsAsync();

        using (_selection.SuppressLoadedSessionSelection())
            _page.SelectModelId(modelId);

        var selection = _runtime.RuntimeSessions.SelectModel(modelId);
        if (!selection.Selected) return false;
        _actions.SetActiveRuntimeSettings(selection.ActiveSettings);
        UpdateActions();
        return true;
    }

    public async Task SelectLoadedSessionAsync(string sessionId, string modelId)
    {
        if (!await SelectLoadedModelAsync(modelId)) return;
        var selection = _runtime.RuntimeSessions.SelectSession(sessionId);
        if (!selection.Selected) return;
        _actions.SetActiveRuntimeSettings(selection.ActiveSettings);
        UpdateActions();
    }

    public Task<(string Model, string Runtime)> ActiveRuntimeLabelsAsync()
    {
        var selectedModel = SelectedModel();
        var active = selectedModel is null ? _sessions.SelectedSnapshot() : _sessions.SessionForModel(selectedModel.Id);
        var supervisor = _sessions.ActiveSupervisor;
        var labels = _runtime.RuntimeOverviewStatus.Labels(new RuntimeOverviewStatusRequest(
            selectedModel,
            active,
            supervisor.State,
            supervisor.LastExitCode));
        return Task.FromResult((labels.Model, labels.Runtime));
    }

    public Task<string> ActiveModelDisplayNameAsync(string modelId)
        => _actions.AppServices().ModelLookupApplication.DisplayNameAsync(modelId);

    private ModelRecord? FindModelChoice(string modelId)
        => _viewModel.Overview.ModelChoices.FirstOrDefault(item =>
            item.Kind == OverviewModelChoiceKind.Model
            && string.Equals(item.Id, modelId, StringComparison.OrdinalIgnoreCase))?.Model;

    private bool IsModelLoaded(ModelRecord? model)
        => model is not null && _sessions.SessionForModel(model.Id)?.IsRunning == true;

    private bool IsModelActive(ModelRecord? model)
        => model is not null && string.Equals(_sessions.SelectedSnapshot()?.ModelId, model.Id, StringComparison.OrdinalIgnoreCase);

    private static bool SameModel(ModelRecord? left, ModelRecord? right)
        => string.Equals(left?.Id ?? "", right?.Id ?? "", StringComparison.OrdinalIgnoreCase);
}
