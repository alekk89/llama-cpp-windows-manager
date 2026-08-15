using LocalLlmConsole.Services;
using LocalLlmConsole.Localization;

namespace LocalLlmConsole;

public sealed class HelpPageState
{
    private readonly HelpPageControls _controls;

    public HelpPageState(HelpPageControls controls)
        => _controls = controls ?? throw new ArgumentNullException(nameof(controls));

    public string SearchText => _controls.SearchBox.Text;

    public void SetSearchText(string value)
    {
        var next = value ?? "";
        if (!string.Equals(_controls.SearchBox.Text, next, StringComparison.Ordinal))
            _controls.SearchBox.Text = next;
    }

    public void Apply(
        HelpSearchResult result,
        IReadOnlyDictionary<string, HelpSectionDefinition> sections,
        Action<string> navigate)
    {
        foreach (var (key, button) in _controls.SectionButtons)
            button.Tag = string.Equals(key, result.ActiveSection.Key, StringComparison.Ordinal) ? "Active" : null;

        _controls.ResultsSummary.Text = result.IsSearch
            ? Loc.T("Help.Search.ResultsFor", result.Articles.Count, result.Query)
            : Loc.T("Help.Search.TopicCount", result.Articles.Count, Loc.T(result.ActiveSection.SummaryKey));
        HelpResultsFactory.Populate(_controls.ResultsHost, result, sections, navigate);
    }

    public void FocusSearch()
    {
        _controls.SearchBox.Focus();
        _controls.SearchBox.SelectAll();
    }

}
