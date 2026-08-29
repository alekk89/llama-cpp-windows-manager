using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public abstract partial class WpfUiTestBase
{
    protected static void AssertSettingsAndHelpSurfaces(
        SettingsPageViewModel settingsViewModel,
        AppSettings persistedSettings)
    {
        var settingsControls = LocalLlmConsole.SettingsPageFactory.Create(new LocalLlmConsole.SettingsPageRequest(
            settingsViewModel.Rows,
            persistedSettings.ThemeMode,
            new LocalLlmConsole.SettingsPageActions(
                (_, _) => { },
                (_, _) => { },
                (_, _) => { },
                (_, _) => { })));
        var settingsState = new LocalLlmConsole.SettingsPageState();
        var applyRequests = 0;
        var authenticationDisabledNotices = 0;
        settingsState.Apply(
            settingsControls,
            settingsViewModel.Rows,
            () => applyRequests++,
            () => authenticationDisabledNotices++);
        Assert.Equal(36, settingsControls.SettingsGrid.RowHeight);
        Assert.Equal(
            ["showOverviewModelSection", "showOverviewLiveRuntimeLog", "showModelsHuggingFace"],
            settingsViewModel.Rows.Where(row => row.Group == "UI").Select(row => row.Key));
        Assert.All(settingsViewModel.Rows.Where(row => row.Group == "UI"), row =>
        {
            Assert.Contains(row.Value, new[] { "Show", "Hide" });
            Assert.Equal(new[] { "Show", "Hide" }, row.Options);
        });
        settingsControls.Root.Measure(new Size(900, 900));
        settingsControls.Root.Arrange(new Rect(0, 0, 900, 900));
        settingsControls.Root.UpdateLayout();
        Assert.Equal(2, settingsControls.SettingsColumns.ColumnDefinitions.Count);
        Assert.All(settingsControls.SettingsColumns.ColumnDefinitions, column => Assert.True(column.Width.IsStar));
        var settingsColumnStacks = settingsControls.SettingsColumns.Children.OfType<StackPanel>().ToArray();
        Assert.Equal(2, settingsColumnStacks.Length);
        Assert.Equal(5, settingsColumnStacks[0].Children.Count);
        Assert.Equal(4, settingsColumnStacks[1].Children.Count);
        Assert.Equal(settingsControls.SettingsColumns.ColumnDefinitions[0].ActualWidth,
            settingsControls.SettingsColumns.ColumnDefinitions[1].ActualWidth, precision: 1);
        var settingsGrids = VisualDescendants<DataGrid>(settingsControls.Root).ToArray();
        var uiSettingsGrid = Assert.Single(settingsGrids, grid =>
            grid.ItemsSource.Cast<EditableSettingRow>().Any(row => row.Group == "UI"));
        Assert.Equal(2, uiSettingsGrid.Columns.Count);
        Assert.Contains(VisualDescendants<DataGrid>(settingsColumnStacks[1]), grid => ReferenceEquals(grid, uiSettingsGrid));
        var networkSettingsGrid = Assert.Single(settingsGrids, grid =>
            grid.ItemsSource.Cast<EditableSettingRow>().Any(row => row.Key == "modelApiKey"));
        Assert.Equal(2, networkSettingsGrid.Columns.Count);
        var electricitySettingsGrid = Assert.Single(settingsGrids, grid =>
            grid.ItemsSource.Cast<EditableSettingRow>().Any(row => row.Key == "electricityDayRatePerKwh"));
        Assert.Equal(6, electricitySettingsGrid.Items.Count);
        var idleEnergyTracking = Assert.Single(
            electricitySettingsGrid.ItemsSource.Cast<EditableSettingRow>(),
            row => row.Key == "trackGpuEnergyWhileIdle");
        Assert.Equal("No", idleEnergyTracking.Value);
        Assert.Equal(new[] { "Yes", "No" }, idleEnergyTracking.Options);
        var benchmarkSettingsGrid = Assert.Single(settingsGrids, grid =>
            grid.ItemsSource.Cast<EditableSettingRow>().Any(row => row.Key == "benchmarkPreventSystemSleep"));
        Assert.Equal(2, benchmarkSettingsGrid.Items.Count);
        Assert.Equal(
            new[] { "benchmarkPreventSystemSleep", "benchmarkStopActiveSessions" },
            benchmarkSettingsGrid.ItemsSource.Cast<EditableSettingRow>().Select(row => row.Key));
        var apiKeyAuthentication = Assert.Single(
            networkSettingsGrid.ItemsSource.Cast<EditableSettingRow>(),
            row => row.Key == "requireApiKeyAuth");
        Assert.Equal("Enable", apiKeyAuthentication.Value);
        Assert.Equal(new[] { "Enable", "Disable" }, apiKeyAuthentication.Options);
        var settingChoices = VisualDescendants<ComboBox>(settingsControls.Root)
            .Where(combo => combo.ItemsSource is not null)
            .ToArray();
        Assert.NotEmpty(settingChoices);
        Assert.All(settingChoices.Where(combo => combo != settingsControls.ThemeCombo), combo =>
        {
            Assert.True(double.IsNaN(combo.Width));
            Assert.Equal(28, combo.Height);
            Assert.Equal(28, combo.MinHeight);
            Assert.Equal(HorizontalAlignment.Stretch, combo.HorizontalAlignment);
            Assert.Equal(new Thickness(0, 0, 3, 0), combo.Margin);
        });
        Assert.Contains(VisualDescendants<TextBox>(settingsControls.Root), textBox =>
            textBox.Height == 28
            && textBox.MinHeight == 28
            && textBox.Padding == new Thickness(8, 2, 8, 2)
            && textBox.VerticalContentAlignment == VerticalAlignment.Center);
        Assert.All(
            VisualDescendants<TextBox>(settingsControls.Root)
                .Where(textBox => textBox.DataContext is EditableSettingRow { Type: "text" }),
            textBox => Assert.Equal(
                LocalLlmConsole.SettingsGridColumnFactory.TextInputCommitDelayMilliseconds,
                BindingOperations.GetBinding(textBox, TextBox.TextProperty)?.Delay));
        var networkActions = VisualDescendants<Button>(networkSettingsGrid)
            .Select(button => button.Content?.ToString())
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToArray();
        Assert.Contains("Show", networkActions);
        Assert.Contains("Copy", networkActions);
        Assert.Contains("Generate", networkActions);
        Assert.DoesNotContain(VisualDescendants<Button>(settingsControls.Root), button =>
            Equals(button.Content, LocalLlmConsole.Localization.Loc.T("Settings.SaveSettingsButton")));
        Assert.Contains(VisualDescendants<TextBlock>(settingsControls.Root), text =>
            text.Text == LocalLlmConsole.Localization.Loc.T("Settings.AutoApplyHint"));

        var helpCatalog = new HelpCatalogService();
        var helpTarget = "";
        var helpController = new LocalLlmConsole.HelpPageController(helpCatalog, target => helpTarget = target);
        var helpPage = helpController.Create();
        helpPage.Content.Measure(new Size(900, 680));
        helpPage.Content.Arrange(new Rect(0, 0, 900, 680));
        helpPage.Content.UpdateLayout();
        Assert.Equal(LocalLlmConsole.Localization.Loc.T("Help.Search.AutomationName"), AutomationProperties.GetName(helpPage.Controls.SearchBox));
        Assert.Equal(6, helpPage.Controls.SectionButtons.Count);
        Assert.Equal(3, helpPage.Controls.ResultsHost.Children.Count);
        Assert.All(
            helpPage.Controls.ResultsHost.Children.OfType<Border>(),
            card => Assert.False(Assert.IsType<Expander>(card.Child).IsExpanded));

        helpPage.Controls.SearchBox.Text = "api key";
        helpPage.Content.UpdateLayout();
        Assert.Equal(Visibility.Visible, helpPage.Controls.ClearSearchButton.Visibility);
        Assert.Contains("searching all topics", helpPage.Controls.ResultsSummary.Text, StringComparison.Ordinal);
        var apiArticle = Assert.Single(
            VisualDescendants<Expander>(helpPage.Content),
            expander => AutomationProperties.GetName(expander) == LocalLlmConsole.Localization.Loc.T("Help.Article.network-and-key.Title"));
        apiArticle.IsExpanded = true;
        helpPage.Content.UpdateLayout();
        var openSettings = Assert.Single(
            VisualDescendants<Button>(apiArticle),
            button => Equals(button.Content, LocalLlmConsole.Localization.Loc.T("Help.Article.network-and-key.Action.1")));
        openSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("settings", helpTarget);

        helpPage.Controls.SearchBox.Text = "phrase-that-does-not-exist";
        Assert.Contains(
            VisualDescendants<TextBlock>(helpPage.Content),
            text => text.Text == LocalLlmConsole.Localization.Loc.T("Help.Search.NoMatchTitle"));
        helpPage.Controls.ClearSearchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("", helpPage.Controls.SearchBox.Text);
        helpPage.Controls.SectionButtons["models"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        helpPage.Content.Measure(new Size(900, 680));
        helpPage.Content.Arrange(new Rect(0, 0, 900, 680));
        helpPage.Content.UpdateLayout();
        Assert.Equal("models", helpCatalog.ActiveSection);
        Assert.All(
            helpPage.Controls.ResultsHost.Children.OfType<Border>(),
            card => Assert.Contains(VisualDescendants<TextBlock>(card), text => text.Text == "MODELS"));

        var idleRow = Assert.Single(settingsViewModel.Rows, row => row.Key == "autoUnloadIdleMinutes");
        idleRow.Value = "15";
        Assert.Equal(1, applyRequests);
        settingsControls.ThemeCombo.SelectedItem = "dark";
        Assert.Equal(2, applyRequests);

        apiKeyAuthentication.Value = "Disable";
        Assert.Equal(1, authenticationDisabledNotices);
        Assert.Equal(
            "Local only",
            settingsViewModel.Rows.Single(row => row.Key == "modelAccessMode").Value);
        Assert.Equal("", settingsViewModel.Rows.Single(row => row.Key == "modelApiKey").Value);
    }
}

public sealed class WpfSettingsAndHelpTests : WpfUiTestBase
{
    [Fact]
    public async Task SettingsAndHelpRenderIndependently()
    {
        await RunStaAsync(() =>
        {
            var settings = AppSettings.CreateDefault(Path.Combine(Path.GetTempPath(), "wpf-settings-smoke")) with
            {
                ModelApiKey = "synthetic-key"
            };
            var viewModel = new SettingsPageViewModel();
            viewModel.ReplaceRows(new SettingsPageDefinitionService().BuildRows(settings));
            AssertSettingsAndHelpSurfaces(viewModel, settings);
        });
    }
}
