namespace LocalLlmConsole.Services;

public sealed partial class HelpCatalogService
{
    private string _activeSection = FirstSteps;

    public IReadOnlyList<HelpSectionDefinition> Sections => DefaultSections;
    public IReadOnlyList<HelpArticleDefinition> Articles => DefaultArticles.Select(LocalizeArticle).ToArray();
    public string ActiveSection => _activeSection;

    public HelpSectionDefinition Select(string? sectionKey)
    {
        var definition = DefinitionFor(sectionKey);
        _activeSection = definition.Key;
        return definition;
    }

    public HelpSectionDefinition DefinitionFor(string? sectionKey)
        => DefaultSections.FirstOrDefault(section => string.Equals(section.Key, sectionKey, StringComparison.Ordinal))
            ?? DefaultSections[0];

    public HelpSearchResult Search(string? query, string? sectionKey = null)
    {
        var normalizedQuery = Normalize(query);
        var activeSection = DefinitionFor(sectionKey ?? _activeSection);
        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var localizedArticles = Articles;
        var candidates = tokens.Length == 0
            ? localizedArticles.Where(article => string.Equals(article.SectionKey, activeSection.Key, StringComparison.Ordinal))
            : localizedArticles.Where(article => MatchesEveryToken(article, tokens));
        var articles = candidates
            .OrderByDescending(article => SearchScore(article, tokens))
            .ThenBy(article => article.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new HelpSearchResult((query ?? "").Trim(), activeSection, articles);
    }

    private static bool MatchesEveryToken(HelpArticleDefinition article, IReadOnlyList<string> tokens)
    {
        var searchable = SearchableText(article);
        return tokens.All(searchable.Contains);
    }

    private static int SearchScore(HelpArticleDefinition article, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0) return 0;
        var title = Normalize(article.Title);
        var summary = Normalize(article.Summary);
        var score = 0;
        foreach (var token in tokens)
        {
            if (title.StartsWith(token, StringComparison.Ordinal)) score += 12;
            else if (title.Contains(token, StringComparison.Ordinal)) score += 8;
            if (summary.Contains(token, StringComparison.Ordinal)) score += 4;
            if (article.Keywords.Any(keyword => Normalize(keyword).Contains(token, StringComparison.Ordinal))) score += 2;
        }
        return score;
    }

    private static string SearchableText(HelpArticleDefinition article)
        => Normalize(string.Join(' ',
            article.Title,
            article.Summary,
            string.Join(' ', article.Details),
            string.Join(' ', article.Keywords)));

    private static string Normalize(string? value)
        => string.Join(' ', (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static HelpArticleDefinition LocalizeArticle(HelpArticleDefinition article)
    {
        var prefix = $"Help.Article.{article.Id}";
        return article with
        {
            Title = Localized($"{prefix}.Title", article.Title),
            Summary = Localized($"{prefix}.Summary", article.Summary),
            Details = article.Details
                .Select((detail, index) => Localized($"{prefix}.Detail.{index + 1}", detail))
                .ToArray(),
            Actions = article.Actions
                .Select((action, index) => action with
                {
                    Label = Localized($"{prefix}.Action.{index + 1}", action.Label)
                })
                .ToArray()
        };
    }

    private static string Localized(string key, string fallback)
    {
        var value = Loc.T(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static HelpArticleDefinition Article(
        string id,
        string sectionKey,
        string title,
        string summary,
        IReadOnlyList<string> details,
        IReadOnlyList<HelpActionDefinition> actions,
        IReadOnlyList<string> keywords)
        => new(id, sectionKey, title, summary, details, actions, keywords);

    private static HelpActionDefinition Action(string label, string target)
        => new(label, target);
}
