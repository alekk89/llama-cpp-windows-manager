namespace LocalLlmConsole;

public partial class MainWindow
{
    private async Task ManageModelGroupsAsync()
    {
        var service = ModelServices.ModelGroups;
        var stateStore = _stateStore ?? throw new InvalidOperationException(Loc.T("Status.PleaseWait"));
        var snapshot = await service.SnapshotAsync();
        var profiles = await stateStore.ListNamedModelLaunchProfilesAsync();
        var modelNames = (await stateStore.ListModelsAsync())
            .ToDictionary(model => model.Id, model => model.Name, StringComparer.OrdinalIgnoreCase);
        var result = ModelGroupDialogFactory.ShowManager(
            this,
            snapshot.Groups,
            profiles,
            modelNames,
            snapshot.Assignments);
        if (result is null) return;

        await RunAsync(Loc.T("ModelGroups.Status.Saving"), async () =>
        {
            var edits = result.Groups.Select(row => new ModelGroupEditDefinition(
                row.EditorKey,
                row.Id,
                row.Name,
                row.RetentionMode,
                row.IdleMinutes,
                row.EvictionPriority)).ToArray();
            await service.ReplaceAsync(edits, result.Assignments);

            await RefreshModelsAsync();
            await RefreshOverviewModelSelectorAsync();
            SetStatus(Loc.T("ModelGroups.Status.Saved"));
        });
    }

    private async Task AssignLaunchProfileGroupAsync(ModelRecord model, NamedModelLaunchProfile profile)
    {
        var service = ModelServices.ModelGroups;
        var snapshot = await service.SnapshotAsync();
        if (snapshot.Groups.Count == 0)
        {
            await ManageModelGroupsAsync();
            snapshot = await service.SnapshotAsync();
            if (snapshot.Groups.Count == 0)
            {
                SetStatus(Loc.T("ModelGroups.Status.CreateFirst"));
                return;
            }
        }
        var currentGroupId = snapshot.Assignments.GetValueOrDefault(profile.Id)?.GroupId ?? "";
        var selectedGroupId = ModelGroupDialogFactory.ShowAssignment(
            this,
            model,
            profile,
            snapshot.Groups,
            currentGroupId,
            out var accepted);
        if (!accepted) return;

        await RunAsync(Loc.T("ModelGroups.Status.Assigning"), async () =>
        {
            if (string.IsNullOrWhiteSpace(selectedGroupId))
                await service.UnassignAsync(profile.Id);
            else
                await service.AssignAsync(profile.Id, selectedGroupId);
            await RefreshModelsAsync();
            await RefreshOverviewModelSelectorAsync();
            SetStatus(string.IsNullOrWhiteSpace(selectedGroupId)
                ? Loc.T("ModelGroups.Status.Inherits", model.Name, profile.Name)
                : Loc.T("ModelGroups.Status.Assigned", model.Name, profile.Name, ModelGroupService.Resolve(await service.SnapshotAsync(), selectedGroupId).Name));
        });
    }

    private async Task RemoveLaunchProfileGroupAsync(ModelRecord model, NamedModelLaunchProfile profile)
    {
        var service = ModelServices.ModelGroups;
        var snapshot = await service.SnapshotAsync();
        if (!snapshot.Assignments.ContainsKey(profile.Id))
        {
            SetStatus(Loc.T("ModelGroups.Status.AlreadyUngrouped", model.Name, profile.Name));
            return;
        }

        await RunAsync(Loc.T("ModelGroups.Status.Removing"), async () =>
        {
            await service.UnassignAsync(profile.Id);
            await RefreshModelsAsync();
            await RefreshOverviewModelSelectorAsync();
            SetStatus(Loc.T("ModelGroups.Status.Inherits", model.Name, profile.Name));
        });
    }
}
