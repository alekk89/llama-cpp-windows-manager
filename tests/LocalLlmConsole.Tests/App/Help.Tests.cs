using LocalLlmConsole.Services;
using LocalLlmConsole.Localization;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class HelpTests : ManagerRegressionTestBase
{
    [Fact]
    public void HelpCatalogIsSearchableAndUsesValidNavigationTargets()
    {
        Loc.LoadLanguage("en");
        try
        {
            var catalog = new HelpCatalogService();
            var navigation = new HelpNavigationApplicationService();

            Assert.NotEmpty(catalog.Sections);
            Assert.NotEmpty(catalog.Articles);
            Assert.Equal(catalog.Sections.Count, catalog.Sections.Select(section => section.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(catalog.Articles.Count, catalog.Articles.Select(article => article.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.All(catalog.Articles, article =>
            {
                Assert.Contains(catalog.Sections, section => section.Key == article.SectionKey);
                Assert.False(string.IsNullOrWhiteSpace(article.Title));
                Assert.False(string.IsNullOrWhiteSpace(article.Summary));
                Assert.NotEmpty(article.Details);
                Assert.All(article.Details, detail => Assert.False(string.IsNullOrWhiteSpace(detail)));
                Assert.All(article.Actions, action => Assert.True(navigation.Plan(action.Target).ShouldNavigate, action.Target));
            });

            var modelKey = Assert.Single(catalog.Search("api key").Articles, article => article.Id == "network-and-key");
            Assert.Equal("settings", modelKey.SectionKey);
            Assert.Contains(catalog.Search("control token model key").Articles, article => article.Id == "two-apis");
            Assert.Contains(catalog.Search("copy gguf scan folder").Articles, article => article.Id == "manual-folders");
            Assert.Contains(catalog.Search("401 wrong key").Articles, article => article.Id == "authentication-errors");
            Assert.Contains(catalog.Search("cuda build toolchain").Articles, article => article.Id == "source-builds");
            Assert.Empty(catalog.Search("phrase-that-does-not-exist").Articles);

            catalog.Select("models");
            var modelSection = catalog.Search("");
            Assert.Equal("models", modelSection.ActiveSection.Key);
            Assert.NotEmpty(modelSection.Articles);
            Assert.All(modelSection.Articles, article => Assert.Equal("models", article.SectionKey));

            var crossSectionSearch = catalog.Search("api");
            Assert.True(crossSectionSearch.IsSearch);
            Assert.Contains(crossSectionSearch.Articles, article => article.SectionKey == "settings");
            Assert.Contains(crossSectionSearch.Articles, article => article.SectionKey == "maintenance");
        }
        finally
        {
            Loc.LoadLanguage("en");
        }
    }

    [Fact]
    public void HelpCatalogLocalizesArticleContentAndSearchesTheLocalizedText()
    {
        try
        {
            Loc.LoadLanguage("de");
            var catalog = new HelpCatalogService();
            var quickStart = Assert.Single(catalog.Articles, article => article.Id == "quick-start");

            Assert.Equal(Loc.T("Help.Article.quick-start.Title"), quickStart.Title);
            Assert.NotEqual("Start a model in four steps", quickStart.Title);
            Assert.Contains(catalog.Search(quickStart.Title).Articles, article => article.Id == quickStart.Id);
        }
        finally
        {
            Loc.LoadLanguage("en");
        }
    }
}
