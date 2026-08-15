using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed class ModelsPageViewModel
{
    private readonly List<ModelGridRow> _allVariantRows = [];

    public ObservableCollection<ModelGridRow> Rows { get; } = new();
    public ObservableCollection<ModelGridRow> VariantRows { get; } = new();

    public void ReplaceModels(
        IEnumerable<ModelRecord> models,
        Func<ModelRecord, bool> isModelActive,
        IEnumerable<NamedModelLaunchProfile>? namedProfiles = null,
        IReadOnlyDictionary<string, string>? modelSizeLabels = null,
        IReadOnlyDictionary<string, ModelGroupRecord>? launchProfileGroups = null)
    {
        var allModels = models.ToArray();
        Rows.Clear();
        VariantRows.Clear();
        _allVariantRows.Clear();
        foreach (var model in allModels.Where(model => !ModelAliasService.IsLaunchAlias(model)))
        {
            Rows.Add(new ModelGridRow
            {
                Name = model.Name,
                Quant = ModelCatalogService.InferQuant(model.ModelPath),
                Size = modelSizeLabels?.GetValueOrDefault(model.Id) ?? "",
                CanDelete = !isModelActive(model),
                DeleteToolTip = isModelActive(model)
                    ? "Unload this model before deleting it from disk."
                    : "Delete this model file and remove it from the catalog.",
                Model = model
            });
        }

        var physicalModels = allModels.Where(model => !ModelAliasService.IsLaunchAlias(model)).ToArray();
        var profiles = (namedProfiles ?? []).ToArray();
        var profileCounts = profiles
            .GroupBy(profile => profile.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            var model = physicalModels.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, profile.ModelId, StringComparison.OrdinalIgnoreCase));
            if (model is null) continue;
            var active = isModelActive(model);
            var hasAlternative = profileCounts.GetValueOrDefault(profile.ModelId) > 1;
            var groupName = launchProfileGroups?.GetValueOrDefault(profile.Id)?.Name ?? "";
            _allVariantRows.Add(new ModelGridRow
            {
                Name = profile.Name,
                Quant = "Profile",
                Size = modelSizeLabels?.GetValueOrDefault(model.Id) ?? "",
                BaseModel = model.Name,
                Port = profile.Settings.Port.ToString(CultureInfo.InvariantCulture),
                Group = groupName,
                GroupAction = string.IsNullOrWhiteSpace(groupName) ? "Add" : "",
                GroupToolTip = string.IsNullOrWhiteSpace(groupName)
                    ? "Add this launch profile to a model group."
                    : $"Click {groupName} to change or remove this group assignment.",
                CanAssignGroup = string.IsNullOrWhiteSpace(groupName),
                DeleteAction = hasAlternative ? "Remove" : "",
                DeleteToolTip = !hasAlternative
                    ? "Every model needs at least one launch profile. Add another profile before removing this one."
                    : active
                    ? "Unload this model before removing its selected launch profile."
                    : profile.IsDefault
                    ? "Remove this profile. A remaining profile will become the model default."
                    : "Remove this launch profile. The model file is kept.",
                CanDelete = hasAlternative && !active,
                Model = model,
                LaunchProfile = profile
            });
        }

        ShowLaunchProfilesForModel(Rows.FirstOrDefault()?.Model.Id);
    }

    public void ShowLaunchProfilesForModel(string? modelId)
    {
        var matchingRows = string.IsNullOrWhiteSpace(modelId)
            ? []
            : _allVariantRows.Where(row => string.Equals(
                row.Model.Id,
                modelId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (VariantRows.SequenceEqual(matchingRows)) return;

        VariantRows.Clear();
        foreach (var row in matchingRows)
            VariantRows.Add(row);
    }

    public string? ModelIdForLaunchProfile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return null;
        return _allVariantRows.FirstOrDefault(row => string.Equals(
            row.LaunchProfile?.Id,
            profileId,
            StringComparison.OrdinalIgnoreCase))?.Model.Id;
    }

}
