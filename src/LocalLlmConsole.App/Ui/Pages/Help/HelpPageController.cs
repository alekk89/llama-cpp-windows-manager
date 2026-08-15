using LocalLlmConsole.Services;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;

namespace LocalLlmConsole;

public sealed class HelpPageController
{
    private readonly HelpCatalogService _catalog;
    private readonly Action<string> _navigate;
    private readonly IReadOnlyDictionary<string, HelpSectionDefinition> _sections;
    private HelpPageState? _state;
    private string _query = "";
    private bool _updating;

    public HelpPageController(HelpCatalogService catalog, Action<string> navigate)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        _sections = catalog.Sections.ToDictionary(section => section.Key, StringComparer.Ordinal);
    }

    public HelpPageBuildResult Create()
    {
        var active = _catalog.DefinitionFor(_catalog.ActiveSection);
        var page = HelpPageFactory.Create(new HelpPageRequest(
            active,
            _catalog.Sections,
            new HelpPageActions(SelectSection, Search)));
        _state = new HelpPageState(page.Controls);
        page.Content.PreviewKeyDown += HandlePreviewKeyDown;
        Apply();
        return page;
    }

    private void HandlePreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == WpfKey.F && WpfKeyboard.Modifiers.HasFlag(WpfModifierKeys.Control))
        {
            _state?.FocusSearch();
            e.Handled = true;
            return;
        }
        if (e.Key != WpfKey.Escape || string.IsNullOrWhiteSpace(_query)) return;

        _query = "";
        _updating = true;
        try
        {
            _state?.SetSearchText("");
        }
        finally
        {
            _updating = false;
        }
        Apply();
        e.Handled = true;
    }

    private void SelectSection(string sectionKey)
    {
        if (_updating) return;
        _catalog.Select(sectionKey);
        _query = "";
        _updating = true;
        try
        {
            _state?.SetSearchText("");
        }
        finally
        {
            _updating = false;
        }
        Apply();
    }

    private void Search(string query)
    {
        if (_updating) return;
        _query = query ?? "";
        Apply();
    }

    private void Apply()
    {
        var result = _catalog.Search(_query);
        _state?.Apply(result, _sections, _navigate);
    }
}
