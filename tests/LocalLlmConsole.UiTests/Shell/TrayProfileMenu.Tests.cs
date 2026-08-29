using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class WpfTrayProfileMenuTests : WpfUiTestBase
{
    [Fact]
    public async Task TrayProfileMenuUsesCompactInlineModelExpansionAndSeparateActionButtons()
    {
        await RunStaAsync(() =>
        {
            LocalLlmConsole.Localization.Loc.LoadLanguage("en");
            LocalLlmConsole.ApplicationThemeService.Apply("dark");
            var root = TestWorkspace;
            var model = new ModelRecord(
                "model-a",
                "Alpha",
                Path.Combine(root, "alpha.gguf"),
                OwnershipKind.External,
                "{}",
                DateTimeOffset.UtcNow);
            var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root));
            var defaultProfile = new NamedModelLaunchProfile(
                "profile-default", model.Id, "Default", settings, DateTimeOffset.UtcNow, true);
            var fastProfile = new NamedModelLaunchProfile(
                "profile-fast", model.Id, "Fast", settings, DateTimeOffset.UtcNow);
            var entries = new[]
            {
                new TrayProfileMenuEntry(model, defaultProfile, true, TrayProfileActionKind.Stop, true),
                new TrayProfileMenuEntry(model, fastProfile, false, TrayProfileActionKind.Switch, true)
            };
            var executed = new List<string>();
            var view = LocalLlmConsole.TrayProfileMenuFactory.CreateView(
                new TrayProfileMenuSnapshot(
                    [entries[0]],
                    [new TrayProfileMenuModel(model, entries)]),
                new TrayProfileMenuActions(
                    entry =>
                    {
                        executed.Add(entry.Profile.Id);
                        return Task.CompletedTask;
                    },
                    () => { },
                    () => { }));
            var menu = view.Menu;
            menu.PlacementTarget = new Border();
            menu.IsOpen = true;

            Assert.NotNull(Application.Current.Resources[typeof(ContextMenu)]);
            Assert.True(menu.IsOpen);
            Assert.Equal(System.Windows.FlowDirection.LeftToRight, menu.FlowDirection);
            Assert.False(menu.StaysOpen);
            var firstSeparator = Assert.IsType<Separator>(menu.Items.OfType<Separator>().First());
            var runtimeSeparatorStyle = Assert.IsType<Style>(
                Application.Current.FindResource(MenuItem.SeparatorStyleKey));
            Assert.Equal(typeof(Separator), runtimeSeparatorStyle.TargetType);
            firstSeparator.Style = runtimeSeparatorStyle;
            Assert.Equal(new Thickness(0, 3, 0, 3), firstSeparator.Margin);
            Assert.Equal(HorizontalAlignment.Stretch, firstSeparator.HorizontalAlignment);
            var favoritesHeader = Assert.IsType<MenuItem>(menu.Items[0]);
            Assert.Equal("Favourites", favoritesHeader.Header);
            var favorite = Assert.IsType<MenuItem>(menu.Items[1]);
            Assert.True(favorite.StaysOpenOnClick);
            favorite.ApplyTemplate();
            firstSeparator.ApplyTemplate();
            var separatorLine = Assert.IsType<Border>(
                firstSeparator.Template.FindName("SeparatorLine", firstSeparator));
            var widthBinding = Assert.IsType<System.Windows.Data.Binding>(
                System.Windows.Data.BindingOperations.GetBinding(separatorLine, FrameworkElement.WidthProperty));
            Assert.Equal(typeof(ItemsPresenter), widthBinding.RelativeSource!.AncestorType);
            Assert.Equal(HorizontalAlignment.Stretch, separatorLine.HorizontalAlignment);
            Assert.Equal(0.65, separatorLine.Opacity);
            Assert.Same(Application.Current.Resources["PanelBorder"], separatorLine.Background);
            var favoriteCheckColumn = Assert.IsType<ColumnDefinition>(
                favorite.Template.FindName("CheckColumn", favorite));
            var favoriteSubmenuColumn = Assert.IsType<ColumnDefinition>(
                favorite.Template.FindName("SubmenuColumn", favorite));
            Assert.Equal(new GridLength(0), favoriteCheckColumn.Width);
            Assert.Equal(new GridLength(0), favoriteSubmenuColumn.Width);
            var favoriteHeader = Assert.IsType<Grid>(favorite.Header);
            Assert.Contains(VisualDescendants<TextBlock>(favoriteHeader), text => text.Text == "Alpha");
            Assert.Contains(VisualDescendants<TextBlock>(favoriteHeader), text => text.Text.Contains("Default", StringComparison.Ordinal));
            var favoriteButton = Assert.Single(VisualDescendants<Button>(favoriteHeader));
            var stopIcon = Assert.IsType<Border>(favoriteButton.Content);
            Assert.Equal(8, stopIcon.Width);
            Assert.Equal(8, stopIcon.Height);
            Assert.Equal(28, favoriteButton.Width);
            Assert.Equal(26, favoriteButton.Height);
            Assert.Equal(LocalLlmConsole.VisualRole.Danger, LocalLlmConsole.VisualRole.GetButtonRole(favoriteButton));

            Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Models"));
            var alpha = menu.Items.OfType<MenuItem>()
                .Single(item => AutomationProperties.GetName(item) == "Alpha");
            Assert.Empty(alpha.Items);
            Assert.True(alpha.StaysOpenOnClick);
            var alphaHeader = Assert.IsType<Grid>(alpha.Header);
            Assert.Contains(VisualDescendants<TextBlock>(alphaHeader), text => text.Text == "+");
            var collapsedCount = menu.Items.Count;

            alpha.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(collapsedCount + 2, menu.Items.Count);
            Assert.Contains(VisualDescendants<TextBlock>(alphaHeader), text => text.Text == "−");
            var alphaIndex = menu.Items.IndexOf(alpha);
            var expandedProfiles = menu.Items.Cast<object>()
                .Skip(alphaIndex + 1)
                .Take(2)
                .Cast<MenuItem>()
                .ToArray();
            var fast = expandedProfiles
                .Single(item => AutomationProperties.GetName(item) == "Fast");
            Assert.True(fast.StaysOpenOnClick);
            var fastHeader = Assert.IsType<Grid>(fast.Header);
            Assert.Equal(new Thickness(14, 0, 0, 0), fastHeader.Margin);
            var fastButton = Assert.Single(VisualDescendants<Button>(fastHeader));
            var switchIcon = Assert.IsType<TextBlock>(fastButton.Content);
            Assert.Equal("↻", switchIcon.Text);
            Assert.Equal(LocalLlmConsole.VisualRole.Primary, LocalLlmConsole.VisualRole.GetButtonRole(fastButton));
            fastButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([fastProfile.Id], executed);
            Assert.True(menu.IsOpen);

            var loadingEntries = entries
                .Select(entry => entry with { Action = TrayProfileActionKind.Loading, CanExecute = false })
                .ToArray();
            view.Refresh(new TrayProfileMenuSnapshot(
                [loadingEntries[0]],
                [new TrayProfileMenuModel(model, loadingEntries)]));

            var refreshedAlpha = menu.Items.OfType<MenuItem>()
                .Single(item => AutomationProperties.GetName(item) == "Alpha");
            var refreshedAlphaIndex = menu.Items.IndexOf(refreshedAlpha);
            var refreshedProfiles = menu.Items.Cast<object>()
                .Skip(refreshedAlphaIndex + 1)
                .Take(2)
                .Cast<MenuItem>()
                .ToArray();
            Assert.Equal(2, refreshedProfiles.Length);
            Assert.All(refreshedProfiles, item =>
            {
                var header = Assert.IsType<Grid>(item.Header);
                var action = Assert.Single(VisualDescendants<Button>(header));
                Assert.False(action.IsEnabled);
                Assert.Equal("…", Assert.IsType<TextBlock>(action.Content).Text);
            });

            refreshedAlpha.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(collapsedCount, menu.Items.Count);
            var collapsedAlphaHeader = Assert.IsType<Grid>(refreshedAlpha.Header);
            Assert.Contains(VisualDescendants<TextBlock>(collapsedAlphaHeader), text => text.Text == "+");
            Assert.True(menu.IsOpen);
            menu.IsOpen = false;
        });
    }
}
