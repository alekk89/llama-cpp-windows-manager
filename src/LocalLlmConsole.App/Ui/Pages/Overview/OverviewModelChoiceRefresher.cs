namespace LocalLlmConsole;

public sealed record OverviewModelChoiceData(
    IReadOnlyList<ModelRecord> Models,
    ModelGroupSnapshot Groups,
    IReadOnlyList<NamedModelLaunchProfile> Profiles,
    IReadOnlyDictionary<string, string> SizeLabels);

/// <summary>The Overview selection disappeared and was replaced by <see cref="Fallback"/> (or cleared).</summary>
public sealed record OverviewSelectionFallback(OverviewModelChoice Previous, OverviewModelChoice? Fallback)
{
    public string Message => Fallback is null
        ? Loc.T("Overview.SelectionRemoved", Previous.Name)
        : Loc.T("Overview.SelectionFallback", Previous.Name, Fallback.Name);
}

/// <summary>
/// Replaces the Overview model choices while keeping the user's selection. The selection is
/// read after the data has loaded, so a change the user made during the refresh wins.
/// </summary>
public sealed class OverviewModelChoiceRefresher
{
    private readonly OverviewPageViewModel _viewModel;
    private readonly OverviewPageState _page;

    public OverviewModelChoiceRefresher(OverviewPageViewModel viewModel, OverviewPageState page)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _page = page ?? throw new ArgumentNullException(nameof(page));
    }

    public async Task<OverviewSelectionFallback?> RefreshAsync(
        Func<Task<OverviewModelChoiceData>> loadAsync,
        Func<string?> defaultSelectedId)
    {
        ArgumentNullException.ThrowIfNull(loadAsync);
        ArgumentNullException.ThrowIfNull(defaultSelectedId);

        var data = await loadAsync();
        var previous = _page.SelectedChoice(_viewModel.ModelChoices);
        var selectedId = string.IsNullOrWhiteSpace(previous?.Id) ? defaultSelectedId() : previous.Id;
        _viewModel.ReplaceModels(data.Models, data.Groups.Groups, data.Groups.Assignments, data.Profiles, data.SizeLabels);
        var selected = _page.SelectModelChoice(selectedId, _viewModel.ModelChoices);
        return previous is not null && !string.Equals(previous.Id, selected?.Id, StringComparison.OrdinalIgnoreCase)
            ? new OverviewSelectionFallback(previous, selected)
            : null;
    }
}
