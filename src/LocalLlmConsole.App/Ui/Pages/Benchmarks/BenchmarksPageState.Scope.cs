using System.Windows.Controls;
using LocalLlmConsole.Models;
using ComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public sealed partial class BenchmarksPageState
{
    public void AddSelectedProfile()
    {
        if (Model?.SelectedItem is not BenchmarkSelectionItem model
            || Profile?.SelectedItem is not BenchmarkSelectionItem profile
            || Runtime?.SelectedItem is not BenchmarkSelectionItem runtime)
            return;
        AddScopeRow(model.Id, profile.Id, runtime.Id);
    }

    public void AddAllProfilesForSelectedModel()
    {
        if (Model?.SelectedItem is not BenchmarkSelectionItem model) return;
        foreach (var profile in _profiles.Where(profile => profile.ModelId.Equals(model.Id, StringComparison.OrdinalIgnoreCase)))
            AddScopeRow(model.Id, profile.Id, "", refresh: false);
        RefreshScopeRows();
    }

    public void RemoveSelectedProfiles()
    {
        if (ScopeProfiles is null) return;
        var selected = ScopeProfiles.SelectedItems.Cast<BenchmarkScopeRow>().ToHashSet();
        _scopeRows.RemoveAll(selected.Contains);
        RefreshScopeRows();
    }

    public void RemoveProfile(BenchmarkScopeRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _scopeRows.RemoveAll(item => item.ProfileId.Equals(row.ProfileId, StringComparison.OrdinalIgnoreCase)
                                     && item.RuntimeId.Equals(row.RuntimeId, StringComparison.OrdinalIgnoreCase));
        RefreshScopeRows();
    }

    public void ClearScopeProfiles()
    {
        _scopeRows.Clear();
        RefreshScopeRows();
    }

    public void ApplyScope(BenchmarkPlan plan)
    {
        _scopeRows.Clear();
        if (plan.ScopeSelections.Count > 0)
        {
            foreach (var selection in plan.ScopeSelections)
                AddScopeRow(selection.ModelId, selection.ProfileId, selection.RuntimeId, refresh: false);
            RefreshScopeRows();
            return;
        }
        var modelIds = plan.AllModels
            ? _models.Select(model => model.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : plan.ModelIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profiles = plan.AllProfiles
            ? _profiles.Where(profile => modelIds.Contains(profile.ModelId))
            : plan.ProfileIds.Count > 0
                ? _profiles.Where(profile => plan.ProfileIds.Contains(profile.Id, StringComparer.OrdinalIgnoreCase))
                : _profiles.Where(profile => modelIds.Contains(profile.ModelId) && profile.IsDefault);
        var runtimeIds = plan.AllRuntimes
            ? _runtimes.Select(runtime => runtime.Id).ToArray()
            : plan.UseProfileRuntime ? [""] : plan.RuntimeIds.ToArray();
        foreach (var profile in profiles)
            foreach (var runtimeId in runtimeIds)
                AddScopeRow(profile.ModelId, profile.Id, runtimeId, refresh: false);
        RefreshScopeRows();
    }

    public void SetProfileItems(IReadOnlyList<NamedModelLaunchProfile> profiles)
    {
        var modelId = (Model?.SelectedItem as BenchmarkSelectionItem)?.Id;
        var items = profiles
            .Where(profile => string.IsNullOrWhiteSpace(modelId) || profile.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(profile => profile.IsDefault)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new BenchmarkSelectionItem(profile.Id, profile.Name))
            .ToArray();
        SetItems(Profile, items);
    }

    private void AddScopeRow(string modelId, string profileId, string runtimeId, bool refresh = true)
    {
        var model = _models.FirstOrDefault(model => model.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        var profile = _profiles.FirstOrDefault(profile => profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (model is null || profile is null) return;
        var effectiveRuntimeId = string.IsNullOrWhiteSpace(runtimeId) ? profile.Settings.RuntimeId : runtimeId;
        if (_scopeRows.Any(row => row.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase)
                                  && EffectiveRuntimeId(row).Equals(effectiveRuntimeId, StringComparison.OrdinalIgnoreCase))) return;
        var runtime = _runtimes.FirstOrDefault(item => item.Id.Equals(effectiveRuntimeId, StringComparison.OrdinalIgnoreCase));
        var runtimeName = runtime is null
            ? string.IsNullOrWhiteSpace(effectiveRuntimeId) ? "Profile runtime (not set)" : "Runtime not found"
            : string.IsNullOrWhiteSpace(runtimeId) ? $"{runtime.Name} (profile)" : runtime.Name;
        var environment = runtime is null ? "" : $"{runtime.Mode}/{runtime.Backend}";
        _scopeRows.Add(new BenchmarkScopeRow(model.Id, model.Name, profile.Id, profile.Name, runtimeId, runtimeName, environment));
        if (refresh) RefreshScopeRows();
    }

    private string EffectiveRuntimeId(BenchmarkScopeRow row)
        => string.IsNullOrWhiteSpace(row.RuntimeId)
            ? _profiles.FirstOrDefault(profile => profile.Id.Equals(row.ProfileId, StringComparison.OrdinalIgnoreCase))?.Settings.RuntimeId ?? ""
            : row.RuntimeId;

    private void RefreshScopeRows()
    {
        if (ScopeProfiles is null) return;
        ScopeProfiles.ItemsSource = null;
        ScopeProfiles.ItemsSource = _scopeRows.ToArray();
    }

    private static void SetItems(ComboBox? combo, IReadOnlyList<BenchmarkSelectionItem> items)
    {
        if (combo is null) return;
        var selected = (combo.SelectedItem as BenchmarkSelectionItem)?.Id;
        combo.ItemsSource = items;
        combo.SelectedItem = items.FirstOrDefault(item => item.Id.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
    }
}
