using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        var applyRequests = 0;
        var authenticationDisabledNotices = 0;
        var liveUiScales = new List<int>();
        var liveFontScales = new List<int>();
        var settingsControls = LocalLlmConsole.SettingsPageFactory.Create(new LocalLlmConsole.SettingsPageRequest(
            settingsViewModel.Rows,
            persistedSettings.ThemeMode,
            new LocalLlmConsole.SettingsPageActions(
                (_, _) => { },
                (_, _) => { },
                (_, _) => { },
                (_, _) => { },
                () => applyRequests++)));
        var settingsState = new LocalLlmConsole.SettingsPageState();
        settingsState.Apply(
            settingsControls,
            settingsViewModel.Rows,
            () => applyRequests++,
            () => authenticationDisabledNotices++,
            liveUiScales.Add,
            liveFontScales.Add);
        Assert.Equal(36, settingsControls.SettingsGrid.RowHeight);
        Assert.Equal(
            ["uiScalePercent", "fontScalePercent", "showOverviewModelSection", "showOverviewLiveRuntimeLog", "showModelsHuggingFace"],
            settingsViewModel.Rows.Where(row => row.Group == "UI").Select(row => row.Key));
        Assert.All(settingsViewModel.Rows.Where(row => row.Group == "UI" && row.Type != "slider"), row =>
        {
            Assert.Contains(row.Value, new[] { "Show", "Hide" });
            Assert.Equal(new[] { "Show", "Hide" }, row.Options);
        });
        var uiScale = Assert.Single(settingsViewModel.Rows, row => row.Key == "uiScalePercent");
        Assert.Equal("slider", uiScale.Type);
        Assert.Equal("100", uiScale.Value);
        Assert.Empty(uiScale.Options);
        var fontScale = Assert.Single(settingsViewModel.Rows, row => row.Key == "fontScalePercent");
        Assert.Equal("Text scale", fontScale.Label);
        Assert.Equal("slider", fontScale.Type);
        Assert.Equal("100", fontScale.Value);
        Assert.Empty(fontScale.Options);
        settingsControls.Root.Measure(new Size(900, 900));
        settingsControls.Root.Arrange(new Rect(0, 0, 900, 900));
        settingsControls.Root.UpdateLayout();
        Assert.Equal(2, settingsControls.SettingsColumns.ColumnDefinitions.Count);
        Assert.Equal(
            LocalLlmConsole.Localization.Loc.T("Settings.StartupProfiles.Title"),
            settingsControls.StartupProfiles.ProfileCombo.ToolTip);
        Assert.IsType<LocalLlmConsole.SearchableComboBox>(settingsControls.StartupProfiles.ProfileCombo);
        Assert.Empty(settingsControls.StartupProfiles.ProfileCombo.Items);
        Assert.False(settingsControls.StartupProfiles.AddButton.IsEnabled);
        Assert.Equal(Visibility.Visible, settingsControls.StartupProfiles.EmptyText.Visibility);
        Assert.Equal(Visibility.Collapsed, settingsControls.StartupProfiles.SelectedGrid.Visibility);
        Assert.All(settingsControls.SettingsColumns.ColumnDefinitions, column => Assert.True(column.Width.IsStar));
        var settingsColumnStacks = settingsControls.SettingsColumns.Children.OfType<StackPanel>().ToArray();
        Assert.Equal(2, settingsColumnStacks.Length);
        Assert.Equal(5, settingsColumnStacks[0].Children.Count);
        Assert.Equal(5, settingsColumnStacks[1].Children.Count);
        Assert.Equal(settingsControls.SettingsColumns.ColumnDefinitions[0].ActualWidth,
            settingsControls.SettingsColumns.ColumnDefinitions[1].ActualWidth, precision: 1);
        var groupOrder = settingsViewModel.Rows.Select(row => row.Group).Distinct().ToList();
        Assert.Equal(820, LocalLlmConsole.SettingsPageResponsiveCoordinator.SingleColumnThreshold);
        settingsControls.Root.Width = 700;
        settingsControls.Root.Measure(new Size(700, 1200));
        settingsControls.Root.Arrange(new Rect(0, 0, 700, 1200));
        settingsControls.Root.UpdateLayout();
        Assert.Equal(10, settingsColumnStacks[0].Children.Count);
        Assert.Empty(settingsColumnStacks[1].Children);
        Assert.Equal(Visibility.Collapsed, settingsColumnStacks[1].Visibility);
        Assert.Equal(0, settingsControls.SettingsColumns.ColumnDefinitions[1].Width.Value);
        Assert.Equal(
            groupOrder,
            settingsColumnStacks[0].Children.Cast<FrameworkElement>()
                .SelectMany(section => VisualDescendants<DataGrid>(section))
                .Where(grid => grid.ItemsSource?.Cast<object>().FirstOrDefault() is EditableSettingRow)
                .Select(grid => grid.ItemsSource.Cast<EditableSettingRow>().First().Group));
        settingsControls.Root.Width = double.NaN;
        settingsControls.Root.Measure(new Size(900, 900));
        settingsControls.Root.Arrange(new Rect(0, 0, 900, 900));
        settingsControls.Root.UpdateLayout();
        Assert.Equal(5, settingsColumnStacks[0].Children.Count);
        Assert.Equal(5, settingsColumnStacks[1].Children.Count);
        Assert.Equal(Visibility.Visible, settingsColumnStacks[1].Visibility);
        Assert.All(settingsControls.SettingsColumns.ColumnDefinitions, column => Assert.True(column.Width.IsStar));
        var settingsGrids = VisualDescendants<DataGrid>(settingsControls.Root).ToArray();
        var uiSettingsGrid = Assert.Single(settingsGrids, grid =>
            grid.ItemsSource.Cast<EditableSettingRow>().Any(row => row.Group == "UI"));
        Assert.Equal(2, uiSettingsGrid.Columns.Count);
        Assert.Contains(VisualDescendants<DataGrid>(settingsColumnStacks[0]), grid => ReferenceEquals(grid, uiSettingsGrid));
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
        Assert.All(settingChoices.Where(combo => combo != settingsControls.ThemeCombo
            && combo.DataContext is EditableSettingRow), combo =>
        {
            Assert.True(double.IsNaN(combo.Width));
            Assert.Equal(28, combo.Height);
            Assert.Equal(28, combo.MinHeight);
            Assert.Equal(HorizontalAlignment.Stretch, combo.HorizontalAlignment);
            Assert.Equal(new Thickness(0, 0, 3, 0), combo.Margin);
        });
        var scaleSlider = Assert.Single(
            VisualDescendants<Slider>(settingsControls.Root),
            slider => slider.DataContext is EditableSettingRow { Key: "uiScalePercent" });
        Assert.Equal(75, scaleSlider.Minimum);
        Assert.Equal(175, scaleSlider.Maximum);
        Assert.Equal(1, scaleSlider.TickFrequency);
        Assert.Equal(1, scaleSlider.SmallChange);
        Assert.Equal(25, scaleSlider.LargeChange);
        Assert.True(scaleSlider.IsSnapToTickEnabled);
        Assert.Equal(100, scaleSlider.Value);
        Assert.Equal(
            UpdateSourceTrigger.PropertyChanged,
            BindingOperations.GetBinding(scaleSlider, Slider.ValueProperty)?.UpdateSourceTrigger);
        Assert.Contains(
            VisualDescendants<TextBlock>(settingsControls.Root),
            text => text.DataContext == uiScale && text.Text == "100%");
        scaleSlider.Value = 150;
        settingsControls.Root.UpdateLayout();
        Assert.Equal("150", uiScale.Value);
        Assert.Equal(0, applyRequests);
        Assert.Equal([150], liveUiScales);
        Assert.Contains(
            VisualDescendants<TextBlock>(settingsControls.Root),
            text => text.DataContext == uiScale && text.Text == "150%");
        scaleSlider.Value = 126;
        scaleSlider.Value = 175;
        scaleSlider.Value = 125;
        Assert.Equal("125", uiScale.Value);
        Assert.Equal([150, 126, 175, 125], liveUiScales);
        Assert.Equal(0, applyRequests);
        var fontScaleSlider = Assert.Single(
            VisualDescendants<Slider>(settingsControls.Root),
            slider => slider.DataContext is EditableSettingRow { Key: "fontScalePercent" });
        fontScaleSlider.Value = 130;
        Assert.Equal("130", fontScale.Value);
        Assert.Equal([130], liveFontScales);
        Assert.Equal(0, applyRequests);
        scaleSlider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
            Source = scaleSlider
        });
        Assert.Equal(1, applyRequests);
        applyRequests = 0;
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

    [Fact]
    public async Task StartupProfileSettingSupportsAddingAndRemovingSeveralProfiles()
    {
        await RunStaAsync(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "wpf-startup-profile-settings");
            var settings = AppSettings.CreateDefault(root);
            var now = DateTimeOffset.UtcNow;
            var model = new ModelRecord("model-1", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", now);
            StartupLaunchProfileChoice Choice(string id, string name, int port) => new(
                model,
                new NamedModelLaunchProfile(
                    id,
                    model.Id,
                    name,
                    ModelLaunchSettings.FromAppSettings(settings with { Port = port }),
                    now,
                    IsDefault: name == "Default"));
            var first = Choice("profile-1", "Default", 8091);
            var second = Choice("profile-2", "Long context", 8092);
            var selected = Choice("profile-3", "Vision", 8093);
            var added = new List<string>();
            var removed = new List<string>();
            var viewModel = new SettingsPageViewModel();
            viewModel.ReplaceRows(new SettingsPageDefinitionService().BuildRows(settings));
            var controls = LocalLlmConsole.SettingsPageFactory.Create(new LocalLlmConsole.SettingsPageRequest(
                viewModel.Rows,
                settings.ThemeMode,
                new LocalLlmConsole.SettingsPageActions((_, _) => { }, (_, _) => { }, (_, _) => { }, (_, _) => { }, () => { }),
                new StartupLaunchProfileSettingsSnapshot([first, second], [selected]),
                new LocalLlmConsole.StartupLaunchProfileSettingsActions(
                    profileId => { added.Add(profileId); return Task.CompletedTask; },
                    profileId => { removed.Add(profileId); return Task.CompletedTask; },
                    () => Task.FromResult(new StartupLaunchProfileSettingsSnapshot([first, second], [selected])),
                    action => action())));
            controls.Root.Measure(new Size(900, 900));
            controls.Root.Arrange(new Rect(0, 0, 900, 900));
            controls.Root.UpdateLayout();

            Assert.Equal(2, controls.StartupProfiles.ProfileCombo.Items.Count);
            var startupProfileCombo = Assert.IsType<LocalLlmConsole.SearchableComboBox>(controls.StartupProfiles.ProfileCombo);
            Assert.Contains("Long context", startupProfileCombo.SearchTextSelector(second));
            Assert.Equal(28, startupProfileCombo.ActualHeight);
            Assert.Equal(startupProfileCombo.ActualHeight, controls.StartupProfiles.AddButton.ActualHeight);
            Assert.Equal(new Thickness(0), startupProfileCombo.Margin);
            Assert.Single(controls.StartupProfiles.SelectedGrid.Items);
            Assert.Equal(Visibility.Collapsed, controls.StartupProfiles.EmptyText.Visibility);
            Assert.Equal(36, Assert.IsType<LocalLlmConsole.ResponsiveActionDataGridColumn>(controls.StartupProfiles.SelectedGrid.Columns[^1]).MinWidth);
            controls.StartupProfiles.ProfileCombo.SelectedItem = second;
            Assert.True(controls.StartupProfiles.AddButton.IsEnabled);
            controls.StartupProfiles.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([second.ProfileId], added);
            controls.Root.Measure(new Size(900, 900));
            controls.Root.Arrange(new Rect(0, 0, 900, 900));
            controls.Root.UpdateLayout();

            var remove = Assert.Single(
                VisualDescendants<Button>(controls.StartupProfiles.SelectedGrid),
                button => AutomationProperties.GetName(button) == LocalLlmConsole.Localization.Loc.T("Models.ActionBtn.Remove"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([selected.ProfileId], removed);
        });
    }

    [Fact]
    public async Task UiScaleAppliesWithoutStackingTransforms()
    {
        await RunStaAsync(() =>
        {
            var content = new Grid();
            var window = new Window { Content = content };

            ApplicationUiScaleService.ApplyToWindow(window, 125);
            var firstScale = Assert.IsType<System.Windows.Media.ScaleTransform>(content.LayoutTransform);
            Assert.Equal(1.25, firstScale.ScaleX);
            Assert.Equal(1.25, firstScale.ScaleY);

            ApplicationUiScaleService.ApplyToWindow(window, 150);
            var secondScale = Assert.IsType<System.Windows.Media.ScaleTransform>(content.LayoutTransform);
            Assert.Equal(1.5, secondScale.ScaleX);
            Assert.Equal(1.5, secondScale.ScaleY);

            ApplicationUiScaleService.ApplyToWindow(window, 100);
            Assert.True(content.LayoutTransform.Value.IsIdentity);
        });
    }

    [Fact]
    public async Task FontScaleChangesTextWithoutScalingLayoutAndDoesNotStack()
    {
        await RunStaAsync(() =>
        {
            var text = new TextBlock { FontSize = 20, Text = "Scaled" };
            var glyphButton = new Button { Content = "★" };
            InlineGlyphButtonVisual.Configure(glyphButton);
            var fixedLayout = new Border { Width = 80, Height = 24 };
            var content = new Grid();
            content.Children.Add(text);
            content.Children.Add(glyphButton);
            content.Children.Add(fixedLayout);
            var window = new Window { Content = content };
            var originalWindowFontSize = window.FontSize;

            ApplicationFontScaleService.ApplyToWindow(window, 125);
            Assert.Equal(25, text.FontSize);
            Assert.Equal(13, glyphButton.FontSize);
            Assert.Equal(originalWindowFontSize * 1.25, window.FontSize);
            Assert.Equal(80, fixedLayout.Width);
            Assert.Equal(24, fixedLayout.Height);
            Assert.True(content.LayoutTransform.Value.IsIdentity);

            ApplicationFontScaleService.ApplyToWindow(window, 150);
            Assert.Equal(30, text.FontSize);
            Assert.Equal(13, glyphButton.FontSize);
            Assert.Equal(originalWindowFontSize * 1.5, window.FontSize);

            ApplicationFontScaleService.ApplyToWindow(window, 100);
            Assert.Equal(20, text.FontSize);
            Assert.Equal(originalWindowFontSize, window.FontSize);
            Assert.Equal(80, fixedLayout.Width);
            Assert.Equal(24, fixedLayout.Height);
        });
    }

    [Fact]
    public async Task FontScaleAutomaticallyAppliesOnlyToNewFontBearingElements()
    {
        await RunStaAsync(() =>
        {
            var text = new TextBlock { FontSize = 20, Text = "Loaded later" };
            var layout = new Border { Width = 80, Height = 24, Child = text };
            var window = new Window { Content = layout, ShowInTaskbar = false };
            try
            {
                ApplicationFontScaleService.Apply(125);
                window.Show();
                window.UpdateLayout();

                Assert.Equal(25, text.FontSize);
                Assert.Equal(80, layout.Width);
                Assert.Equal(24, layout.Height);
            }
            finally
            {
                window.Close();
                ApplicationFontScaleService.Apply(100);
            }
        });
    }
}
