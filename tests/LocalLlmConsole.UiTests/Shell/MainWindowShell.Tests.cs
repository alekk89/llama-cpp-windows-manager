using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class WpfMainWindowShellTests : WpfUiTestBase
{
    [Fact]
    public async Task MainWindowShellComposesNavigationLocalizationAndEndpointInspectionIndependently()
    {
        await RunStaAsync(() =>
        {
            LocalLlmConsole.Localization.Loc.LoadLanguage("en");
            LocalLlmConsole.ApplicationThemeService.Apply("light");
            var lightAppBackground = Assert.IsType<SolidColorBrush>(Application.Current.Resources["AppBack"]).Color;
            var lightDisabledBackground = Assert.IsType<SolidColorBrush>(Application.Current.Resources["DisabledPrimaryBack"]).Color;
            var lightDisabledForeground = Assert.IsType<SolidColorBrush>(Application.Current.Resources["DisabledPrimaryForeground"]).Color;
            var lightDestructive = Assert.IsType<SolidColorBrush>(Application.Current.Resources["DestructiveAction"]).Color;
            var lightDanger = Assert.IsType<SolidColorBrush>(Application.Current.Resources["Danger"]).Color;
            Assert.NotEqual(lightDisabledBackground, lightDisabledForeground);
            Assert.NotEqual(lightDestructive, lightDanger);

            LocalLlmConsole.ApplicationThemeService.Apply("dark");
            var darkAppBackground = Assert.IsType<SolidColorBrush>(Application.Current.Resources["AppBack"]).Color;
            var darkDisabledBackground = Assert.IsType<SolidColorBrush>(Application.Current.Resources["DisabledPrimaryBack"]).Color;
            var darkDisabledForeground = Assert.IsType<SolidColorBrush>(Application.Current.Resources["DisabledPrimaryForeground"]).Color;
            var darkDestructive = Assert.IsType<SolidColorBrush>(Application.Current.Resources["DestructiveAction"]).Color;
            var darkDanger = Assert.IsType<SolidColorBrush>(Application.Current.Resources["Danger"]).Color;
            Assert.NotEqual(lightAppBackground, darkAppBackground);
            Assert.NotEqual(darkDisabledBackground, darkDisabledForeground);
            Assert.NotEqual(darkDestructive, darkDanger);
            LocalLlmConsole.ApplicationThemeService.Apply("light");

            var window = new LocalLlmConsole.MainWindow
            {
                Width = 1024,
                Height = 680
            };
            DetachLoadedStartup(window);
            try
            {
                var windowContent = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                windowContent.Measure(new Size(1024, 680));
                windowContent.Arrange(new Rect(0, 0, 1024, 680));
                windowContent.UpdateLayout();

                var appStatusText = Assert.IsType<TextBlock>(window.FindName("AppStatusText"));
                var helpButton = Assert.IsType<Button>(window.FindName("HelpNavButton"));
                var appVersionText = Assert.IsType<TextBlock>(window.FindName("AppVersionText"));
                var navigationToggle = Assert.IsType<Button>(window.FindName("NavigationToggleButton"));
                var minimizeButton = Assert.IsType<Button>(window.FindName("MinimizeButton"));
                var maximizeButton = Assert.IsType<Button>(window.FindName("MaximizeButton"));
                var closeButton = Assert.IsType<Button>(window.FindName("CloseButton"));
                var languageCombo = Assert.IsType<ComboBox>(window.FindName("LanguageCombo"));
                var sidebarNavigation = Assert.IsType<Border>(window.FindName("SidebarNavigation"));
                var sidebarColumn = Assert.IsType<ColumnDefinition>(window.FindName("SidebarColumn"));
                var statusCard = Assert.IsType<Border>(Assert.IsType<StackPanel>(appStatusText.Parent).Parent);
                var helpY = helpButton.TranslatePoint(new Point(0, 0), windowContent).Y;
                var statusY = statusCard.TranslatePoint(new Point(0, 0), windowContent).Y;
                Assert.True(statusY > helpY + helpButton.ActualHeight, $"Help bottom {helpY + helpButton.ActualHeight}, status top {statusY}.");
                Assert.True(statusY + statusCard.ActualHeight <= 680, $"Status bottom {statusY + statusCard.ActualHeight}.");
                Assert.Equal("v2.5.0", appVersionText.Text);
                Assert.Equal(28, navigationToggle.Width);
                var navigationToggleGlyph = Assert.IsType<TextBlock>(navigationToggle.Content);
                Assert.Equal("\uE700", navigationToggleGlyph.Text);
                Assert.Equal(13, navigationToggleGlyph.FontSize);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(minimizeButton)));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(maximizeButton)));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(closeButton)));
                Assert.Equal("Language", AutomationProperties.GetName(languageCombo));
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(appStatusText));
                Assert.Equal(244, sidebarColumn.Width.Value);
                Assert.Equal(Visibility.Visible, sidebarNavigation.Visibility);
                navigationToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, sidebarColumn.Width.Value);
                Assert.Equal(Visibility.Collapsed, sidebarNavigation.Visibility);
                Assert.Equal("Expand navigation menu.", navigationToggle.ToolTip);
                navigationToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(244, sidebarColumn.Width.Value);
                Assert.Equal(Visibility.Visible, sidebarNavigation.Visibility);
                Assert.All(
                    new[] { "OverviewNavButton", "ModelsNavButton", "RuntimesNavButton", "HelpNavButton" }
                        .Select(name => Assert.IsType<Button>(window.FindName(name))),
                    button => Assert.True(button.MinHeight >= 40));

                var applyLocalizedStrings = typeof(LocalLlmConsole.MainWindow).GetMethod(
                    "ApplyLocalizedXamlStrings",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(applyLocalizedStrings);
                LocalLlmConsole.Localization.Loc.LoadLanguage("ar");
                applyLocalizedStrings.Invoke(window, null);
                Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
                var copiedEndpointValue = "";
                const string endpointApiKey = "endpoint-report-test-api-key-1234567890";
                var endpointDialog = LocalLlmConsole.EndpointInspectionDialogFactory.Create(
                    window,
                    new EndpointInspectionReport(
                        EndpointInspectionKind.Gateway,
                        "Gateway",
                        "http://127.0.0.1:8082/v1",
                        "Ready",
                        DateTimeOffset.UtcNow,
                        [new EndpointInspectionModel("route-id", "Route", "manager", "default", null, 32768, null, 7_000_000_000, 4_000_000_000)],
                        null,
                        [],
                        [],
                        "Keep loaded",
                        "Loopback",
                        []),
                    endpointApiKey,
                    value => copiedEndpointValue = value);
                var endpointDialogContent = Assert.IsAssignableFrom<FrameworkElement>(endpointDialog.Content);
                endpointDialogContent.Measure(new Size(760, 560));
                endpointDialogContent.Arrange(new Rect(0, 0, 760, 560));
                endpointDialogContent.UpdateLayout();
                var endpointDialogRoot = Assert.IsAssignableFrom<DependencyObject>(endpointDialogContent);
                Assert.Equal(FlowDirection.RightToLeft, endpointDialog.FlowDirection);
                Assert.Contains("فحص", endpointDialog.Title, StringComparison.Ordinal);
                Assert.Contains(
                    VisualDescendants<TextBlock>(endpointDialogRoot),
                    text => text.Text.Contains("تقرير", StringComparison.Ordinal));
                var selectableEndpoint = Assert.Single(
                    VisualDescendants<TextBox>(endpointDialogRoot),
                    textBox => textBox.Text == "http://127.0.0.1:8082/v1");
                Assert.True(selectableEndpoint.IsReadOnly);
                Assert.False(selectableEndpoint.IsReadOnlyCaretVisible);
                Assert.Same(
                    System.Windows.Application.Current.Resources["KeyboardFocusVisual"],
                    selectableEndpoint.FocusVisualStyle);
                Assert.Equal(new Thickness(0), selectableEndpoint.BorderThickness);
                Assert.Equal(System.Windows.Media.Brushes.Transparent, selectableEndpoint.Background);
                Assert.Equal(0, selectableEndpoint.MinHeight);
                var endpointTable = Assert.Single(VisualDescendants<DataGrid>(endpointDialogRoot));
                Assert.Equal(DataGridSelectionMode.Extended, endpointTable.SelectionMode);
                Assert.Equal(DataGridSelectionUnit.CellOrRowHeader, endpointTable.SelectionUnit);
                Assert.Equal(DataGridClipboardCopyMode.IncludeHeader, endpointTable.ClipboardCopyMode);
                var copyButtons = VisualDescendants<Button>(endpointDialogRoot)
                    .Where(button => AutomationProperties.GetAutomationId(button).StartsWith("EndpointCopy", StringComparison.Ordinal))
                    .ToDictionary(AutomationProperties.GetAutomationId, StringComparer.Ordinal);
                copyButtons["EndpointCopyEndpointButton"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("http://127.0.0.1:8082/v1", copiedEndpointValue);
                copyButtons["EndpointCopyReportButton"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("http://127.0.0.1:8082/v1", copiedEndpointValue, StringComparison.Ordinal);
                Assert.Contains("route-id", copiedEndpointValue, StringComparison.Ordinal);
                Assert.DoesNotContain(endpointApiKey, copiedEndpointValue, StringComparison.Ordinal);
                copyButtons["EndpointCopyApiKeyButton"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(endpointApiKey, copiedEndpointValue);
                endpointDialog.Close();
                LocalLlmConsole.Localization.Loc.LoadLanguage("en");
                applyLocalizedStrings.Invoke(window, null);
                Assert.Equal(FlowDirection.LeftToRight, window.FlowDirection);

                Assert.Null(window.FindName("ControlApiNavButton"));

                var deferredCloseRan = false;
                LocalLlmConsole.WindowCloseScheduler.Schedule(
                    System.Windows.Threading.Dispatcher.CurrentDispatcher,
                    () => deferredCloseRan = true);
                Assert.False(deferredCloseRan);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.True(deferredCloseRan);

                var runtimeDiscoveryCancellationField = typeof(LocalLlmConsole.MainWindow).GetField(
                    "_runtimeLaunchOptionDiscoveryCancellation",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var cancelRuntimeDiscovery = typeof(LocalLlmConsole.MainWindow).GetMethod(
                    "CancelRuntimeLaunchOptionDiscovery",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(runtimeDiscoveryCancellationField);
                Assert.NotNull(cancelRuntimeDiscovery);
                runtimeDiscoveryCancellationField.SetValue(window, new CancellationTokenSource());
                cancelRuntimeDiscovery.Invoke(window, null);
                cancelRuntimeDiscovery.Invoke(window, null);
                Assert.Null(runtimeDiscoveryCancellationField.GetValue(window));

            }
            finally
            {
                typeof(LocalLlmConsole.MainWindow)
                    .GetMethod("DisposeTrayIcon", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
            }
        });
    }
}
