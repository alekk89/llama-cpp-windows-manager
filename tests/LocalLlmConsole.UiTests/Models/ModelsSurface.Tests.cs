using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class WpfModelsSurfaceTests : WpfUiTestBase
{
    [Fact]
    public async Task ModelsSurfaceRendersProfilesGroupsAndVisibilityIndependently()
    {
        await RunStaAsync(() =>
        {
            var settings = AppSettings.CreateDefault(Path.Combine(Path.GetTempPath(), "wpf-models-smoke"));
            var viewModel = new MainWindowViewModel();
            var alternateSettings = ModelLaunchSettings.FromAppSettings(settings with { Port = 8099 });
            var alternateProfile = new NamedModelLaunchProfile(
                "profile:model-1:alternate",
                "model-1",
                "Alternate",
                alternateSettings,
                DateTimeOffset.UtcNow);
            var ungroupedProfile = alternateProfile with
            {
                Id = "profile:model-1:ungrouped",
                Name = "Ungrouped",
                Settings = alternateSettings with { Port = 8100 }
            };
            var modelRow = new ModelGridRow { Name = "Qwen", Quant = "Q4_K_M", Size = "4 GiB", Model = RunningModel() };
            var nonFavoriteModel = modelRow.Model with
            {
                Id = "model-2",
                Name = "Llama",
                ModelPath = Path.Combine(Path.GetTempPath(), "llama.gguf")
            };
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
                new LocalLlmConsole.DataGridSearchControls(new Grid(), new TextBox(), new Button()),
                new LocalLlmConsole.DataGridSearchControls(new Grid(), new TextBox(), new Button()),
                new Grid(),
                new GridSplitter(),
                new TextBox(),
                new DataGrid()));
            modelGrid.SelectedItem = modelRow;
            profileGrid.SelectedItem = profileRow;

            Assert.Equal(modelRow.Model.Id, modelsState.SelectedModel?.Id);
            Assert.Equal(alternateProfile.Id, modelsState.SelectedLaunchProfileId);
            Assert.Equal(8099, modelsState.SelectedLaunchProfile?.Settings.Port);
            modelsState.ReleaseView();
            Assert.True(modelsState.SelectedModel is null && !modelsState.HasHuggingFaceGrid);

            viewModel.Models.ReplaceModels(
                [nonFavoriteModel, modelRow.Model],
                _ => false,
                [ungroupedProfile, alternateProfile],
                new Dictionary<string, string>
                {
                    [modelRow.Model.Id] = "4 GiB",
                    [nonFavoriteModel.Id] = "5 GiB"
                },
                new Dictionary<string, ModelGroupRecord>
                {
                    [alternateProfile.Id] = new(
                        "group:interactive", "Interactive", ModelGroupRetentionMode.Pinned, 30,
                        ModelGroupEvictionPriority.High, DateTimeOffset.UtcNow)
                },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { alternateProfile.Id },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ungroupedProfile.Id },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { modelRow.Model.Id });
            Assert.Equal([modelRow.Model.Id, nonFavoriteModel.Id], viewModel.Models.Rows.Select(row => row.Model.Id).ToArray());
            Assert.Equal([alternateProfile.Id, ungroupedProfile.Id], viewModel.Models.VariantRows.Select(row => row.LaunchProfile!.Id).ToArray());
            var launchSettingsPanel = new Grid();
            var startupToggleProfileId = "";
            var modelFavoriteId = "";
            var profileFavoriteId = "";
            var modelPage = LocalLlmConsole.ModelsPageFactory.Create(new LocalLlmConsole.ModelsPageRequest(
                viewModel,
                settings.ModelsRoot,
                launchSettingsPanel,
                new LocalLlmConsole.ModelsPageActions(
                    () => Task.CompletedTask, () => Task.CompletedTask,
                    () => Task.CompletedTask,
                    () => { },
                    () => Task.CompletedTask,
                    (_, _) => Task.CompletedTask,
                    (_, _) => Task.CompletedTask,
                    model => { modelFavoriteId = model.Id; return Task.CompletedTask; },
                    (_, profile) => { profileFavoriteId = profile.Id; return Task.CompletedTask; },
                    (_, profile) => { startupToggleProfileId = profile.Id; return Task.CompletedTask; },
                    (_, _) => Task.CompletedTask,
                    () => { },
                    (_, _) => { },
                    (_, _) => { },
                    (_, _) => { },
                    () => Task.CompletedTask,
                    () => Task.CompletedTask,
                    _ => { })));
            modelPage.Root.Measure(new Size(1024, 680));
            modelPage.Root.Arrange(new Rect(0, 0, 1024, 680));
            modelPage.Root.UpdateLayout();
            var modelSettingsWidthBeforeResize = launchSettingsPanel.ActualWidth;
            var modelSettingsSplitter = Assert.Single(VisualDescendants<GridSplitter>(modelPage.Root),
                splitter => splitter.ResizeDirection == GridResizeDirection.Columns);
            modelSettingsSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent
            });
            modelSettingsSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(-120, 0)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent
            });
            modelSettingsSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(-120, 0, false)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent
            });
            modelPage.Root.UpdateLayout();
            Assert.True(launchSettingsPanel.ActualWidth > modelSettingsWidthBeforeResize + 80,
                $"Model Settings width remained {launchSettingsPanel.ActualWidth:0.#} after starting at {modelSettingsWidthBeforeResize:0.#}.");
            var horizontalFallbacks = VisualDescendants<ScrollViewer>(modelPage.Root)
                .Where(scroll => scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto)
                .ToArray();
            Assert.True(horizontalFallbacks.Length >= 2);
            modelPage.Root.Measure(new Size(624, 680));
            modelPage.Root.Arrange(new Rect(0, 0, 624, 680));
            modelPage.Root.UpdateLayout();
            Assert.Contains(horizontalFallbacks, scroll => scroll.ExtentWidth > scroll.ViewportWidth);
            var liveModelsState = new LocalLlmConsole.ModelsPageState();
            liveModelsState.Apply(modelPage);
            liveModelsState.ApplyUiPreferences(settings);

            Assert.False(settings.ShowModelsHuggingFace);
            Assert.Equal(Visibility.Collapsed, modelPage.HuggingFaceSection.Visibility);
            Assert.Equal(Visibility.Collapsed, modelPage.HuggingFaceSplitter.Visibility);
            Assert.Equal(0, modelPage.Root.RowDefinitions[3].Height.Value);
            liveModelsState.ApplyUiPreferences(settings with { ShowModelsHuggingFace = true });
            Assert.Equal(Visibility.Visible, modelPage.HuggingFaceSection.Visibility);
            Assert.Equal(Visibility.Visible, modelPage.HuggingFaceSplitter.Visibility);
            Assert.Equal(230, modelPage.Root.RowDefinitions[3].Height.Value);
            var favoriteProfileRow = viewModel.Models.VariantRows.Single(row => row.LaunchProfile!.Id == alternateProfile.Id);
            var startupProfileRow = viewModel.Models.VariantRows.Single(row => row.LaunchProfile!.Id == ungroupedProfile.Id);
            Assert.Equal("Add", startupProfileRow.GroupAction);
            Assert.True(startupProfileRow.CanAssignGroup);
            Assert.Equal("Interactive", favoriteProfileRow.Group);
            Assert.Equal("", favoriteProfileRow.GroupAction);
            Assert.Equal("Click Interactive to change or remove this group assignment.", favoriteProfileRow.GroupToolTip);
            Assert.False(favoriteProfileRow.CanAssignGroup);
            Assert.False(startupProfileRow.IsFavorite);
            Assert.True(favoriteProfileRow.IsFavorite);
            Assert.True(startupProfileRow.IsLoadOnStartup);
            Assert.False(favoriteProfileRow.IsLoadOnStartup);
            Assert.DoesNotContain(modelPage.ModelsGrid.Columns, column => Equals(column.Header, "Group"));
            Assert.Contains(modelPage.ModelVariantsGrid.Columns, column => Equals(column.Header, "Group"));
            Assert.Contains(modelPage.ModelsGrid.Columns, column => Equals(column.Header, "Folder"));
            Assert.DoesNotContain(modelPage.ModelsGrid.Columns, column => Equals(column.Header, "Open Folder"));
            Assert.DoesNotContain(modelPage.ModelVariantsGrid.Columns, column => Equals(column.Header, "Folder"));
            Assert.Equal(48, Assert.IsType<LocalLlmConsole.FlexibleTextDataGridColumn>(modelPage.ModelsGrid.Columns[1]).MinWidth);
            Assert.Equal(48, Assert.IsType<LocalLlmConsole.FlexibleActionDataGridColumn>(modelPage.ModelsGrid.Columns[4]).MinWidth);
            Assert.Equal(36, Assert.IsType<LocalLlmConsole.ResponsiveActionDataGridColumn>(modelPage.ModelsGrid.Columns[^1]).MinWidth);
            Assert.Equal(36, Assert.IsType<LocalLlmConsole.ResponsiveActionDataGridColumn>(modelPage.ModelVariantsGrid.Columns[^1]).MinWidth);
            AssertContextMenu(modelPage.ModelsGrid, viewModel.Models.Rows[0], "Remove from favorites", "Open Folder", "Save New Profile", "Delete");
            AssertContextMenu(modelPage.ModelVariantsGrid, favoriteProfileRow, "Load", "Remove from favorites", "Load on startup", "Change group…", "Remove from group", "Remove");
            Assert.Equal(Visibility.Collapsed, modelPage.ModelsSearch.Input.Visibility);
            Assert.Equal(Visibility.Collapsed, modelPage.LaunchProfilesSearch.Input.Visibility);
            modelPage.ModelsGrid.SelectedItem = viewModel.Models.Rows[0];
            modelPage.ModelsGrid.ScrollIntoView(viewModel.Models.Rows[0]);
            modelPage.ModelsGrid.UpdateLayout();
            var modelDataRow = Assert.IsType<DataGridRow>(modelPage.ModelsGrid.ItemContainerGenerator.ContainerFromItem(viewModel.Models.Rows[0]));
            var folderButton = VisualDescendants<LocalLlmConsole.ResponsiveActionButton>(modelDataRow)
                .Single(button => button.FullLabel == "Open");
            Assert.Equal("Open", System.Windows.Automation.AutomationProperties.GetName(folderButton));
            modelPage.ModelsGrid.Columns[4].Width = new DataGridLength(48);
            modelPage.ModelsGrid.UpdateLayout();
            Assert.Equal("\uE8B7", folderButton.Content);
            Assert.Equal("Open", System.Windows.Automation.AutomationProperties.GetName(folderButton));
            modelPage.ModelsGrid.Columns[4].Width = new DataGridLength(100);
            modelPage.ModelsGrid.UpdateLayout();
            Assert.Equal("Open", folderButton.Content);
            modelPage.ModelsGrid.Columns[^1].Width = new DataGridLength(36);
            modelPage.ModelsGrid.UpdateLayout();
            var deleteButton = VisualDescendants<LocalLlmConsole.ResponsiveActionButton>(modelDataRow)
                .Single(button => button.FullLabel == "Delete");
            Assert.Equal("×", deleteButton.Content);
            modelPage.ModelsGrid.Columns[^1].Width = new DataGridLength(100);
            modelPage.ModelsGrid.UpdateLayout();
            Assert.Equal("Delete", deleteButton.Content);
            var modelFavoriteButton = VisualDescendants<Button>(modelDataRow).Single(button => Equals(button.Content, "★"));
            AssertVerticallyCentered(modelFavoriteButton, modelDataRow);
            var modelFavoriteCell = Assert.IsType<DataGridCell>(LocalLlmConsole.VisualTreeTraversal.FindAncestor<DataGridCell>(modelFavoriteButton));
            Assert.Equal(0, Assert.IsType<System.Windows.Media.SolidColorBrush>(modelFavoriteCell.Background).Color.A);
            Assert.Same(System.Windows.Application.Current.TryFindResource("Accent"), modelFavoriteButton.Foreground);
            modelFavoriteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(modelRow.Model.Id, modelFavoriteId);
            modelPage.ModelsGrid.ContextMenu!.IsOpen = true;
            modelPage.ModelsGrid.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Remove from favorites"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(modelRow.Model.Id, modelFavoriteId);
            modelPage.ModelsGrid.ContextMenu.IsOpen = false;
            modelPage.ModelsSearch.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            modelPage.ModelsSearch.Input.Text = "not-a-model";
            Assert.Empty(modelPage.ModelsGrid.Items.Cast<ModelGridRow>());
            modelPage.ModelsSearch.Input.Text = "";
            Assert.Equal(2, modelPage.ModelsGrid.Items.Cast<ModelGridRow>().Count());
            modelPage.LaunchProfilesSearch.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            modelPage.LaunchProfilesSearch.Input.Text = "Ungrouped";
            Assert.Equal("Ungrouped", Assert.Single(modelPage.ModelVariantsGrid.Items.Cast<ModelGridRow>()).Name);
            modelPage.LaunchProfilesSearch.Input.Text = "";
            modelPage.ModelVariantsGrid.SelectedItem = startupProfileRow;
            modelPage.ModelVariantsGrid.ContextMenu!.IsOpen = true;
            var loadOnStartup = modelPage.ModelVariantsGrid.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Don't load on startup"));
            Assert.False(loadOnStartup.IsCheckable);
            loadOnStartup.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(ungroupedProfile.Id, startupToggleProfileId);
            modelPage.ModelVariantsGrid.ContextMenu.IsOpen = false;
            modelPage.ModelVariantsGrid.ScrollIntoView(favoriteProfileRow);
            modelPage.ModelVariantsGrid.UpdateLayout();
            var profileDataRow = Assert.IsType<DataGridRow>(modelPage.ModelVariantsGrid.ItemContainerGenerator.ContainerFromItem(favoriteProfileRow));
            var profileFavoriteButton = VisualDescendants<Button>(profileDataRow).Single(button => Equals(button.Content, "★"));
            AssertVerticallyCentered(profileFavoriteButton, profileDataRow);
            profileFavoriteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(alternateProfile.Id, profileFavoriteId);
            Assert.Contains(VisualDescendants<Button>(modelPage.Root), button => Equals(button.Content, "Groups…"));
            modelPage.ModelVariantsGrid.ScrollIntoView(startupProfileRow);
            modelPage.ModelVariantsGrid.UpdateLayout();
            var inlineGroupButtons = VisualDescendants<Button>(modelPage.ModelVariantsGrid)
                .Where(button => Equals(button.Content, "Add")
                                 && button.Visibility == Visibility.Visible
                                 && ReferenceEquals(button.DataContext, startupProfileRow))
                .ToArray();
            Assert.Single(inlineGroupButtons);
            Assert.Equal(Visibility.Visible, inlineGroupButtons[0].Visibility);
            AssertGridActionButtonMatches(inlineGroupButtons[0], modelPage.ModelVariantsGrid, "Remove");
            var addGroupButtonWidth = inlineGroupButtons[0].ActualWidth;
            modelPage.ModelVariantsGrid.ScrollIntoView(favoriteProfileRow);
            modelPage.ModelVariantsGrid.UpdateLayout();
            var variantButtons = VisualDescendants<Button>(modelPage.ModelVariantsGrid).ToArray();
            var groupNameButtons = variantButtons
                .Where(button => button.Visibility == Visibility.Visible
                                 && LocalLlmConsole.VisualRole.GetButtonRole(button) == LocalLlmConsole.VisualRole.Quiet
                                 && button.DataContext is ModelGridRow row
                                 && !string.IsNullOrWhiteSpace(row.Group)
                                 && Equals(button.Content, row.Group))
                .ToArray();
            Assert.True(groupNameButtons.Length == 1, string.Join(" | ", variantButtons.Select(button =>
                $"{button.Content ?? "<null>"}:{button.Visibility}:{LocalLlmConsole.VisualRole.GetButtonRole(button)}:{(button.DataContext as ModelGridRow)?.Group}")));
            var groupNameButton = groupNameButtons[0];
            Assert.Equal(addGroupButtonWidth, groupNameButton.ActualWidth, precision: 1);
            Assert.Equal(LocalLlmConsole.VisualRole.Quiet, LocalLlmConsole.VisualRole.GetButtonRole(groupNameButton));
            Assert.Equal("Interactive", Assert.IsType<ModelGridRow>(groupNameButton.DataContext).Group);
            Assert.Contains(VisualDescendants<TextBlock>(groupNameButton), text => text.Text == "Interactive");
            groupNameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(
                ["Change group…", "Remove from group"],
                groupNameButton.ContextMenu!.Items.Cast<MenuItem>().Select(item => item.Header).ToArray());
            groupNameButton.ContextMenu.IsOpen = false;
        });
    }
}
