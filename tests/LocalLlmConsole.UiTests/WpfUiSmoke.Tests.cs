using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class WpfUiSmokeTests
{
    [Fact]
    public async Task CoreWpfSurfacesComposeResizeFilterAndExposeSessionUnloadOnStaThread()
    {
        await RunStaAsync(() =>
        {
            LocalLlmConsole.Localization.Loc.LoadLanguage("en");
            var app = new LocalLlmConsole.App();
            app.InitializeComponent();

            var window = new LocalLlmConsole.MainWindow
            {
                Width = 1024,
                Height = 680
            };
            try
            {
                var windowContent = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                windowContent.Measure(new Size(1024, 680));
                windowContent.Arrange(new Rect(0, 0, 1024, 680));
                windowContent.UpdateLayout();

                var appStatusText = Assert.IsType<TextBlock>(window.FindName("AppStatusText"));
                var helpButton = Assert.IsType<Button>(window.FindName("HelpNavButton"));
                var appVersionText = Assert.IsType<TextBlock>(window.FindName("AppVersionText"));
                var statusCard = Assert.IsType<Border>(Assert.IsType<StackPanel>(appStatusText.Parent).Parent);
                var helpY = helpButton.TranslatePoint(new Point(0, 0), windowContent).Y;
                var statusY = statusCard.TranslatePoint(new Point(0, 0), windowContent).Y;
                Assert.True(statusY > helpY + helpButton.ActualHeight, $"Help bottom {helpY + helpButton.ActualHeight}, status top {statusY}.");
                Assert.True(statusY + statusCard.ActualHeight <= 680, $"Status bottom {statusY + statusCard.ActualHeight}.");
                Assert.Equal("v2.1.0", appVersionText.Text);
                Assert.All(
                    new[] { "OverviewNavButton", "ModelsNavButton", "RuntimesNavButton", "HelpNavButton" }
                        .Select(name => Assert.IsType<Button>(window.FindName(name))),
                    button => Assert.True(button.MinHeight >= 40));

                Assert.Null(window.FindName("ControlApiNavButton"));

                var settings = AppSettings.CreateDefault(Path.Combine(Path.GetTempPath(), "wpf-smoke"));
                var panelState = new LocalLlmConsole.LaunchSettingsPanelState();
                var controlPlan = new LaunchSettingsControlStateService().Build(new LaunchSettingsControlStateRequest(
                    ShowAdvancedSections: true,
                    RuntimeBackend.Cpu,
                    VisionLaunchSettingsAvailable: true,
                    SpeculativeType: "none"));
                var panel = LocalLlmConsole.LaunchSettingsPanelFactory.Create(new LocalLlmConsole.LaunchSettingsPanelRequest(
                    settings,
                    [new RuntimeChoice("cpu", "Official CPU", RuntimeBackend.Cpu, RuntimeMode.Native, "llama-server.exe")],
                    ShowAdvancedLaunchSettings: true,
                    RuntimeSelectionChanged: () => { },
                    AdvancedSettingsChanged: _ => { },
                    LaunchSettingsSearchChanged: () => panelState.ApplyControlState(controlPlan),
                    SaveForModelAsync: () => Task.CompletedTask,
                    SaveDefaultsAsync: () => Task.CompletedTask,
                    ResetDefaults: () => { },
                    SaveAsNewAsync: () => Task.CompletedTask,
                    ChooseVisionProjectorAsync: () => Task.CompletedTask,
                    ChooseDraftModelAsync: () => Task.CompletedTask,
                    ChooseMtpHeadAsync: () => Task.CompletedTask,
                    SaveAsNewNameChanged: () => { },
                    ChooseAdditionalFile: _ => null,
                    ChooseAdditionalDirectory: _ => null));
                panelState.Apply(panel);
                panel.FormControls.RuntimeOptions!.SetOptions([
                    new RuntimeLaunchOptionDefinition("--cpu-mask", ["--cpu-mask"], "MASK", "CPU affinity mask", RuntimeLaunchOptionValueKind.Text, []),
                    new RuntimeLaunchOptionDefinition("--numa", ["--numa"], "TYPE", "NUMA strategy", RuntimeLaunchOptionValueKind.Choice, ["distribute", "isolate"])
                ]);
                panelState.ApplyControlState(controlPlan);

                Assert.Equal(28, panel.LaunchSettingsSearchBox.Height);
                Assert.True(panel.RuntimeCombo.MinHeight >= 28);
                panel.LaunchSettingsSearchBox.Text = "context size";
                Assert.All(panelState.LaunchSettingElements["Context size"], element => Assert.Equal(Visibility.Visible, element.Visibility));
                Assert.All(panelState.LaunchSettingElements["Threads"], element => Assert.Equal(Visibility.Collapsed, element.Visibility));
                panel.LaunchSettingsSearchBox.Text = "numa";
                Assert.Equal(Visibility.Visible, panel.FormControls.RuntimeOptions.Root.Visibility);
                panel.LaunchSettingsSearchBox.Text = "no-setting-can-match-this";
                Assert.Equal(Visibility.Collapsed, panel.FormControls.RuntimeOptions.Root.Visibility);

                var viewModel = new MainWindowViewModel();
                viewModel.Overview.ReplaceSessions([RunningSession(settings)]);
                viewModel.Overview.ReplaceLaunchProfiles([
                    new NamedModelLaunchProfile("default:model-1", "model-1", "Default", ModelLaunchSettings.FromAppSettings(settings), DateTimeOffset.UtcNow, true)
                ]);
                var overview = LocalLlmConsole.OverviewPageFactory.Create(new LocalLlmConsole.OverviewPageRequest(
                    viewModel,
                    new LocalLlmConsole.OverviewPageActions(
                        () => Task.CompletedTask,
                        () => Task.CompletedTask,
                        () => Task.CompletedTask,
                        () => Task.CompletedTask,
                        (_, _) => { }),
                    _ => { }));
                overview.LaunchProfileCombo.SelectedIndex = 0;
                overview.Root.Measure(new Size(900, 680));
                overview.Root.Arrange(new Rect(0, 0, 900, 680));
                overview.Root.UpdateLayout();
                Assert.Equal(1, Grid.GetRow(overview.LoadButton));
                Assert.Equal(1, Grid.GetRowSpan(overview.LoadButton));
                Assert.InRange(overview.LoadButton.ActualHeight, 28, 36);
                var launchProfileText = VisualDescendants<TextBlock>(overview.LaunchProfileCombo)
                    .Select(text => text.Text)
                    .ToArray();
                Assert.Contains("Default", launchProfileText);
                Assert.DoesNotContain(launchProfileText, text => text.Contains(nameof(OverviewLaunchProfileChoice), StringComparison.Ordinal));
                Assert.Equal(8, overview.LoadedSessionsGrid.Columns.Count);
                Assert.Equal("Default", viewModel.Overview.SessionRows[0].C2);
                Assert.Equal("Unload", viewModel.Overview.SessionRows[0].C8);
                Assert.True(viewModel.Overview.SessionRows[0].B1);

                var alternateSettings = ModelLaunchSettings.FromAppSettings(settings with { Port = 8099 });
                var alternateProfile = new NamedModelLaunchProfile(
                    "profile:model-1:alternate",
                    "model-1",
                    "Alternate",
                    alternateSettings,
                    DateTimeOffset.UtcNow);
                var modelRow = new ModelGridRow { Name = "Qwen", Quant = "Q4_K_M", Size = "4 GiB", Model = RunningModel() };
                var profileRow = new ModelGridRow
                {
                    Name = alternateProfile.Name,
                    Quant = "Profile",
                    Size = "4 GiB",
                    Model = modelRow.Model,
                    LaunchProfile = alternateProfile
                };
                var modelGrid = new DataGrid { ItemsSource = new[] { modelRow } };
                var profileGrid = new DataGrid { ItemsSource = new[] { profileRow } };
                var modelsState = new LocalLlmConsole.ModelsPageState();
                modelsState.Apply(new LocalLlmConsole.ModelsPageControls(
                    new Grid(),
                    new TextBlock(),
                    modelGrid,
                    profileGrid,
                    new TextBox(),
                    new DataGrid()));
                modelGrid.SelectedItem = modelRow;
                profileGrid.SelectedItem = profileRow;

                Assert.Equal(modelRow.Model.Id, modelsState.SelectedModel?.Id);
                Assert.Equal(alternateProfile.Id, modelsState.SelectedLaunchProfileId);
                Assert.Equal(8099, modelsState.SelectedLaunchProfile?.Settings.Port);

                LocalLlmConsole.MetricCardFactory.SetMetricText(
                    overview.RuntimeDashboardGpu,
                    "CPU: AMD Ryzen 9 7950X\nTelemetry: 18.5% load | 16C/32T | 57.2 °C thermal\nGPU 0: AMD Radeon RX 7900 XTX | 53.4% | 8.0/24.0 GiB");
                Assert.Equal(3, overview.RuntimeDashboardGpu.RowDefinitions.Count);

                var metricCards = new[]
                {
                    overview.RuntimeDashboardModel,
                    overview.RuntimeDashboardGpu,
                    overview.RuntimeDashboardKvCache,
                    overview.RuntimeDashboardTokens,
                    overview.RuntimeDashboardMtpTokens,
                    overview.RuntimeDashboardSlots
                }.Select(MetricCard).ToArray();
                Assert.All(metricCards, card => Assert.Equal(104, card.Height));
                var metricDashboard = Assert.IsType<Grid>(metricCards[0].Parent);
                Assert.Equal(2, metricDashboard.ColumnDefinitions.Count);

                overview.RuntimeDashboardTokensGraph.Push("model|runtime|8081", 35, 17.5);
                overview.RuntimeDashboardMtpTokensGraph.Push("model|runtime|8081", 4.5, 3.5);
                overview.RuntimeDashboardKvCacheGraph.Push("model|runtime|8081", 47.25);
                LocalLlmConsole.MetricCardFactory.SetMetricText(overview.RuntimeDashboardTokens, "Gen 35 t/s\nPrompt 17.5 t/s");
                LocalLlmConsole.MetricCardFactory.SetMetricText(overview.RuntimeDashboardMtpTokens, "Inactive");
                LocalLlmConsole.MetricCardFactory.SetMetricText(overview.RuntimeDashboardKvCache, "Used 8,192 t | 50%\nCapacity 16,384 t | unified");
                overview.Root.UpdateLayout();
                Assert.Equal(1, overview.RuntimeDashboardTokensGraph.SampleCount);
                Assert.Equal(1, overview.RuntimeDashboardMtpTokensGraph.SampleCount);
                Assert.Equal(1, overview.RuntimeDashboardKvCacheGraph.SampleCount);
                var graphOffsets = new[]
                {
                    (Graph: overview.RuntimeDashboardTokensGraph, Card: MetricCard(overview.RuntimeDashboardTokens)),
                    (Graph: overview.RuntimeDashboardMtpTokensGraph, Card: MetricCard(overview.RuntimeDashboardMtpTokens)),
                    (Graph: overview.RuntimeDashboardKvCacheGraph, Card: MetricCard(overview.RuntimeDashboardKvCache))
                }.Select(item => item.Graph.TranslatePoint(new Point(0, 0), item.Card).Y).ToArray();
                Assert.All(graphOffsets, offset => Assert.Equal(graphOffsets[0], offset, precision: 1));
                Assert.All(
                    new[]
                    {
                        overview.RuntimeDashboardTokens,
                        overview.RuntimeDashboardMtpTokens,
                        overview.RuntimeDashboardKvCache
                    },
                    metric => Assert.All(
                        metric.Children.OfType<TextBlock>(),
                        line => Assert.Equal(2, Grid.GetColumnSpan(line))));

                overview.Root.Measure(new Size(1024, 680));
                overview.Root.Arrange(new Rect(0, 0, 1024, 680));
                overview.Root.UpdateLayout();
                Assert.Equal(3, metricDashboard.ColumnDefinitions.Count);
                Assert.Equal(0, Grid.GetRow(MetricCard(overview.RuntimeDashboardSlots)));
                Assert.Equal(2, Grid.GetColumn(MetricCard(overview.RuntimeDashboardSlots)));
                Assert.Equal(1, Grid.GetRow(MetricCard(overview.RuntimeDashboardTokens)));
                Assert.Equal(1, Grid.GetRow(MetricCard(overview.RuntimeDashboardMtpTokens)));
                Assert.Equal(1, Grid.GetRow(MetricCard(overview.RuntimeDashboardKvCache)));
                Assert.All(
                    new[]
                    {
                        overview.RuntimeDashboardTokensGraph,
                        overview.RuntimeDashboardMtpTokensGraph,
                        overview.RuntimeDashboardKvCacheGraph
                    },
                    graph => Assert.Equal(28, graph.ActualHeight));
                for (var sample = 0; sample < 65; sample++)
                    overview.RuntimeDashboardTokensGraph.Push("model|runtime|8081", sample, sample / 2.0);
                Assert.Equal(60, overview.RuntimeDashboardTokensGraph.SampleCount);
                overview.RuntimeDashboardTokensGraph.Push("other|runtime|8082", 1, 2);
                Assert.Equal(1, overview.RuntimeDashboardTokensGraph.SampleCount);

                overview.Root.Measure(new Size(580, 900));
                overview.Root.Arrange(new Rect(0, 0, 580, 900));
                overview.Root.UpdateLayout();
                Assert.Single(metricDashboard.ColumnDefinitions);
                Assert.All(metricCards, card => Assert.Equal(104, card.ActualHeight));

                var persistedSettings = settings with { ModelApiKey = "persisted-key" };
                var settingDefinitions = new SettingsPageDefinitionService().BuildRows(persistedSettings);
                var settingsViewModel = new SettingsPageViewModel();
                settingsViewModel.ReplaceRows(settingDefinitions);
                var settingsControls = LocalLlmConsole.SettingsPageFactory.Create(new LocalLlmConsole.SettingsPageRequest(
                    settingsViewModel.Rows,
                    persistedSettings.ThemeMode,
                    new LocalLlmConsole.SettingsPageActions(
                        (_, _) => { },
                        (_, _) => { },
                        (_, _) => { },
                        (_, _) => { },
                        (_, _) => { }),
                    text => text));
                var settingsState = new LocalLlmConsole.SettingsPageState();
                settingsState.Apply(
                    settingsControls,
                    settingsViewModel.Rows,
                    settingDefinitions.ToDictionary(definition => definition.Key, definition => definition.Value, StringComparer.OrdinalIgnoreCase),
                    persistedSettings.ThemeMode);
                Assert.False(settingsState.HasUnsavedChanges);
                Assert.False(settingsControls.SaveButton.IsEnabled);

                var idleRow = Assert.Single(settingsViewModel.Rows, row => row.Key == "autoUnloadIdleMinutes");
                var savedIdleValue = idleRow.Value;
                idleRow.Value = "15";
                Assert.True(settingsState.HasUnsavedChanges);
                Assert.True(settingsControls.SaveButton.IsEnabled);
                idleRow.Value = savedIdleValue;
                Assert.False(settingsState.HasUnsavedChanges);
                Assert.False(settingsControls.SaveButton.IsEnabled);

                settingsControls.ThemeCombo.SelectedItem = "dark";
                Assert.True(settingsState.HasUnsavedChanges);
                Assert.True(settingsControls.SaveButton.IsEnabled);
            }
            finally
            {
                typeof(LocalLlmConsole.MainWindow)
                    .GetMethod("DisposeTrayIcon", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
            }
        });
    }

    private static LoadedModelSessionSnapshot RunningSession(AppSettings settings)
        => new(
            "session-1",
            "model-1",
            "Qwen",
            "runtime-1",
            "Official CPU",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            settings,
            "runtime.log",
            DateTimeOffset.UtcNow,
            "",
            123,
            LoadedModelSessionStatus.Running,
            IsRunning: true,
            IsSelected: true,
            LaunchProfileId: "default:model-1",
            LaunchProfileName: "Default");

    private static ModelRecord RunningModel()
        => new(
            "model-1",
            "Qwen",
            Path.Combine(Path.GetTempPath(), "qwen.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static Border MetricCard(Grid metric)
        => Assert.IsType<Border>(Assert.IsType<StackPanel>(metric.Parent).Parent);

    private static async Task RunStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        thread.Join(TimeSpan.FromSeconds(5));
    }
}
